using System.IO;
using System.Text;

namespace JpScratch.Infrastructure;

/// <summary>
/// <see cref="AtomicFile.TryReadAllText(string, out string, out FileReadFailure)"/> が
/// 「読めなかった」理由。呼び出し元の案内文がまるで変わるため区別する。
/// </summary>
internal enum FileReadFailure
{
    /// <summary>読めた（または存在しない）。</summary>
    None,

    /// <summary>
    /// 一時的に読めない（共有違反・権限エラー）。ファイルは無傷で、再起動や時間経過で直る。
    /// </summary>
    Unreadable,

    /// <summary>
    /// UTF-8 として解釈できないバイト列（外部エディタで CP932 保存された等）。
    /// ファイルは無傷だが、直すのはユーザーの操作であって再起動では直らない。
    /// </summary>
    InvalidEncoding,
}

/// <summary>
/// 一時ファイル経由で、既存データを壊さずに行うファイル書き込み（要件 3.2.4 / R-8）。
/// 一時ファイルへ書き切ってから <see cref="File.Replace(string,string,string?)"/> で差し替えるので、
/// 書き込み中にプロセスが落ちても既存の本文は無傷で残る。
/// </summary>
internal static class AtomicFile
{
    /// <summary>BOM なし UTF-8。メモ帳・WSL の双方から素直に読める形にしておく。</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 読み取り専用の UTF-8。不正バイト列で例外を投げる（<c>throwOnInvalidBytes: true</c>）のが要点。
    /// 既定の UTF8Encoding は不正バイトを U+FFFD へ黙って置換するため、外部エディタで CP932 として
    /// 保存された本文が「読めた」ことになり、次の自動保存で元のバイト列を破壊する。ここで失敗させ、
    /// <see cref="TryReadAllText"/> が false を返す＝「読めなかった」経路（タブを開かず LoadFailures へ）
    /// に合流させる。
    /// </summary>
    private static readonly UTF8Encoding Utf8Strict =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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
    /// 存在するのに読めない（一時的なロック・権限等、または UTF-8 として不正）場合は <c>false</c> を返す。
    /// 「読めたが空」と「読めなかった」を区別し、読めなかった本文を空で上書きする事故を防ぐ。
    /// </summary>
    public static bool TryReadAllText(string path, out string content)
        => TryReadAllText(path, out content, out _);

    /// <summary>
    /// 失敗の理由も返す版。「時間が解決する失敗」と「ユーザーが直すまで直らない失敗」では
    /// 案内すべき内容が正反対になるため、区別できるようにしてある
    /// （<see cref="Services.SettingsService"/> の起動時警告が使う）。
    /// </summary>
    public static bool TryReadAllText(string path, out string content, out FileReadFailure failure)
    {
        if (!File.Exists(path))
        {
            content = string.Empty;
            failure = FileReadFailure.None;
            return true;
        }

        try
        {
            // BOM 付き UTF-8 は StreamReader が BOM を検出して剥がす（従来どおり）。
            // UTF-8 として解釈できないバイト列だけが DecoderFallbackException になる。
            content = File.ReadAllText(path, Utf8Strict);
            failure = FileReadFailure.None;
            return true;
        }
        catch (IOException)
        {
            content = string.Empty;
            failure = FileReadFailure.Unreadable;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            content = string.Empty;
            failure = FileReadFailure.Unreadable;
            return false;
        }
        catch (DecoderFallbackException)
        {
            // 不正な UTF-8（外部エディタで CP932 保存された等）。U+FFFD へ置換して「読めた」ことに
            // すると、次の自動保存で元のバイト列を復元不能に壊す。読めなかった扱いにして残す。
            content = string.Empty;
            failure = FileReadFailure.InvalidEncoding;
            return false;
        }
    }

    /// <summary>存在しなければ空文字を返す。改行は AvalonEdit 側で正規化する。</summary>
    public static string ReadAllTextOrEmpty(string path)
        => TryReadAllText(path, out string content) ? content : string.Empty;
}
