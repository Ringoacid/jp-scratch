using System.Windows;
using Microsoft.Win32;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// テーマの適用（要件 3.2.2）。OS 追従と手動固定の両対応。
/// OS 側の切替は WM_SETTINGCHANGE で飛んでくるので、ウィンドウのフックから <see cref="ReevaluateSystemTheme"/> を呼ぶ。
/// </summary>
internal sealed class ThemeService
{
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private AppTheme _mode = AppTheme.System;

    /// <summary>今マージされているテーマ辞書。差し替え時にこれだけを狙って外す。</summary>
    private ResourceDictionary? _applied;

    /// <summary>実際に適用されている色。OS 追従のときは OS の設定で決まる。</summary>
    public bool IsDark { get; private set; }

    /// <summary>テーマが切り替わった直後。AvalonEdit のように XAML の動的リソースが効かない部分はここで塗り直す。</summary>
    public event Action? Changed;

    public void Apply(AppTheme mode)
    {
        _mode = mode;
        ApplyResolved(Resolve(mode));
    }

    /// <summary>OS の設定が変わったときに呼ぶ。追従モードでなければ何もしない。</summary>
    public void ReevaluateSystemTheme()
    {
        if (_mode != AppTheme.System) return;

        var dark = IsSystemDark();
        if (dark != IsDark) ApplyResolved(dark);
    }

    private void ApplyResolved(bool dark)
    {
        IsDark = dark;

        var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var dictionary = (ResourceDictionary)Application.LoadComponent(uri);

        var merged = Application.Current.Resources.MergedDictionaries;

        // 先に挿してから外す。逆順にすると一瞬キーが消えて、DynamicResource が
        // 既定色にフォールバックしてチラつく。
        var index = _applied is null ? 0 : Math.Max(0, merged.IndexOf(_applied));
        merged.Insert(index, dictionary);
        if (_applied is not null) merged.Remove(_applied);
        _applied = dictionary;

        Changed?.Invoke();
    }

    private static bool Resolve(AppTheme mode) => mode switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => IsSystemDark(),
    };

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // AppsUseLightTheme: 1 = ライト, 0 = ダーク。キーが無い環境ではライト扱い。
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
