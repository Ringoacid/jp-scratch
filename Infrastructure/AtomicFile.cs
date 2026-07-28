using System.IO;
using System.Text;

namespace JpScratch.Infrastructure;

/// <summary>
/// 原子的なテキスト書き込み（要件 3.2.4 / R-8）。
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

    /// <summary>存在しなければ空文字を返す。改行は AvalonEdit 側で正規化する。</summary>
    public static string ReadAllTextOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
