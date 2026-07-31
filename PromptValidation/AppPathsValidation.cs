using JpScratch.Infrastructure;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="AppPaths.ResolveRoot"/>（環境変数によるデータディレクトリ切り替え）の自己テスト。
/// 静的コンストラクタの副作用（ディレクトリ作成）を経由せず、純粋関数だけを検査する。
/// 実ファイルシステムには一切触れない。
/// </summary>
internal static class AppPathsValidation
{
    internal static bool RunSelfTests()
    {
        const string appData = @"C:\Users\test\AppData\Roaming";
        string defaultRoot = Path.Combine(appData, "JpScratch");

        bool unsetFallsBackToDefault =
            AppPaths.ResolveRoot(null, appData) == defaultRoot;
        bool emptyFallsBackToDefault =
            AppPaths.ResolveRoot("", appData) == defaultRoot;
        bool whitespaceFallsBackToDefault =
            AppPaths.ResolveRoot("   ", appData) == defaultRoot;

        string isolatedDir = Path.Combine(Path.GetTempPath(), "JpScratchIsolatedTest");
        string resolvedIsolated = AppPaths.ResolveRoot(isolatedDir, appData);
        bool setUsesGivenDirectory =
            resolvedIsolated == Path.GetFullPath(isolatedDir) &&
            resolvedIsolated != defaultRoot;

        // 前後の空白はトリムしてから解決する。
        bool trimsWhitespace =
            AppPaths.ResolveRoot("  " + isolatedDir + "  ", appData) == Path.GetFullPath(isolatedDir);

        // 相対パスも Path.GetFullPath を通して絶対化される（既定へ落ちない）。
        string relative = "relative-jpscratch-test-dir";
        bool resolvesRelativePath =
            AppPaths.ResolveRoot(relative, appData) == Path.GetFullPath(relative);

        // Windows でパスとして不正な文字列（不正なドライブ指定）は例外を漏らさず既定へ落ちる。
        bool invalidPathFallsBackToDefault =
            AppPaths.ResolveRoot("C:\\invalid\u0000path", appData) == defaultRoot;

        bool passed =
            unsetFallsBackToDefault && emptyFallsBackToDefault && whitespaceFallsBackToDefault &&
            setUsesGivenDirectory && trimsWhitespace && resolvesRelativePath &&
            invalidPathFallsBackToDefault;

        Console.WriteLine(
            "AppPaths（環境変数によるデータディレクトリ解決、未設定/空白/不正値の既定フォールバック）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
