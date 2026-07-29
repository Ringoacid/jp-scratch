using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JpScratch.Editor;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>設定画面（要件 5 / v1）。OK を押すまで実際の設定には触らない。</summary>
public partial class SettingsWindow : Window
{
    private const string AutoFontLabel = "（自動: メイリオ → 游ゴシック）";

    private readonly SettingsService _service;

    internal SettingsWindow(SettingsService service)
    {
        _service = service;
        InitializeComponent();
        LoadFrom(service.Current);
    }

    private void LoadFrom(AppSettings s)
    {
        PositionCombo.ItemsSource = new[]
        {
            "タスクバーのあるモニタの右下",
            "マウスカーソルのあるモニタの右下",
            "前回の位置を復元",
        };
        PositionCombo.SelectedIndex = (int)s.PositionMode;

        ThemeCombo.ItemsSource = new[] { "OS の設定に従う", "ライト", "ダーク" };
        ThemeCombo.SelectedIndex = (int)s.Theme;

        TopmostCheck.IsChecked = s.Topmost;
        HideOnFocusLostCheck.IsChecked = s.HideOnFocusLost;
        CopyOnHideCheck.IsChecked = s.CopyToClipboardOnHide;

        ToggleHotkeyBox.Text = s.ToggleHotkey.DisplayName;
        CopyHideHotkeyBox.Text = s.CopyAndHideHotkey.DisplayName;

        var families = new List<string> { AutoFontLabel };
        families.AddRange(FontResolver.InstalledFamilies());
        FontCombo.ItemsSource = families;
        FontCombo.SelectedItem = string.IsNullOrWhiteSpace(s.FontFamily) || !FontResolver.IsInstalled(s.FontFamily)
            ? AutoFontLabel
            : s.FontFamily;

        FontSizeBox.Text = s.FontSize.ToString("0", CultureInfo.InvariantCulture);
        WordWrapCheck.IsChecked = s.WordWrap;
        LineNumbersCheck.IsChecked = s.ShowLineNumbers;
        CurrentLineCheck.IsChecked = s.HighlightCurrentLine;
        WhitespaceCheck.IsChecked = s.ShowWhitespace;
        EndOfLineCheck.IsChecked = s.ShowEndOfLine;

        AutoSaveBox.Text = s.AutoSaveDebounceMs.ToString(CultureInfo.InvariantCulture);
        TrashDaysBox.Text = s.TrashRetentionDays.ToString(CultureInfo.InvariantCulture);

        StartupCheck.IsChecked = s.StartWithWindows;

        DataFolderText.Text =
            $"本文と設定の保存先: {AppPaths.Root}\n" +
            "本文はプレーンテキスト（UTF-8）で保存されるため、このアプリが動かなくなってもメモ帳で開けます。";
    }

    private void ApplyTo(AppSettings s)
    {
        s.PositionMode = (WindowPositionMode)Math.Max(0, PositionCombo.SelectedIndex);
        s.Theme = (AppTheme)Math.Max(0, ThemeCombo.SelectedIndex);

        s.Topmost = TopmostCheck.IsChecked == true;
        s.HideOnFocusLost = HideOnFocusLostCheck.IsChecked == true;
        s.CopyToClipboardOnHide = CopyOnHideCheck.IsChecked == true;

        s.HotkeyToggle = ParseHotkeyOrKeep(ToggleHotkeyBox.Text, s.HotkeyToggle);
        s.HotkeyCopyAndHide = ParseHotkeyOrKeep(CopyHideHotkeyBox.Text, s.HotkeyCopyAndHide);

        s.FontFamily = FontCombo.SelectedItem as string == AutoFontLabel
            ? ""
            : FontCombo.SelectedItem as string ?? "";

        s.FontSize = ParseNumber(FontSizeBox.Text, s.FontSize);
        s.WordWrap = WordWrapCheck.IsChecked == true;
        s.ShowLineNumbers = LineNumbersCheck.IsChecked == true;
        s.HighlightCurrentLine = CurrentLineCheck.IsChecked == true;
        s.ShowWhitespace = WhitespaceCheck.IsChecked == true;
        s.ShowEndOfLine = EndOfLineCheck.IsChecked == true;

        s.AutoSaveDebounceMs = (int)ParseNumber(AutoSaveBox.Text, s.AutoSaveDebounceMs);
        s.TrashRetentionDays = (int)ParseNumber(TrashDaysBox.Text, s.TrashRetentionDays);

        s.StartWithWindows = StartupCheck.IsChecked == true;
    }

    private static string ParseHotkeyOrKeep(string display, string fallback)
        => HotkeySpec.TryParse(display.Replace(" ", ""), out var spec) ? spec.ToString() : fallback;

    private static double ParseNumber(string text, double fallback)
        => double.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    /// <summary>
    /// ホットキーの入力欄。押されたキーをそのまま割り当てる。
    /// 修飾キー単独では確定させない（Alt だけを登録しても意味がないため）。
    /// </summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Back)
        {
            box.Text = "";
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
                or Key.System or Key.None or Key.ImeProcessed)
        {
            return;
        }

        var spec = new HotkeySpec(Keyboard.Modifiers, key);
        if (!spec.IsValid)
        {
            box.Text = "修飾キーと組み合わせてください";
            return;
        }

        box.Text = spec.DisplayName;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // 現在値のコピーに書き込んでから差し替える。途中で例外が出ても設定が半端に壊れない。
        var updated = _service.Current.Clone();
        ApplyTo(updated);
        _service.Replace(updated);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "フォルダを開けませんでした。", "JP Scratch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
