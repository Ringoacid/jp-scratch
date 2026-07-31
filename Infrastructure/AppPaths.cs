using System.IO;

namespace JpScratch.Infrastructure;

/// <summary>
/// %APPDATA%\JpScratch 配下のパスを解決する（要件 4）。
/// アプリが壊れてもメモ帳で本文をサルベージできること自体が要件なので、
/// 本文は必ずこの下のプレーンテキストとして置く。
/// </summary>
internal static class AppPaths
{
    /// <summary>
    /// 設定すると、データディレクトリを %APPDATA%\JpScratch の代わりにこのパスへ切り替える。
    /// 課金履歴画面などを実データに触れずに検証するための隔離用途で、通常運用では未設定のまま。
    /// </summary>
    internal const string DataDirEnvironmentVariable = "JPSCRATCH_DATA_DIR";

    public static string Root { get; }

    static AppPaths()
    {
        Root = ResolveRoot(
            Environment.GetEnvironmentVariable(DataDirEnvironmentVariable),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(TabsDir);
        Directory.CreateDirectory(TrashDir);
    }

    /// <summary>
    /// データディレクトリを決定する純粋関数。<paramref name="environmentValue"/> が
    /// 空・空白のみ・パスとして不正な場合は既定（<paramref name="appDataFolder"/>\JpScratch）へ
    /// 安全に落ちる。静的コンストラクタでの例外は型初期化例外になり起動全体を道連れにするため、
    /// 副作用（ディレクトリ作成）から切り離してここでテスト可能にしてある。
    /// </summary>
    internal static string ResolveRoot(string? environmentValue, string appDataFolder)
    {
        string defaultRoot = Path.Combine(appDataFolder, "JpScratch");

        if (string.IsNullOrWhiteSpace(environmentValue))
            return defaultRoot;

        try
        {
            string fullPath = Path.GetFullPath(environmentValue.Trim());
            return string.IsNullOrWhiteSpace(fullPath) ? defaultRoot : fullPath;
        }
        catch (Exception)
        {
            // ArgumentException（不正文字等）/ PathTooLongException / NotSupportedException（"C:\\a:b" 等）を
            // まとめて捕捉する。ここで何を投げても型初期化例外になり起動全体が死ぬため、必ず既定へ落とす。
            return defaultRoot;
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string PricingFile => Path.Combine(Root, "pricing.json");
    public static string CredentialsFile => Path.Combine(Root, "credentials.dat");
    public static string DatabaseFile => Path.Combine(Root, "app.db");
    public static string TabsDir => Path.Combine(Root, "tabs");
    public static string TrashDir => Path.Combine(TabsDir, "trash");
    public static string CrashLogFile => Path.Combine(Root, "crash.log");

    public static string TabFile(string tabId) => Path.Combine(TabsDir, tabId + ".txt");
    public static string TrashFile(string tabId) => Path.Combine(TrashDir, tabId + ".txt");
}
