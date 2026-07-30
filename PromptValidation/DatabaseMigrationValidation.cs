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
                firstUpgrade = Version(upgraded) == 3 && HasMetadataTable(upgraded);
                upgraded.Execute(
                    "INSERT INTO app_metadata (key, value) VALUES ('preserved', 'value');");
                // DDL成功後、version更新前に中断した状態。IF NOT EXISTSで再実行して保持する。
                upgraded.Execute("PRAGMA user_version=2;");
            }

            using var resumed = new Database(file);
            bool resumedUpgrade = Version(resumed) == 3 &&
                resumed.Read(
                    "SELECT value FROM app_metadata WHERE key = 'preserved';",
                    reader => reader.Read() && reader.GetString(0) == "value");
            bool passed = firstUpgrade && resumedUpgrade;
            Console.WriteLine("DB移行（v2→v3中断再開・metadata保持）: " +
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

    private static int Version(Database database)
        => database.Read("PRAGMA user_version;", reader => reader.Read() ? reader.GetInt32(0) : 0);

    private static bool HasMetadataTable(Database database)
        => database.Read(
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'app_metadata';",
            reader => reader.Read());
}
