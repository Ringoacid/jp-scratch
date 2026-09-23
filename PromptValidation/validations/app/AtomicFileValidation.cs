using System.Text;
using JpScratch.Infrastructure;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="AtomicFile.TryReadAllText(string, out string, out FileReadFailure)"/> の検証。
///
/// 「読めなかったものを既定値・空文字で上書きしない」規約の土台であり、
/// <see cref="JpScratch.Services.SettingsService"/> の起動時警告はここが返す理由で文面を
/// 切り替える（review-result-2026-08-04 P2-1）。SettingsService 自体は WPF 依存で
/// 自己テストへ取り込めないため、判定の中身はこの層で確かめる。
/// 実ファイルを使うので、一時ディレクトリを作って必ず片付ける。
/// </summary>
internal static class AtomicFileValidation
{
    internal static bool RunSelfTests()
    {
        (string Name, Func<string, bool> Test)[] tests =
        [
            ("往復（UTF-8 で書いて読める・BOM 付きも読める）", TestRoundTrip),
            ("存在しないファイルは「読めた・空」", TestMissingFile),
            ("不正な UTF-8 は InvalidEncoding で失敗", TestInvalidEncoding),
            ("ロック中のファイルは Unreadable で失敗", TestLockedFile),
        ];

        string dir = Path.Combine(Path.GetTempPath(), "JpScratchAtomicFileTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            bool passed = true;
            foreach ((string name, Func<string, bool> test) in tests)
            {
                bool result = test(dir);
                Console.WriteLine($"AtomicFile（{name}）: {(result ? "PASS" : "FAIL")}");
                passed &= result;
            }
            return passed;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static bool TestRoundTrip(string dir)
    {
        string path = Path.Combine(dir, "roundtrip.txt");
        const string content = "日本語の本文\nと改行 🎌";
        AtomicFile.WriteAllText(path, content);

        if (!AtomicFile.TryReadAllText(path, out string read, out FileReadFailure failure))
            return false;
        if (read != content || failure != FileReadFailure.None)
            return false;

        // メモ帳が付ける BOM は剥がして読めること（BOM が本文の先頭に混ざらない）。
        string bomPath = Path.Combine(dir, "bom.txt");
        File.WriteAllText(bomPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return AtomicFile.TryReadAllText(bomPath, out string bomRead, out FileReadFailure bomFailure) &&
               bomRead == content &&
               bomFailure == FileReadFailure.None;
    }

    /// <summary>「存在しない」と「読めない」は別物。前者を失敗にすると初回起動が壊れる。</summary>
    private static bool TestMissingFile(string dir)
        => AtomicFile.TryReadAllText(
               Path.Combine(dir, "missing.txt"), out string content, out FileReadFailure failure) &&
           content.Length == 0 &&
           failure == FileReadFailure.None;

    /// <summary>
    /// CP932 で保存された日本語。既定の UTF-8 デコーダは U+FFFD へ置換して「読めた」ことに
    /// してしまうため、置換文字が返っていないこと（＝失敗していること）を確かめる。
    /// </summary>
    private static bool TestInvalidEncoding(string dir)
    {
        string path = Path.Combine(dir, "cp932.txt");
        // "日本語" の Shift_JIS バイト列。UTF-8 としては不正。
        File.WriteAllBytes(path, [0x93, 0xFA, 0x96, 0x7B, 0x8C, 0xEA]);

        bool ok = AtomicFile.TryReadAllText(path, out string content, out FileReadFailure failure);
        return !ok &&
               failure == FileReadFailure.InvalidEncoding &&
               content.Length == 0 &&
               // 呼び出し元が false を無視しても、置換文字入りの本文が漏れていないこと。
               !content.Contains('�') &&
               // 元のファイルは触らない（退避も切り詰めもしない）。
               File.ReadAllBytes(path).Length == 6;
    }

    /// <summary>
    /// 他プロセスが掴んでいる（共有違反）。文字コード不正と違い、待てば直る種類の失敗なので
    /// <see cref="FileReadFailure.Unreadable"/> でなければならない。
    /// </summary>
    private static bool TestLockedFile(string dir)
    {
        string path = Path.Combine(dir, "locked.txt");
        AtomicFile.WriteAllText(path, "本文");

        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        bool ok = AtomicFile.TryReadAllText(path, out string content, out FileReadFailure failure);
        return !ok && failure == FileReadFailure.Unreadable && content.Length == 0;
    }
}
