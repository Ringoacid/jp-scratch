using System.IO.Compression;
using System.IO;
using System.Text.Json;
using JpScratch.Infrastructure;

namespace JpScratch.Services;

internal sealed record BackupResult(string FilePath, int IncludedFileCount);

/// <summary>
/// 本文・設定・DB・資格情報をひとつのバックアップへまとめる。
/// credentials.dat はDPAPIで現在のWindowsユーザーに結び付いているため、同じユーザーでのみ復元できる。
/// </summary>
internal static class BackupService
{
    private const string BackupFormat = "jpscratch-backup-v1";
    private const string BackupManifestFileName = "backup-info.json";

    public static BackupResult Create(Database database, string dataRoot, string destinationFile)
    {
        string root = Path.GetFullPath(dataRoot);
        string sourceDatabase = Path.Combine(root, "app.db");
        string destination = Path.GetFullPath(destinationFile);
        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new IOException("バックアップ先のフォルダを特定できません。");

        Directory.CreateDirectory(destinationDirectory);

        string workingDirectory = Path.Combine(
            Path.GetTempPath(), "JpScratchBackup", Guid.NewGuid().ToString("N"));
        string temporaryDatabase = Path.Combine(workingDirectory, "app.db");
        string temporaryArchive = destination + ".tmp-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(workingDirectory);

        try
        {
            database.BackupTo(temporaryDatabase);

            int count = 0;
            using (ZipArchive archive = ZipFile.Open(temporaryArchive, ZipArchiveMode.Create))
            {
                AddFile(archive, temporaryDatabase, "app.db", ref count);

                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string fullPath = Path.GetFullPath(file);
                    if (fullPath.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.Equals(temporaryArchive, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        fullPath.Equals(sourceDatabase, StringComparison.OrdinalIgnoreCase) ||
                        fullPath.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
                        fullPath.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(root, fullPath);
                    // 復元済みの旧バージョンがマニフェストをデータルートへ残していても、
                    // 同名エントリを二重に作らない。マニフェストはバックアップのメタデータであり、
                    // ユーザーデータではないため、常にこの実行で新しく生成する。
                    if (relative.Equals(BackupManifestFileName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    AddFile(archive, fullPath, relative, ref count);
                }

                string manifest = JsonSerializer.Serialize(new
                {
                    format = BackupFormat,
                    createdAt = DateTimeOffset.Now,
                    appVersion = ReleaseInfo.CurrentVersion,
                    includesCredentials = File.Exists(Path.Combine(root, "credentials.dat")),
                    credentialScope = "Windows DPAPI: 現在のユーザーのみ復号可能",
                }, new JsonSerializerOptions { WriteIndented = true });
                ZipArchiveEntry entry = archive.CreateEntry(BackupManifestFileName);
                using StreamWriter writer = new(entry.Open());
                writer.Write(manifest);
            }

            ReplaceExistingFile(temporaryArchive, destination);
            return new BackupResult(destination, count);
        }
        catch
        {
            TryDelete(temporaryArchive);
            throw;
        }
        finally
        {
            TryDelete(temporaryDatabase);
            try
            {
                if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void AddFile(ZipArchive archive, string source, string entryName, ref int count)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        using Stream input = File.OpenRead(source);
        using Stream output = entry.Open();
        input.CopyTo(output);
        count++;
    }

    private static void ReplaceExistingFile(string source, string destination)
    {
        if (File.Exists(destination))
            File.Replace(source, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        else
            File.Move(source, destination);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
