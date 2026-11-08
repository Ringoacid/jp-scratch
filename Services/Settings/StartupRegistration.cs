using System.Diagnostics;
using Microsoft.Win32;

namespace JpScratch.Services;

/// <summary>
/// Windows へのスタートアップ登録（要件 5 / v1）。
/// HKCU の Run キーだけを触るので、管理者権限も MSI の再実行もいらない。
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JpScratch";

    /// <summary>設定値に合わせて登録状態を揃える。起動時と設定変更時に呼ぶ。</summary>
    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var path = GetExecutablePath();
                if (path is null) return;

                // --startup を付けておくと、OS 起動時はウィンドウを出さずトレイだけで待てる。
                var command = $"\"{path}\" --startup";
                if (key.GetValue(ValueName) as string != command)
                    key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // 登録できなくてもアプリの動作自体は続けられる
        }
    }

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        // .NET の単一ファイル/フレームワーク依存の双方で .exe を指すのは MainModule。
        // Assembly.Location は apphost 経由だと .dll を返すことがあり、Run キーには使えない。
        using var process = Process.GetCurrentProcess();
        return process.MainModule?.FileName;
    }
}
