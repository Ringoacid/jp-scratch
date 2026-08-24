using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;

namespace JpScratch.Services;

internal sealed record PreparedBackup(
    string StagingDirectory,
    string SourceFilePath,
    string AppVersion,
    bool IncludesCredentials,
    int IncludedFileCount);

internal sealed record RestoreResult(string? PreviousDataDirectory);

/// <summary>
/// バックアップの検証・安全な展開・復元を担当する。
/// 復元はDB接続を閉じたアプリ終了後に実行する前提で、現在のデータは別名へ退避して残す。
/// </summary>
internal static class BackupRestoreService
{
    private const string BackupFormat = "jpscratch-backup-v1";
    private const string BackupManifestFileName = "backup-info.json";
    private const int MaximumEntryCount = 10_000;
    private const long MaximumUncompressedBytes = 4L * 1024 * 1024 * 1024;

    public static PreparedBackup Prepare(string backupFile)
    {
        string source = Path.GetFullPath(backupFile);
        if (!File.Exists(source))
            throw new FileNotFoundException("バックアップファイルが見つかりません。", source);

        string staging = Path.Combine(
            Path.GetTempPath(), "JpScratchRestore", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(staging);

            using ZipArchive archive = ZipFile.OpenRead(source);
            if (archive.Entries.Count == 0)
                throw new InvalidDataException("バックアップにファイルが含まれていません。");
            if (archive.Entries.Count > MaximumEntryCount)
                throw new InvalidDataException("バックアップ内のファイル数が多すぎます。");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long uncompressedBytes = 0;
            int fileCount = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string relativePath = NormalizeEntryPath(entry.FullName);
                if (!names.Add(relativePath))
                    throw new InvalidDataException($"バックアップ内に重複したファイルがあります: {relativePath}");

                if (entry.Length > MaximumUncompressedBytes - uncompressedBytes)
                    throw new InvalidDataException("バックアップの展開サイズが大きすぎます。");
                uncompressedBytes += entry.Length;

                string destination = SafeCombine(staging, relativePath);
                bool isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                                   entry.FullName.EndsWith("\\", StringComparison.Ordinal);
                if (isDirectory)
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                string? directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                using Stream input = entry.Open();
                using FileStream output = new(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    options: FileOptions.SequentialScan);
                input.CopyTo(output);
                fileCount++;
            }

            string databaseFile = Path.Combine(staging, "app.db");
            string manifestFile = Path.Combine(staging, BackupManifestFileName);
            if (!File.Exists(databaseFile) || !File.Exists(manifestFile))
                throw new InvalidDataException("JP Scratchのバックアップ形式ではありません。");
            ValidateDatabaseHeader(databaseFile);

            (string appVersion, bool includesCredentials) = ReadManifest(manifestFile);
            return new PreparedBackup(
                staging,
                source,
                appVersion,
                includesCredentials,
                fileCount);
        }
        catch
        {
            Discard(staging);
            throw;
        }
    }

    /// <summary>
    /// 検証済みの一時展開先をデータフォルダへ適用する。
    /// 呼び出し前にアプリ本体のDB・本文ファイルのハンドルが閉じている必要がある。
    /// </summary>
    public static RestoreResult RestorePrepared(string stagingDirectory, string dataRoot)
    {
        return RestorePrepared(stagingDirectory, dataRoot, CreateRecoveryDirectoryName);
    }

    /// <summary>退避先の生成を差し替え、退避失敗時のロールバックを自己検証するための入口。</summary>
    internal static RestoreResult RestorePrepared(
        string stagingDirectory,
        string dataRoot,
        Func<string, string> createRecoveryDirectoryName)
    {
        ArgumentNullException.ThrowIfNull(createRecoveryDirectoryName);

        string staging = Path.GetFullPath(stagingDirectory);
        string root = Path.GetFullPath(dataRoot);
        ValidateStagingDirectory(staging);

        // マニフェストは展開・確認にだけ使うバックアップメタデータであり、アプリのデータではない。
        // データルートへ残すと次回バックアップで同名エントリが重複するため、適用前に除く。
        File.Delete(Path.Combine(staging, BackupManifestFileName));

        string? parent = Path.GetDirectoryName(root);
        if (string.IsNullOrWhiteSpace(parent))
            throw new IOException("データフォルダの親フォルダを特定できません。");
        Directory.CreateDirectory(parent);

        string? previous = null;
        bool previousMoved = false;
        bool originalRootExisted = Directory.Exists(root);
        try
        {
            if (File.Exists(root))
                throw new IOException("データフォルダの場所に同名のファイルがあります。");

            if (Directory.Exists(root))
            {
                previous = createRecoveryDirectoryName(root);
                Directory.Move(root, previous);
                previousMoved = true;
            }

            try
            {
                Directory.Move(staging, root);
            }
            catch (IOException)
            {
                // TEMPとAppDataが別ボリュームでも復元できるよう、移動できない場合はコピーする。
                CopyDirectory(staging, root);
                Directory.Delete(staging, recursive: true);
            }

            return new RestoreResult(previous);
        }
        catch (Exception ex)
        {
            // 現行データの退避に成功した場合、または元々データルートが無かった場合だけ、
            // 復元処理が作った可能性のある root を削除する。退避の Directory.Move 自体が
            // 失敗したときの root は現行データそのものなので、絶対に削除してはいけない。
            if (previousMoved || !originalRootExisted)
            {
                try
                {
                    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                }
                catch (Exception rollbackError) when (
                    rollbackError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        "バックアップの復元に失敗し、復元途中のフォルダも削除できませんでした。",
                        new AggregateException(ex, rollbackError));
                }
            }

            if (previousMoved && previous is not null && !Directory.Exists(root))
            {
                try
                {
                    Directory.Move(previous, root);
                }
                catch (Exception rollbackError) when (
                    rollbackError is IOException or UnauthorizedAccessException)
                {
                    throw new IOException(
                        $"バックアップの復元に失敗しました。元のデータは次の場所に残っています。\n{previous}",
                        new AggregateException(ex, rollbackError));
                }
            }

            throw new IOException("バックアップを復元できませんでした。元のデータは変更していません。", ex);
        }
    }

    public static void Discard(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static (string AppVersion, bool IncludesCredentials) ReadManifest(string manifestFile)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestFile));
            JsonElement root = document.RootElement;
            string? format = root.TryGetProperty("format", out JsonElement formatElement)
                ? formatElement.GetString()
                : null;
            if (!string.Equals(format, BackupFormat, StringComparison.Ordinal))
                throw new InvalidDataException("このバックアップは対応していない形式です。");

            string appVersion = root.TryGetProperty("appVersion", out JsonElement versionElement)
                ? versionElement.GetString() ?? "不明"
                : "不明";
            bool includesCredentials = root.TryGetProperty(
                "includesCredentials", out JsonElement credentialsElement) &&
                credentialsElement.ValueKind == JsonValueKind.True;
            return (appVersion, includesCredentials);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("バックアップ情報を読み取れません。", ex);
        }
    }

    private static void ValidateStagingDirectory(string staging)
    {
        if (!Directory.Exists(staging) ||
            !File.Exists(Path.Combine(staging, "app.db")) ||
            !File.Exists(Path.Combine(staging, BackupManifestFileName)))
        {
            throw new InvalidDataException("復元用の一時データが見つからないか、壊れています。");
        }

        _ = ReadManifest(Path.Combine(staging, BackupManifestFileName));
        ValidateDatabaseHeader(Path.Combine(staging, "app.db"));
    }

    private static void ValidateDatabaseHeader(string databaseFile)
    {
        byte[] expected = Encoding.ASCII.GetBytes("SQLite format 3\0");
        byte[] actual = new byte[expected.Length];
        using FileStream stream = File.OpenRead(databaseFile);
        int read = stream.Read(actual, 0, actual.Length);
        if (read != expected.Length || !actual.SequenceEqual(expected))
            throw new InvalidDataException("バックアップ内のapp.dbがSQLiteデータベースではありません。");
    }

    private static string NormalizeEntryPath(string entryName)
    {
        string normalized = entryName.Replace('\\', '/');
        normalized = normalized.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('\0'))
            throw new InvalidDataException("バックアップ内に不正なパスがあります。");
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
        {
            throw new InvalidDataException($"バックアップ内に不正なパスがあります: {entryName}");
        }

        string firstPart = normalized.Split('/')[0];
        if (firstPart.Contains(':', StringComparison.Ordinal))
            throw new InvalidDataException($"バックアップ内に絶対パスがあります: {entryName}");
        return normalized;
    }

    private static string SafeCombine(string root, string relativePath)
    {
        string rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                                   Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"バックアップ内のパスが展開先の外側を指しています: {relativePath}");
        return fullPath;
    }

    private static string CreateRecoveryDirectoryName(string root)
    {
        string parent = Path.GetDirectoryName(root)!;
        string name = Path.GetFileName(root);
        return Path.Combine(
            parent,
            $"{name}.before-restore-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            string? targetDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDirectory)) Directory.CreateDirectory(targetDirectory);
            File.Copy(file, target, overwrite: false);
        }
    }
}
