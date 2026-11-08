using System.IO.Compression;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class BackupServiceValidation
{
    public static bool RunSelfTests()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "JpScratchBackupValidation", Guid.NewGuid().ToString("N"));
        string databaseFile = Path.Combine(root, "app.db");
        string backupFile = Path.Combine(root, "backup.jpsbackup");

        try
        {
            Directory.CreateDirectory(root);
            BackupResult result;
            using (var database = new Database(databaseFile))
            {
                // 実運用と同じく、SQLiteのWAL接続を開いたままバックアップする。
                result = BackupService.Create(database, root, backupFile);
            }

            using ZipArchive archive = ZipFile.OpenRead(result.FilePath);
            bool hasDatabase = archive.GetEntry("app.db") is not null;
            bool hasManifest = archive.GetEntry("backup-info.json") is not null;
            bool passed = result.IncludedFileCount >= 1 && hasDatabase && hasManifest;
            Console.WriteLine($"バックアップ作成（DBロック中）: {(passed ? "PASS" : "FAIL")}");
            return passed;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"バックアップ作成（DBロック中）: FAIL / {exception.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
