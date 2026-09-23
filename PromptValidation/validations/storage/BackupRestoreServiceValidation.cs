using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class BackupRestoreServiceValidation
{
    public static bool RunSelfTests()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "JpScratchBackupRestoreValidation", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        string targetRoot = Path.Combine(root, "target");
        string archiveFile = Path.Combine(root, "backup.jpsbackup");
        string repeatedArchiveFile = Path.Combine(root, "backup-after-restore.jpsbackup");
        PreparedBackup? prepared = null;
        PreparedBackup? repeatedPrepared = null;
        PreparedBackup? failedMovePrepared = null;

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "tabs"));
            File.WriteAllText(Path.Combine(sourceRoot, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(sourceRoot, "tabs", "tab.txt"), "復元テスト");

            using (var database = new Database(Path.Combine(sourceRoot, "app.db")))
                BackupService.Create(database, sourceRoot, archiveFile);

            prepared = BackupRestoreService.Prepare(archiveFile);
            Directory.CreateDirectory(targetRoot);
            File.WriteAllText(Path.Combine(targetRoot, "old-data.txt"), "復元前");

            RestoreResult result = BackupRestoreService.RestorePrepared(
                prepared.StagingDirectory,
                targetRoot);
            prepared = null;

            bool restorePassed = File.Exists(Path.Combine(targetRoot, "app.db")) &&
                                 File.Exists(Path.Combine(targetRoot, "settings.json")) &&
                                 File.ReadAllText(Path.Combine(targetRoot, "tabs", "tab.txt")) == "復元テスト" &&
                                 !File.Exists(Path.Combine(targetRoot, "old-data.txt")) &&
                                 !File.Exists(Path.Combine(targetRoot, "backup-info.json")) &&
                                 result.PreviousDataDirectory is { } previous &&
                                 File.Exists(Path.Combine(previous, "old-data.txt"));
            Console.WriteLine($"バックアップ復元（旧データ退避・マニフェスト除外）: {(restorePassed ? "PASS" : "FAIL")}");

            // 旧バージョンがデータルートへ残したマニフェストを再現する。このファイルを
            // 新しいバックアップへ取り込むと同名エントリが二重になり、Prepareが拒否する。
            File.WriteAllText(Path.Combine(targetRoot, "backup-info.json"), "stale manifest");
            using (var restoredDatabase = new Database(Path.Combine(targetRoot, "app.db")))
                BackupService.Create(restoredDatabase, targetRoot, repeatedArchiveFile);
            repeatedPrepared = BackupRestoreService.Prepare(repeatedArchiveFile);
            bool repeatPassed = repeatedPrepared.IncludedFileCount >= 2;
            Console.WriteLine($"バックアップ復元（復元後の再バックアップ）: {(repeatPassed ? "PASS" : "FAIL")}");
            BackupRestoreService.Discard(repeatedPrepared.StagingDirectory);
            repeatedPrepared = null;

            // 現行データの退避先を既存ディレクトリへ固定し、最初のDirectory.Moveを
            // 確実に失敗させる。失敗後も現行データがその場に残ることを確認する。
            string failedMoveTarget = Path.Combine(root, "failed-move-target");
            string occupiedRecovery = Path.Combine(root, "occupied-recovery");
            Directory.CreateDirectory(failedMoveTarget);
            Directory.CreateDirectory(occupiedRecovery);
            File.WriteAllText(Path.Combine(failedMoveTarget, "must-survive.txt"), "現行データ");
            failedMovePrepared = BackupRestoreService.Prepare(archiveFile);
            bool failedMovePreserved = false;
            try
            {
                BackupRestoreService.RestorePrepared(
                    failedMovePrepared.StagingDirectory,
                    failedMoveTarget,
                    _ => occupiedRecovery);
            }
            catch (IOException)
            {
                failedMovePreserved =
                    File.ReadAllText(Path.Combine(failedMoveTarget, "must-survive.txt")) == "現行データ";
            }
            Console.WriteLine($"バックアップ復元（退避失敗時の現行データ保全）: {(failedMovePreserved ? "PASS" : "FAIL")}");

            if (result.PreviousDataDirectory is { } recoveryDirectory &&
                Directory.Exists(recoveryDirectory))
            {
                Directory.Delete(recoveryDirectory, recursive: true);
            }

            return restorePassed && repeatPassed && failedMovePreserved;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"バックアップ復元（旧データ退避）: FAIL / {exception.Message}");
            return false;
        }
        finally
        {
            if (prepared is not null)
                BackupRestoreService.Discard(prepared.StagingDirectory);
            if (repeatedPrepared is not null)
                BackupRestoreService.Discard(repeatedPrepared.StagingDirectory);
            if (failedMovePrepared is not null)
                BackupRestoreService.Discard(failedMovePrepared.StagingDirectory);
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
