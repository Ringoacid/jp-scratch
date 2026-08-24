using Microsoft.Data.Sqlite;
using System.IO;
using JpScratch.Infrastructure;

namespace JpScratch.Services;

/// <summary>
/// app.db（要件 4.1）への接続とスキーマ移行。
/// 履歴・課金ログ・学習データを入れる器で、本文は入れない。
/// </summary>
internal sealed class Database : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <summary>
    /// Microsoft.Data.Sqlite は、トランザクション実行中のコマンドに Transaction を明示しないと例外を投げる。
    /// <see cref="InTransaction"/> の間だけここに入る。
    /// </summary>
    private SqliteTransaction? _activeTransaction;

    public Database(string? databaseFile = null)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile ?? AppPaths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };

        _connection = new SqliteConnection(csb.ToString());
        _connection.Open();

        // WAL にしておくと、書き込み中の読み取りがブロックされない。
        // synchronous=NORMAL は WAL と組み合わせる限り実用上安全で、書き込みが目に見えて速い。
        ExecuteInternal("PRAGMA journal_mode=WAL;");
        ExecuteInternal("PRAGMA synchronous=NORMAL;");
        ExecuteInternal("PRAGMA foreign_keys=ON;");

        Migrate();
    }

    /// <summary>
    /// user_version を版番号として扱う素朴な移行。
    /// v3（学習）のテーブルはその実装時に版を足して追加する。
    /// </summary>
    private void Migrate()
    {
        var version = Convert.ToInt32(ScalarInternal("PRAGMA user_version;") ?? 0);

        // v1/v2 の DDL も v3/v4 と同じ理由で IF NOT EXISTS で書く。DDL が通った直後・
        // PRAGMA user_version の更新前に中断すると、次回起動で同じ CREATE を踏み、
        // 「table already exists」で起動不能になる（v1/v2 は従来素の CREATE だった）。
        if (version < 1)
        {
            ExecuteInternal("""
                CREATE TABLE IF NOT EXISTS tabs (
                    id            TEXT PRIMARY KEY,
                    title         TEXT NOT NULL,
                    is_auto_title INTEGER NOT NULL DEFAULT 1,
                    sort_order    INTEGER NOT NULL,
                    is_active     INTEGER NOT NULL DEFAULT 0,
                    caret_offset  INTEGER NOT NULL DEFAULT 0,
                    created_at    TEXT NOT NULL,
                    updated_at    TEXT NOT NULL,
                    deleted_at    TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_tabs_sort    ON tabs(sort_order);
                CREATE INDEX IF NOT EXISTS idx_tabs_deleted ON tabs(deleted_at);
                """);
            ExecuteInternal("PRAGMA user_version=1;");
        }

        if (version < 2)
        {
            ExecuteInternal("""
                CREATE TABLE IF NOT EXISTS api_calls (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    called_at      TEXT    NOT NULL,
                    trigger_type   TEXT    NOT NULL,
                    model          TEXT    NOT NULL,
                    prompt_tokens  INTEGER NOT NULL,
                    output_tokens  INTEGER NOT NULL,
                    usd_cost       TEXT    NOT NULL,
                    usd_jpy_rate   REAL,
                    rate_date      TEXT,
                    jpy_cost       TEXT,
                    duration_ms    INTEGER NOT NULL,
                    status         TEXT    NOT NULL,
                    error_message  TEXT,
                    suggestion_cnt INTEGER NOT NULL DEFAULT 0,
                    discarded_cnt  INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_api_calls_at ON api_calls(called_at);

                CREATE TABLE IF NOT EXISTS reactions (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    reacted_at     TEXT NOT NULL,
                    api_call_id    INTEGER REFERENCES api_calls(id),
                    tab_id         TEXT,
                    original       TEXT NOT NULL,
                    suggestion     TEXT NOT NULL,
                    left_context   TEXT,
                    right_context  TEXT,
                    reaction       TEXT NOT NULL,
                    user_reason    TEXT,
                    used_in_prompt INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_reactions_at  ON reactions(reacted_at);
                CREATE INDEX IF NOT EXISTS idx_reactions_rxn ON reactions(reaction);

                CREATE TABLE IF NOT EXISTS style_guides (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    generated_at     TEXT    NOT NULL,
                    content          TEXT    NOT NULL,
                    source_reactions INTEGER NOT NULL,
                    is_active        INTEGER NOT NULL DEFAULT 0,
                    is_user_edited   INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS fx_rates (
                    rate_date  TEXT PRIMARY KEY,
                    usd_jpy    REAL NOT NULL,
                    fetched_at TEXT NOT NULL
                );
                """);
            ExecuteInternal("PRAGMA user_version=2;");
        }

        if (version < 3)
        {
            ExecuteInternal("""
                CREATE TABLE IF NOT EXISTS app_metadata (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """);
            ExecuteInternal("PRAGMA user_version=3;");
        }

        if (version < 4)
        {
            // 保持期限を過ぎた api_calls の明細を圧縮して置き換える日次サマリ（要件 3.6.2）。
            //
            // 粒度に usd_jpy_rate / rate_date を含めるのが要点。ここを落として日×種別×モデル×成否
            // だけにすると、「その日に適用したレート」が失われ、期間合計の
            // ApiCallUsageSummary.DistinctRateCount / SingleUsdJpyRate を明細と同じ規約で
            // 再現できなくなる。レートまで粒度に含めれば、サマリ1行を「同じレートのN件」として
            // 明細行と完全に同じように集計へ流し込める。1日あたり最大でも数行にしかならない。
            //
            // usd_jpy_rate を api_calls と同じ REAL にしてあるのは意図的。金額（usd_cost / jpy_cost）は
            // decimal を壊さないよう TEXT のままだが、レートだけは両テーブルで同じ REAL → decimal の
            // 変換経路を通さないと、同じレートが「別のレート」として二重に数えられうる。
            // IF NOT EXISTS は v3 と同じ理由。DDLが通った直後に user_version の更新前で中断すると、
            // 次回起動で同じ CREATE を踏む（PromptValidation の移行テストが再現している）。
            ExecuteInternal("""
                CREATE TABLE IF NOT EXISTS api_call_daily (
                    day            TEXT    NOT NULL,       -- ローカル日 yyyy-MM-dd
                    trigger_type   TEXT    NOT NULL,
                    model          TEXT    NOT NULL,
                    status         TEXT    NOT NULL,
                    usd_jpy_rate   REAL,
                    rate_date      TEXT,
                    call_cnt       INTEGER NOT NULL,
                    prompt_tokens  INTEGER NOT NULL,
                    output_tokens  INTEGER NOT NULL,
                    usd_cost       TEXT    NOT NULL,       -- decimal を文字列で保持
                    jpy_cost       TEXT,
                    suggestion_cnt INTEGER NOT NULL DEFAULT 0,
                    discarded_cnt  INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_api_call_daily_day ON api_call_daily(day);
                """);
            ExecuteInternal("PRAGMA user_version=4;");
        }

        if (version < 5)
        {
            // USD換算の確定状態と、後から補完できる元通貨額を api_calls に保持する。
            // SQLiteには ALTER TABLE ADD COLUMN IF NOT EXISTS がないため、DDL実行後に
            // user_version の更新前で中断しても再開できるよう、列の存在を確認してから追加する。
            if (!HasColumnInternal("api_calls", "original_currency"))
                ExecuteInternal("ALTER TABLE api_calls ADD COLUMN original_currency TEXT;");
            if (!HasColumnInternal("api_calls", "original_cost"))
                ExecuteInternal("ALTER TABLE api_calls ADD COLUMN original_cost TEXT;");
            if (!HasColumnInternal("api_calls", "usd_cost_confirmed"))
                ExecuteInternal(
                    "ALTER TABLE api_calls ADD COLUMN usd_cost_confirmed INTEGER NOT NULL DEFAULT 1;");

            ExecuteInternal("PRAGMA user_version=5;");
        }
    }

    public SqliteCommand CreateCommand(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _activeTransaction;
        return cmd;
    }

    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = CreateCommand(sql);
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
    }

    public T Read<T>(string sql, Func<SqliteDataReader, T> projector, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var cmd = CreateCommand(sql);
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();
            return projector(reader);
        }
    }

    /// <summary>SQLiteのオンラインバックアップAPIで、一貫性のあるDBスナップショットを作る。</summary>
    public void BackupTo(string destinationFile)
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            string? directory = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = destinationFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                // バックアップ先は直後にZIPへ読み込むため、接続プールに残さない。
                // プールに返却されるだけだとWindows上でapp.dbのハンドルが残り、
                // BackupService.AddFileが共有違反になることがある。
                Pooling = false,
            };

            using var destination = new SqliteConnection(csb.ToString());
            destination.Open();
            _connection.BackupDatabase(destination);
        }
    }

    /// <summary>診断情報へ表示するSQLiteのスキーマ番号。</summary>
    public int SchemaVersion
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return Convert.ToInt32(ScalarInternal("PRAGMA user_version;") ?? 0);
            }
        }
    }

    /// <summary>複数の書き込みをまとめる。タブの並べ替えのように N 行を一括更新する場面で使う。</summary>
    public void InTransaction(Action<Database> body)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            using var tx = _connection.BeginTransaction();
            _activeTransaction = tx;
            try
            {
                body(this);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
            finally
            {
                _activeTransaction = null;
            }
        }
    }

    private void ExecuteInternal(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private object? ScalarInternal(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private bool HasColumnInternal(string tableName, string columnName)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM pragma_table_info($table_name) WHERE name = $column_name LIMIT 1;";
        cmd.Parameters.AddWithValue("$table_name", tableName);
        cmd.Parameters.AddWithValue("$column_name", columnName);
        return cmd.ExecuteScalar() is not null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            try
            {
                ExecuteInternal("PRAGMA wal_checkpoint(TRUNCATE);");
            }
            catch (SqliteException) { }

            _connection.Dispose();
            _disposed = true;
            SqliteConnection.ClearAllPools();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
