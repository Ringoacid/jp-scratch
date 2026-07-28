using System.IO;

namespace JpScratch.Infrastructure;

/// <summary>
/// %APPDATA%\JpScratch 配下のパスを解決する（要件 4）。
/// アプリが壊れてもメモ帳で本文をサルベージできること自体が要件なので、
/// 本文は必ずこの下のプレーンテキストとして置く。
/// </summary>
internal static class AppPaths
{
    public static string Root { get; }

    static AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JpScratch");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(TabsDir);
        Directory.CreateDirectory(TrashDir);
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
