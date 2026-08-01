using System.IO;
using System.Text;

namespace JpScratch.Infrastructure;

/// <summary>
/// 一時ファイル経由で、既存データを壊さずに行うファイル書き込み（要件 3.2.4 / R-8）。
/// 一時ファイルへ書き切ってから <see cref="File.Replace(string,string,string?)"/> で差し替えるので、
/// 書き込み中にプロセスが落ちても既存の本文は無傷で残る。
/// </summary>
internal static class AtomicFile
{
    /// <summary>BOM なし UTF-8。メモ帳・WSL の双方から素直に読める形にしておく。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";

        try
        {
            // WriteThrough でディスクまで落としてから差し替える。
            // ここを省くと、電源断のときに「空の新ファイルで上書き」が起こりうる。
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                                           bufferSize: 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(fs, Utf8NoBom))
            {
                writer.Write(content);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch
        {
            // 失敗経路では .tmp を残さない（既存ファイルは壊れていない）。
            TryDeleteTmp(tmp);
            throw;
        }
    }

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";

        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                                           bufferSize: 4096, FileOptions.WriteThrough))
            {
                fs.Write(content);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(tmp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch
        {
            TryDeleteTmp(tmp);
            throw;
        }
    }

    private static void TryDeleteTmp(string tmp)
    {
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 存在しなければ <c>true</c> で <paramref name="content"/> に空文字を返す。
    /// 存在するのに読めない（一時的なロック・権限等）場合は <c>false</c> を返す。
    /// 「読めたが空」と「読めなかった」を区別し、読めなかった本文を空で上書きする事故を防ぐ。
    /// </summary>
    public static bool TryReadAllText(string path, out string content)
    {
        if (!File.Exists(path))
        {
            content = string.Empty;
            return true;
        }

        try
        {
            content = File.ReadAllText(path);
            return true;
        }
        catch (IOException)
        {
            content = string.Empty;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            content = string.Empty;
            return false;
        }
    }

    /// <summary>存在しなければ空文字を返す。改行は AvalonEdit 側で正規化する。</summary>
    public static string ReadAllTextOrEmpty(string path)
        => TryReadAllText(path, out string content) ? content : string.Empty;
}
