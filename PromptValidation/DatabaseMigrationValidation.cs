using Microsoft.Data.Sqlite;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class DatabaseMigrationValidation
{
    internal static bool RunSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchDatabaseMigration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "test.db");
        string legacyFile = Path.Combine(directory, "legacy-v4.db");

        try
        {
            using (var database = new Database(file))
            {
                // v2完了直後から、v3 DDL前に中断した状態を再現する。
                database.Execute("DROP TABLE app_metadata;");
                database.Execute("PRAGMA user_version=2;");
            }

            bool firstUpgrade;
            using (var upgraded = new Database(file))
            {
                firstUpgrade = Version(upgraded) == CurrentVersion &&
                    HasMetadataTable(upgraded) && HasTable(upgraded, "api_call_daily") &&
                    HasColumn(upgraded, "api_calls", "original_currency") &&
                    HasColumn(upgraded, "api_calls", "original_cost") &&
                    HasColumn(upgraded, "api_calls", "usd_cost_confirmed");
                upgraded.Execute(
                    "INSERT INTO app_metadata (key, value) VALUES ('preserved', 'value');");
                // DDL成功後、version更新前に中断した状態。IF NOT EXISTSで再実行して保持する。
                upgraded.Execute("PRAGMA user_version=2;");
            }

            using var resumed = new Database(file);
            bool resumedUpgrade = Version(resumed) == CurrentVersion &&
                HasTable(resumed, "api_call_daily") &&
                HasColumn(resumed, "api_calls", "original_currency") &&
                HasColumn(resumed, "api_calls", "original_cost") &&
                HasColumn(resumed, "api_calls", "usd_cost_confirmed") &&
                resumed.Read(
                    "SELECT value FROM app_metadata WHERE key = 'preserved';",
                    reader => reader.Read() && reader.GetString(0) == "value");

            // v4のapi_callsだけを持つDBを直接用意し、v5の列追加そのものも検証する。
            CreateV4Database(legacyFile);
            bool legacyUpgrade;
            using (var legacy = new Database(legacyFile))
            {
                legacyUpgrade = Version(legacy) == CurrentVersion &&
                    HasColumn(legacy, "api_calls", "original_currency") &&
                    HasColumn(legacy, "api_calls", "original_cost") &&
                    HasColumn(legacy, "api_calls", "usd_cost_confirmed") &&
                    legacy.Read(
                        "SELECT usd_cost_confirmed FROM api_calls WHERE usd_cost = '0';",
                        reader => reader.Read() && reader.GetInt32(0) == 1);
                // 列追加後、user_version更新前に中断した状態を再現する。
                legacy.Execute("PRAGMA user_version=4;");
            }

            using var legacyResumed = new Database(legacyFile);
            bool legacyReentry = Version(legacyResumed) == CurrentVersion &&
                HasColumn(legacyResumed, "api_calls", "original_currency") &&
                HasColumn(legacyResumed, "api_calls", "original_cost") &&
                HasColumn(legacyResumed, "api_calls", "usd_cost_confirmed");

            bool passed = firstUpgrade && resumedUpgrade && legacyUpgrade && legacyReentry;
            Console.WriteLine("DB移行（旧版→v5・追加列・中断再開・metadata保持・再入耐性）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary><see cref="Database.Migrate"/> が到達する最新の user_version。</summary>
    private const int CurrentVersion = 5;

    private static int Version(Database database)
        => database.Read("PRAGMA user_version;", reader => reader.Read() ? reader.GetInt32(0) : 0);

    private static bool HasMetadataTable(Database database) => HasTable(database, "app_metadata");

    private static bool HasTable(Database database, string name)
        => database.Read(
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name;",
            reader => reader.Read(),
            ("$name", name));

    private static bool HasColumn(Database database, string table, string column)
        => database.Read(
            "SELECT 1 FROM pragma_table_info($table) WHERE name = $column LIMIT 1;",
            reader => reader.Read(),
            ("$table", table),
            ("$column", column));

    private static void CreateV4Database(string file)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE api_calls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                called_at TEXT NOT NULL,
                trigger_type TEXT NOT NULL,
                model TEXT NOT NULL,
                prompt_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                usd_cost TEXT NOT NULL,
                usd_jpy_rate REAL,
                rate_date TEXT,
                jpy_cost TEXT,
                duration_ms INTEGER NOT NULL,
                status TEXT NOT NULL,
                error_message TEXT,
                suggestion_cnt INTEGER NOT NULL DEFAULT 0,
                discarded_cnt INTEGER NOT NULL DEFAULT 0
            );
            INSERT INTO api_calls (
                called_at, trigger_type, model, prompt_tokens, output_tokens,
                usd_cost, duration_ms, status, suggestion_cnt, discarded_cnt)
            VALUES ('2026-01-01T00:00:00.0000000+09:00', 'manual', 'legacy',
                    0, 0, '0', 0, 'ok', 0, 0);
            PRAGMA user_version=4;
            """;
        command.ExecuteNonQuery();
    }
}
