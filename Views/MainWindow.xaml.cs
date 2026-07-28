using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JpScratch.Editor;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>
/// メインウィンドウ。常駐アプリなので閉じても破棄せず、Show/Hide で出し入れする（要件 2.1）。
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly TabManager _tabs;
    private readonly TabRepository _repository;
    private readonly HotkeyService _hotkeys;

    private readonly IdeographicSpaceColorizer _ideographicSpace = new();
    private readonly DispatcherTimer _statusTimer;

    private IntPtr _handle;

    /// <summary>表示する直前に前面だったウィンドウ。「コピーして隠す」で戻す先（要件 3.1.4）。</summary>
    private IntPtr _previousForeground;

    /// <summary>設定ダイアログなど、自分の子ウィンドウを開いている間は自動非表示を止める。</summary>
    private bool _suppressHideOnDeactivate;

    private DateTime _lastAutoHide = DateTime.MinValue;
    private DateTime _statusMessageUntil = DateTime.MinValue;

    private ScratchTab? _dragTab;
    private Point _dragOrigin;

    private CrossTabSearchWindow? _crossTabSearch;

    internal MainWindow(SettingsService settings, ThemeService theme, TabManager tabs,
                        TabRepository repository, HotkeyService hotkeys)
    {
        _settings = settings;
        _theme = theme;
        _tabs = tabs;
        _repository = repository;
        _hotkeys = hotkeys;

        InitializeComponent();

        Width = settings.Current.WindowWidth;
        Height = settings.Current.WindowHeight;
        Topmost = settings.Current.Topmost;

        TabStrip.ItemsSource = _tabs.Tabs;
        _tabs.ActiveChanged += OnActiveTabChanged;

        Editor.TextArea.TextView.LineTransformers.Add(_ideographicSpace);
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            // どの保存経路を通っても復元できるよう、キャレット位置は常にタブへ写しておく
            if (_tabs.Active is { } active) active.CaretOffset = Editor.CaretOffset;
            ScheduleStatusUpdate();
        };
        Editor.TextArea.SelectionChanged += (_, _) => ScheduleStatusUpdate();
        Editor.TextChanged += (_, _) => ScheduleStatusUpdate();

        FindPanel.Attach(Editor);
        FindPanel.CrossTabSearchRequested += OpenCrossTabSearch;
        FindPanel.Closed += () => Editor.TextArea.Focus();

        _theme.Changed += ApplyThemeToEditor;
        _settings.Changed += OnSettingsChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            UpdateStatus();
        };

        SetupInputBindings();
        ApplyEditorSettings();
        ApplyThemeToEditor();
        OnActiveTabChanged(_tabs.Active);
    }

    // ================= ライフサイクル =================

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_handle)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_SETTINGCHANGE)
        {
            // OS のライト/ダーク切替はここで飛んでくる（要件 3.2.2）
            _theme.ReevaluateSystemTheme();
        }

        return IntPtr.Zero;
    }

    /// <summary>App から呼ぶ。HWND ができたあとでないとホットキーを配線できない。</summary>
    internal void AttachHotkeyHandlers()
    {
        _hotkeys.Pressed += id =>
        {
            switch (id)
            {
                case HotkeyService.IdToggle:
                    ToggleVisibility();
                    break;
                case HotkeyService.IdCopyAndHide:
                    CopyAllAndHide();
                    break;
            }
        };
    }

    /// <summary>
    /// OS 起動時など、ウィンドウを見せずに常駐だけ始めるときの下準備。
    /// 画面外で一度描画しておくと、最初のホットキー表示が目に見えて速くなる（要件 2.1）。
    /// </summary>
    internal void WarmUp()
    {
        ShowActivated = false;
        _suppressHideOnDeactivate = true;

        try
        {
            NativeMethods.SetWindowPos(_handle, IntPtr.Zero, -30000, -30000, 480, 600,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Show();

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                Hide();
                ShowActivated = true;
                _suppressHideOnDeactivate = false;
            });
        }
        catch (InvalidOperationException)
        {
            ShowActivated = true;
            _suppressHideOnDeactivate = false;
        }
    }

    /// <summary>閉じるボタンでは終了しない。終了はトレイの「終了」だけ（要件 3.1.1）。</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        HideWindow(auto: false);
    }

    // ================= 表示・非表示 =================

    public void ToggleVisibility()
    {
        // トレイアイコンのクリックはまずウィンドウを非アクティブにするため、
        // 自動非表示 → トグルで再表示、という往復が起きる。直後のトグルは無視する。
        var justAutoHid = (DateTime.UtcNow - _lastAutoHide).TotalMilliseconds < 400;

        if (IsVisible) HideWindow(auto: false);
        else if (!justAutoHid) ShowAndFocus();
    }

    public void ShowAndFocus()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != _handle && foreground != IntPtr.Zero) _previousForeground = foreground;

        Topmost = _settings.Current.Topmost;

        // 見せる前に置く。順番を逆にすると、前回位置で一瞬描かれてから飛ぶ。
        WindowPlacer.Place(this, _settings.Current);
        Show();
        WindowPlacer.Place(this, _settings.Current);

        Activate();
        NativeMethods.ForceForeground(_handle);
        Editor.TextArea.Focus();
        Keyboard.Focus(Editor.TextArea);
    }

    /// <param name="auto">フォーカス喪失による非表示か。トレイクリックとの往復を抑えるのに使う。</param>
    /// <param name="alreadyCopied">呼び出し側が本文をコピー済みか。二重にコピーしないための印。</param>
    private void HideWindow(bool auto, bool alreadyCopied = false)
    {
        if (!IsVisible) return;

        if (_tabs.Active is { } active)
        {
            _tabs.SaveCaret(active, Editor.CaretOffset);

            // 隠すときに本文をクリップボードへ（要件 3.1.3）。
            // ホットキー・Esc・閉じるボタンでも同じように効かせる。隠れたあとでは
            // 「コピーし忘れた」と気づいても取り返せないので、経路で差をつけない。
            if (!alreadyCopied && _settings.Current.CopyToClipboardOnHide) CopyCurrentText();
        }

        // ウィンドウ非表示は保存タイミングのひとつ（要件 3.2.4）
        _tabs.SaveDirty();

        WindowPlacer.Capture(this, _settings.Current);
        _settings.SaveDebounced();

        if (auto) _lastAutoHide = DateTime.UtcNow;
        Hide();

        // ここから先はトレイで待つだけ。抱えているメモリを返してから寝る（要件 2.1）。
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (IsVisible) return;   // 返し終える前に呼び戻されたら何もしない

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            NativeMethods.TrimWorkingSet();
        });
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (!_settings.Current.HideOnFocusLost) return;
        if (PinButton.IsChecked == true) return;          // ピン留め中は隠さない
        if (_suppressHideOnDeactivate) return;            // 設定ダイアログなどを開いている

        // 変換中に消えると入力そのものが失われる（要件 3.1.3 / R-5）
        if (NativeMethods.HasImeComposition(_handle)) return;

        HideWindow(auto: true);
    }

    /// <summary>全文（選択があればその範囲）をコピーして隠し、元のウィンドウへ戻る（要件 3.1.4）。</summary>
    private void CopyAllAndHide()
    {
        if (!IsVisible)
        {
            // 隠れている状態でも、直近のタブの内容を渡せたほうが役に立つ
            CopyCurrentText();
            return;
        }

        CopyCurrentText();
        HideWindow(auto: false, alreadyCopied: true);
        NativeMethods.ForceForeground(_previousForeground);
    }

    private void CopyCurrentText()
    {
        var text = Editor.SelectionLength > 0 ? Editor.SelectedText : Editor.Document.Text;
        if (!ClipboardHelper.TrySetText(text))
            SetTransientStatus("クリップボードにコピーできませんでした");
    }

    // ================= 設定の反映 =================

    private void OnSettingsChanged(AppSettings settings)
    {
        Topmost = settings.Topmost;
        _theme.Apply(settings.Theme);
        ApplyEditorSettings();
        _tabs.ReloadAutoSaveInterval();
        StartupRegistration.Sync(settings.StartWithWindows);

        var failures = _hotkeys.Reregister(settings);
        if (failures.Count > 0)
            SetTransientStatus("ホットキーを登録できませんでした（設定を確認してください）");
        else
            UpdateStatus();
    }

    private void ApplyEditorSettings()
    {
        var s = _settings.Current;

        Editor.FontFamily = FontResolver.Resolve(s.FontFamily, AppSettings.FontFallback);
        Editor.FontSize = s.FontSize;
        Editor.WordWrap = s.WordWrap;
        Editor.ShowLineNumbers = s.ShowLineNumbers;

        Editor.Options.ShowSpaces = s.ShowWhitespace;
        Editor.Options.ShowTabs = s.ShowWhitespace;
        Editor.Options.ShowEndOfLine = s.ShowEndOfLine;
        Editor.Options.HighlightCurrentLine = s.HighlightCurrentLine;
        Editor.Options.AllowScrollBelowDocument = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;

        // 全角スペースは AvalonEdit の ShowSpaces では出ない（要件 3.2.2）
        _ideographicSpace.Enabled = s.ShowWhitespace;
        Editor.TextArea.TextView.Redraw();
    }

    private void ApplyThemeToEditor()
    {
        // AvalonEdit の描画色は DependencyProperty ではない部分が多く、XAML から追随できない。
        Editor.Background = Brush("EditorBackgroundBrush");
        Editor.Foreground = Brush("EditorForegroundBrush");
        Editor.LineNumbersForeground = Brush("LineNumberForegroundBrush");

        Editor.TextArea.SelectionBrush = Brush("SelectionBrush");
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;

        Editor.TextArea.TextView.CurrentLineBackground = Brush("CurrentLineBackgroundBrush");
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(Brush("CurrentLineBorderBrush"), 1);
        Editor.TextArea.TextView.NonPrintableCharacterBrush = Brush("WhitespaceBrush");

        _ideographicSpace.Background = Brush("WhitespaceBrush");

        FindPanel.ApplyTheme(Brush("SearchMatchBrush"), Brush("SearchCurrentMatchBrush"));

        Editor.TextArea.TextView.Redraw();
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    // ================= タブ =================

    private void OnActiveTabChanged(ScratchTab? tab)
    {
        if (tab is null) return;

        // Document を差し替えるとキャレットが 0 に戻り、その通知で tab.CaretOffset が
        // 上書きされてしまう。復元したい位置は先に控えておく。
        var restoreOffset = Math.Clamp(tab.CaretOffset, 0, tab.Document.TextLength);

        Editor.Document = tab.Document;
        Editor.CaretOffset = restoreOffset;
        Editor.ScrollToLine(Editor.TextArea.Caret.Line);

        FindPanel.OnDocumentSwapped();
        UpdateStatus();
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        _tabs.AddNew();
        Editor.TextArea.Focus();
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ScratchTab tab) _tabs.Close(tab);
        e.Handled = true;
    }

    private void TabItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ScratchTab tab) return;

        if (e.ClickCount == 2)
        {
            BeginRename(sender as FrameworkElement, tab);
            e.Handled = true;
            return;
        }

        _tabs.Activate(tab);
        _dragTab = tab;
        _dragOrigin = e.GetPosition(this);
    }

    private void TabItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed) return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance) return;

        // ドラッグ中のタブを、今カーソルが乗っているタブの位置へ差し込む（要件 3.2.1）
        if ((sender as FrameworkElement)?.DataContext is not ScratchTab hovered) return;
        if (ReferenceEquals(hovered, _dragTab)) return;

        var from = _tabs.Tabs.IndexOf(_dragTab);
        var to = _tabs.Tabs.IndexOf(hovered);
        if (from >= 0 && to >= 0) _tabs.Move(from, to);
    }

    private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _dragTab = null;

    private void TabItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 中クリックで閉じる（要件 3.2.1）
        if (e.ChangedButton != MouseButton.Middle) return;
        if ((sender as FrameworkElement)?.DataContext is ScratchTab tab) _tabs.Close(tab);
        e.Handled = true;
    }

    private void BeginRename(FrameworkElement? container, ScratchTab tab)
    {
        tab.IsEditing = true;

        // Visibility が切り替わって実体ができるのを待ってからフォーカスする
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var box = VisualTreeHelpers.FindDescendant<TextBox>(container, "RenameBox");
            if (box is null) return;

            box.Text = tab.Title;
            box.Focus();
            box.SelectAll();
        });
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ScratchTab tab) return;

        switch (e.Key)
        {
            case Key.Enter:
                _tabs.Rename(tab, box.Text);
                tab.IsEditing = false;
                Editor.TextArea.Focus();
                e.Handled = true;
                break;

            case Key.Escape:
                tab.IsEditing = false;
                Editor.TextArea.Focus();
                e.Handled = true;
                break;
        }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ScratchTab tab) return;
        if (!tab.IsEditing) return;

        _tabs.Rename(tab, box.Text);
        tab.IsEditing = false;
    }

    private void TabScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // タブは横並びなので、縦ホイールを横スクロールに読み替える
        TabScroller.ScrollToHorizontalOffset(TabScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    // ================= 入力 =================

    private void SetupInputBindings()
    {
        void Bind(Key key, ModifierKeys modifiers, Action action)
            => InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));

        Bind(Key.T, ModifierKeys.Control, () => _tabs.AddNew());
        Bind(Key.W, ModifierKeys.Control, () => { if (_tabs.Active is { } t) _tabs.Close(t); });
        Bind(Key.T, ModifierKeys.Control | ModifierKeys.Shift, RestoreClosedTab);
        Bind(Key.Tab, ModifierKeys.Control, () => _tabs.ActivateByOffset(1));
        Bind(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, () => _tabs.ActivateByOffset(-1));

        Bind(Key.F, ModifierKeys.Control, () => FindPanel.Open(focusReplace: false));
        Bind(Key.H, ModifierKeys.Control, () => FindPanel.Open(focusReplace: true));
        Bind(Key.F3, ModifierKeys.None, () => FindPanel.FindNext(forward: true));
        Bind(Key.F3, ModifierKeys.Shift, () => FindPanel.FindNext(forward: false));
        Bind(Key.F, ModifierKeys.Control | ModifierKeys.Shift, () => OpenCrossTabSearch(""));

        Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, ExportActiveTab);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // IME が処理したキーには触らない
        if (e.Key == Key.ImeProcessed) return;

        if (e.Key != Key.Escape) return;

        // タブ名のインライン編集中の Esc は「編集の取り消し」。ウィンドウを隠してはいけない。
        if (Keyboard.FocusedElement is TextBox { Name: "RenameBox" }) return;

        if (FindPanel.Visibility == Visibility.Visible)
        {
            FindPanel.Hide();
        }
        else if (!NativeMethods.HasImeComposition(_handle))
        {
            HideWindow(auto: false);
        }

        e.Handled = true;
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        // Ctrl + ホイールでフォントサイズ（要件 3.2.2）
        var size = _settings.Current.FontSize + (e.Delta > 0 ? 1 : -1);
        _settings.Current.FontSize = Math.Clamp(size, 8, 72);
        Editor.FontSize = _settings.Current.FontSize;
        _settings.SaveDebounced();

        SetTransientStatus($"{_settings.Current.FontSize:0} pt");
        e.Handled = true;
    }

    private void RestoreClosedTab()
    {
        if (_tabs.RestoreLastClosed() is null) SetTransientStatus("復元できるタブがありません");
    }

    // ================= 各種ウィンドウ =================

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void HideButton_Click(object sender, RoutedEventArgs e) => HideWindow(auto: false);

    private void PinButton_Changed(object sender, RoutedEventArgs e)
        => PinButton.ToolTip = PinButton.IsChecked == true
            ? "ピン留め中（フォーカスが外れても隠れません）"
            : "ピン留め（フォーカスが外れても隠さない）";

    public void OpenSettings()
    {
        if (!IsVisible) ShowAndFocus();

        _suppressHideOnDeactivate = true;
        try
        {
            var dialog = new SettingsWindow(_settings) { Owner = this };
            dialog.ShowDialog();
        }
        finally
        {
            _suppressHideOnDeactivate = false;
        }

        Activate();
        Editor.TextArea.Focus();
    }

    private void OpenCrossTabSearch(string initialTerm)
    {
        _suppressHideOnDeactivate = true;

        if (_crossTabSearch is null)
        {
            _crossTabSearch = new CrossTabSearchWindow(_tabs, _repository) { Owner = this };
            _crossTabSearch.Closed += (_, _) =>
            {
                _crossTabSearch = null;
                _suppressHideOnDeactivate = false;
            };
            _crossTabSearch.HitSelected += JumpToHit;
            _crossTabSearch.Show();
        }
        else
        {
            _crossTabSearch.Activate();
        }

        _crossTabSearch.SetTerm(initialTerm);
    }

    private void JumpToHit(CrossTabHit hit)
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Id == hit.TabId);
        if (tab is null)
        {
            // ゴミ箱の中のタブは、まず開いているタブとして戻す必要がある
            SetTransientStatus("ゴミ箱のタブです。Ctrl+Shift+T で復元してください");
            return;
        }

        _tabs.Activate(tab);
        Activate();

        var offset = Math.Clamp(hit.Offset, 0, Editor.Document.TextLength);
        Editor.Select(offset, Math.Min(hit.Length, Editor.Document.TextLength - offset));
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(offset).LineNumber);
        Editor.TextArea.Focus();
    }

    private void ExportActiveTab()
    {
        if (_tabs.Active is not { } tab) return;

        _suppressHideOnDeactivate = true;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "テキスト ファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = SanitizeFileName(tab.Title) + ".txt",
            };

            if (dialog.ShowDialog(this) != true) return;

            File.WriteAllText(dialog.FileName, tab.Document.Text, new System.Text.UTF8Encoding(false));
            SetTransientStatus("エクスポートしました");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetTransientStatus("エクスポートに失敗しました");
        }
        finally
        {
            _suppressHideOnDeactivate = false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? "scratch" : cleaned;
    }

    // ================= ステータスバー =================

    /// <summary>
    /// ステータスバー右側に一時的なメッセージを出す。
    /// 定期更新に即座に上書きされて読めない、ということがないよう数秒間は保持する。
    /// </summary>
    private void SetTransientStatus(string message)
    {
        StatusRight.Text = message;
        _statusMessageUntil = DateTime.UtcNow.AddSeconds(4);
    }

    private void ScheduleStatusUpdate()
    {
        // 1 打鍵ごとに全文を数え直すと重いので、少しまとめる
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void UpdateStatus()
    {
        var document = Editor.Document;
        var caret = Editor.TextArea.Caret;

        var characters = CountCharacters(document);
        var status = $"{caret.Line} 行 {caret.Column} 列    {document.LineCount} 行  {characters} 文字";

        if (Editor.SelectionLength > 0)
            status += $"    選択 {Editor.SelectedText.Replace("\r", "").Replace("\n", "").Length} 文字";

        StatusLeft.Text = status;

        if (DateTime.UtcNow > _statusMessageUntil)
            StatusRight.Text = $"{_settings.Current.CopyAndHideHotkey.DisplayName} でコピーして隠す";
    }

    /// <summary>改行を除いた文字数。日本語の文書では実質こちらが「文字数」（要件 3.2.2）。</summary>
    private static int CountCharacters(ICSharpCode.AvalonEdit.Document.TextDocument document)
    {
        var total = document.TextLength;

        for (var i = 1; i <= document.LineCount; i++)
            total -= document.GetLineByNumber(i).DelimiterLength;

        return total;
    }
}
