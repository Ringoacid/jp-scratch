using System.Windows;
using System.Windows.Media;

namespace JpScratch.Infrastructure;

internal static class VisualTreeHelpers
{
    /// <summary>子孫から名前つきの要素を探す。DataTemplate 内の要素は FindName で取れないため。</summary>
    public static T? FindDescendant<T>(DependencyObject? root, string? name = null) where T : FrameworkElement
    {
        if (root is null) return null;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T typed && (name is null || typed.Name == name)) return typed;

            var found = FindDescendant<T>(child, name);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>祖先方向へ辿る。クリックされた子要素から、対応するタブ見出しを引き当てるのに使う。</summary>
    public static T? FindAncestor<T>(DependencyObject? origin) where T : DependencyObject
    {
        while (origin is not null)
        {
            if (origin is T typed) return typed;
            origin = VisualTreeHelper.GetParent(origin);
        }
        return null;
    }
}
