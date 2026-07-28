using System.Windows.Media;

namespace JpScratch.Editor;

/// <summary>
/// 日本語フォントのフォールバック解決（要件 3.2.2）。
/// 「游ゴシック」のように和名でしか一致しないファミリがあるため、ローカライズ名まで見る。
/// </summary>
internal static class FontResolver
{
    private static HashSet<string>? _installed;

    public static FontFamily Resolve(string? preferred, IReadOnlyList<string> fallbacks)
    {
        if (!string.IsNullOrWhiteSpace(preferred) && IsInstalled(preferred))
            return new FontFamily(preferred);

        foreach (var candidate in fallbacks)
            if (IsInstalled(candidate)) return new FontFamily(candidate);

        // どれも無いことは実質ないが、その場合は WPF の既定に任せる
        return new FontFamily("Segoe UI");
    }

    public static bool IsInstalled(string familyName)
    {
        _installed ??= BuildInstalledIndex();
        return _installed.Contains(familyName);
    }

    /// <summary>設定画面のフォント一覧用。表示名を昇順で返す。</summary>
    public static IEnumerable<string> InstalledFamilies()
    {
        _installed ??= BuildInstalledIndex();
        return _installed.OrderBy(x => x, StringComparer.CurrentCulture);
    }

    private static HashSet<string> BuildInstalledIndex()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in Fonts.SystemFontFamilies)
        {
            names.Add(family.Source);
            foreach (var localized in family.FamilyNames.Values) names.Add(localized);
        }

        return names;
    }
}
