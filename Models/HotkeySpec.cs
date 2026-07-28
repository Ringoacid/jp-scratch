using System.Text;
using System.Windows.Input;
using JpScratch.Infrastructure;

namespace JpScratch.Models;

/// <summary>
/// ホットキーの組み合わせ（要件 3.1.4）。設定ファイルには "Alt+Space" のような文字列で持つ。
/// </summary>
public sealed record HotkeySpec(ModifierKeys Modifiers, Key Key)
{
    public static readonly HotkeySpec None = new(ModifierKeys.None, Key.None);

    public bool IsValid => Key != Key.None && Modifiers != ModifierKeys.None;

    public uint Win32Modifiers
    {
        get
        {
            uint m = NativeMethods.MOD_NOREPEAT;
            if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
            if (Modifiers.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= NativeMethods.MOD_WIN;
            return m;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = ModifierKeys.None;
        var key = Key.None;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= ModifierKeys.Control;
                    break;
                case "alt":
                    mods |= ModifierKeys.Alt;
                    break;
                case "shift":
                    mods |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    mods |= ModifierKeys.Windows;
                    break;
                default:
                    if (!Enum.TryParse<Key>(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }

        if (key == Key.None) return false;
        spec = new HotkeySpec(mods, key);
        return true;
    }

    public static HotkeySpec ParseOrDefault(string? text, HotkeySpec fallback)
        => TryParse(text, out var spec) ? spec : fallback;

    public override string ToString()
    {
        if (Key == Key.None) return string.Empty;

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(Key);
        return sb.ToString();
    }

    /// <summary>設定画面やツールチップ用の表示名。</summary>
    public string DisplayName => ToString().Replace("+", " + ");
}
