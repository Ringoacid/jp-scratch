using Microsoft.Data.Sqlite;
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

        if (version < 1)
        {
            ExecuteInternal("""
                CREATE TABLE tabs (
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
                CREATE INDEX idx_tabs_sort    ON tabs(sort_order);
                CREATE INDEX idx_tabs_deleted ON tabs(deleted_at);
                """);
            ExecuteInternal("PRAGMA user_version=1;");
        }

        if (version < 2)
        {
            ExecuteInternal("""
                CREATE TABLE api_calls (
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
                CREATE INDEX idx_api_calls_at ON api_calls(called_at);

                CREATE TABLE reactions (
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
                CREATE INDEX idx_reactions_at  ON reactions(reacted_at);
                CREATE INDEX idx_reactions_rxn ON reactions(reaction);

                CREATE TABLE style_guides (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    generated_at     TEXT    NOT NULL,
                    content          TEXT    NOT NULL,
                    source_reactions INTEGER NOT NULL,
                    is_active        INTEGER NOT NULL DEFAULT 0,
                    is_user_edited   INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE fx_rates (
                    rate_date  TEXT PRIMARY KEY,
                    usd_jpy    REAL NOT NULL,
                    fetched_at TEXT NOT NULL
                );
                """);
            ExecuteInternal("PRAGMA user_version=2;");
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
            using var cmd = CreateCommand(sql);
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
    }

    public T Read<T>(string sql, Func<SqliteDataReader, T> projector, params (string Name, object? Value)[] parameters)
    {
        lock (_gate)
        {
            using var cmd = CreateCommand(sql);
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var reader = cmd.ExecuteReader();
            return projector(reader);
        }
    }

    /// <summary>複数の書き込みをまとめる。タブの並べ替えのように N 行を一括更新する場面で使う。</summary>
    public void InTransaction(Action<Database> body)
    {
        lock (_gate)
        {
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

    public void Dispose()
    {
        try
        {
            ExecuteInternal("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch (SqliteException) { }

        _connection.Dispose();
        SqliteConnection.ClearAllPools();
    }
}
