using System.IO;
using System.Runtime.InteropServices;
using JpScratch.Infrastructure;

namespace JpScratch.Services;

/// <summary>
/// Issue本文へ貼り付ける診断情報。本文、APIキー、ユーザー名などは含めない。
/// </summary>
internal static class DiagnosticInfoService
{
    public static string Create(Database database)
    {
        FileInfo? crashLog = TryGetFileInfo(AppPaths.CrashLogFile);
        FileInfo? databaseFile = TryGetFileInfo(AppPaths.DatabaseFile);

        return string.Join(
            Environment.NewLine,
            "JP Scratch 診断情報",
            $"アプリバージョン: {ReleaseInfo.CurrentVersion}",
            $"プロセスアーキテクチャ: {ReleaseInfo.ProcessArchitecture}",
            $"OS: {RuntimeInformation.OSDescription}",
            $".NET: {Environment.Version}",
            $"データ形式: SQLite user_version={database.SchemaVersion}",
            $"データベース: {(databaseFile is null ? "なし" : $"あり ({databaseFile.Length:N0} bytes)")}",
            $"クラッシュログ: {(crashLog is null ? "なし" : $"あり ({crashLog.Length:N0} bytes)")}");
    }

    private static FileInfo? TryGetFileInfo(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
