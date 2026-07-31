using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using JpScratch.Editor;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Proofreading;
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
    private readonly CredentialService _credentials;
    private readonly PricingService _pricing;
    private readonly ApiCallRepository _apiCalls;
    private readonly FxRateService _fxRates;
    private readonly ReactionRepository _reactions;
    private readonly StyleGuideRepository _styleGuides;
    private readonly IProofreadingClient _proofreadingClient;
    private readonly TrayIconService _tray;
    private readonly DateTimeOffset _sessionStartedAt;

    /// <summary>1回のGemini応答で固定するUSD額と、その時点の為替スナップショット。</summary>
    private sealed record ApiUsageCost(
        int PromptTokens,
        int OutputTokens,
        decimal UsdCost,
        FxRate? FxRate,
        bool IsUsageKnown)
    {
        internal decimal? JpyCost => FxRate is null ? null : UsdCost * FxRate.UsdJpy;
    }

    private sealed record RecordedApiCall(long? Id, ApiUsageCost Cost);

    private readonly IdeographicSpaceColorizer _ideographicSpace = new();
    private readonly ProofreadingUnderlineRenderer _proofreadingRenderer = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _usageRolloverTimer;
    private readonly DispatcherTimer _proofreadingTimer;
    private readonly ProofreadingSchedule _proofreadingSchedule = new();

    /// <summary>
    /// 最後に <see cref="RefreshUsageDisplay"/> が読み取った当月累計USD。発火条件5（月間上限）の
    /// 判定・進捗バー・確認ダイアログの表示が、それぞれ別にDBを読みに行かずこれを共有する。
    /// </summary>
    private decimal _monthUsageUsd;

    /// <summary>月間上限到達のトレイ通知を「年月＋上限額」単位で一度だけに抑える状態。</summary>
    private readonly UsageLimitNotificationTracker _usageLimitNotifications = new();

    private IntPtr _handle;

    /// <summary>表示する直前に前面だったウィンドウ。「コピーして隠す」で戻す先（要件 3.1.4）。</summary>
    private IntPtr _previousForeground;

    /// <summary>
    /// 自動非表示の抑止（要件 3.1.3）。設定ダイアログ・全タブ検索・課金履歴など、複数の子ウィンドウを
    /// 同時に開いている状態で片方だけ閉じても、残りが開いている間は抑止され続ける必要がある。
    /// bool 共有では、後から閉じた側が無条件に解除してしまい、まだ開いている側があっても
    /// 自動非表示が働いてしまう不具合があった。ロジックは <see cref="HideSuppressionCounter"/> に
    /// 切り出してあり、PromptValidation から参照カウントの挙動だけを単体検証できる。
    /// </summary>
    private readonly HideSuppressionCounter _hideSuppression = new();

    private void SuppressAutoHide() => _hideSuppression.Suppress();

    private void ReleaseAutoHide() => _hideSuppression.Release();

    private DateTime _lastAutoHide = DateTime.MinValue;
    private DateTime _statusMessageUntil = DateTime.MinValue;
    private DateOnly _usageDisplayDate = DateOnly.MinValue;

    private ScratchTab? _dragTab;
    private Point _dragOrigin;

    private CrossTabSearchWindow? _crossTabSearch;
    private BillingHistoryWindow? _billingHistory;
    private ProofreadingSession? _activeProofreading;
    private ProofreadingProposal? _selectedProposal;
    private bool _alternativeInProgress;
    private bool _proofreadingRunInProgress;

    /// <summary>
    /// スタイルガイド自動生成（要件3.4.2）が課金APIの応答待ちか。校正・別案生成と同じく
    /// トレイの「校正中」表示・_apiErrorStickyを共有するので、互いに排他させる。
    /// </summary>
    private bool _styleGuideGenerationInProgress;

    /// <summary>
    /// 直近の Gemini 呼び出しが失敗したまま復帰していないか（トレイアイコン用、要件 3.1.1）。
    /// 次に 1 回でも成功したら解除する。キー未設定・確認ダイアログのキャンセル・本文変更による
    /// 破棄はここに含めない。APIの異常ではなく、こちらの都合で呼ばなかった・捨てただけなので、
    /// 「APIエラー」を出すと直しようのない警告を出し続けることになる。
    /// </summary>
    private bool _apiErrorSticky;

    internal MainWindow(SettingsService settings, ThemeService theme, TabManager tabs,
                         TabRepository repository, HotkeyService hotkeys,
                         CredentialService credentials,
                         PricingService pricing,
                         ApiCallRepository apiCalls,
                         FxRateService fxRates,
                         ReactionRepository reactions,
                         StyleGuideRepository styleGuides,
                        IProofreadingClient proofreadingClient,
                        TrayIconService tray)
    {
        _settings = settings;
        _theme = theme;
        _tabs = tabs;
        _repository = repository;
        _hotkeys = hotkeys;
        _credentials = credentials;
        _pricing = pricing;
        _apiCalls = apiCalls;
        _fxRates = fxRates;
        _reactions = reactions;
        _styleGuides = styleGuides;
        _proofreadingClient = proofreadingClient;
        _tray = tray;
        _sessionStartedAt = DateTimeOffset.Now;

        InitializeComponent();

        Width = settings.Current.WindowWidth;
        Height = settings.Current.WindowHeight;
        Topmost = settings.Current.Topmost;

        TabStrip.ItemsSource = _tabs.Tabs;
        _tabs.ActiveChanged += OnActiveTabChanged;
        _tabs.TabTextChanged += OnTabTextChanged;

        Editor.TextArea.TextView.LineTransformers.Add(_ideographicSpace);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_proofreadingRenderer);
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

        _usageRolloverTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _usageRolloverTimer.Tick += (_, _) => RefreshUsageForRollover();
        Application.Current.Exit += (_, _) => _usageRolloverTimer.Stop();

        _proofreadingTimer = new DispatcherTimer();
        _proofreadingTimer.Tick += async (_, _) =>
        {
            _proofreadingTimer.Stop();
            await RunProofreadingAsync(manual: false);
        };
        ConfigureProofreadingSchedule();

        SetupInputBindings();
        ApplyEditorSettings();
        ApplyThemeToEditor();
        OnActiveTabChanged(_tabs.Active);
        DateTimeOffset initialUsageNow = DateTimeOffset.Now;
        if (RefreshUsageDisplay(initialUsageNow))
            _usageDisplayDate = LocalDate(initialUsageNow);
        _usageRolloverTimer.Start();
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
        SuppressAutoHide();

        // 正常系では BeginInvoke のコールバックが後から1回だけ解放する。ここでの解放は
        // 「その前に何かで失敗した」経路専用なので、二重解放を避けるために一度きりにする。
        bool suppressionReleased = false;
        void ReleaseOnce()
        {
            if (suppressionReleased) return;
            suppressionReleased = true;
            ReleaseAutoHide();
        }

        try
        {
            NativeMethods.SetWindowPos(_handle, IntPtr.Zero, -30000, -30000, 480, 600,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            Show();

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                Hide();
                ShowActivated = true;
                ReleaseOnce();
            });
        }
        catch (InvalidOperationException)
        {
            // 例外の握りつぶし方自体は変えない。この型だけは起動続行のため飲み込む。
            ShowActivated = true;
            ReleaseOnce();
        }
        catch
        {
            // それ以外の例外はこれまで通り再スローする。抑止カウントの解放漏れだけを防ぐ。
            ReleaseOnce();
            throw;
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
        if (_hideSuppression.IsSuppressed) return;         // 設定ダイアログなどを開いている

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
        ConfigureProofreadingSchedule();
        // 上限額・警告閾値が変わった直後に進捗バー・ツールチップ・自動停止表示を反映する
        // （UsageLimitNotificationTrackerの鍵は上限額を含むため、変更後は再び通知できる）。
        RefreshUsageDisplay();
        ScheduleAutomaticProofreading();
        StartupRegistration.Sync(settings.StartWithWindows);
        // 保持期間を短くした直後に効かせる。起動時にしか圧縮しないと、設定を変えても
        // 再起動するまで課金履歴画面が何も変わらず「設定が効いていない」ように見える。
        CompactApiLogsInBackground();

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
        _proofreadingRenderer.UnderlineBrush = Brush("ProofreadingUnderlineBrush");
        _proofreadingRenderer.SelectedBackgroundBrush = Brush("ProofreadingSelectionBrush");

        FindPanel.ApplyTheme(Brush("SearchMatchBrush"), Brush("SearchCurrentMatchBrush"));

        Editor.TextArea.TextView.Redraw();
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    // ================= タブ =================

    private void OnActiveTabChanged(ScratchTab? tab)
    {
        if (tab is null) return;

        if (_activeProofreading is not null)
            _activeProofreading.Changed -= OnProofreadingChanged;

        // Document を差し替えるとキャレットが 0 に戻り、その通知で tab.CaretOffset が
        // 上書きされてしまう。復元したい位置は先に控えておく。
        var restoreOffset = Math.Clamp(tab.CaretOffset, 0, tab.Document.TextLength);

        Editor.Document = tab.Document;
        _activeProofreading = tab.Proofreading;
        _activeProofreading.Changed += OnProofreadingChanged;
        _selectedProposal = null;
        Editor.CaretOffset = restoreOffset;
        Editor.ScrollToLine(Editor.TextArea.Caret.Line);

        FindPanel.OnDocumentSwapped();
        RefreshProofreadingPresentation();
        UpdateStatus();
        ScheduleAutomaticProofreading();
    }

    private void OnTabTextChanged(ScratchTab tab)
    {
        _proofreadingSchedule.NotifyChanged(tab.Id, DateTimeOffset.Now);
        if (ReferenceEquals(tab, _tabs.Active))
            ScheduleAutomaticProofreading();
    }

    private void OnProofreadingChanged()
        => RefreshProofreadingPresentation();

    private void RefreshProofreadingPresentation()
    {
        IReadOnlyList<ProofreadingProposal> proposals =
            _activeProofreading?.Proposals
                .Where(proposal => proposal.IsActive)
                .OrderBy(proposal => proposal.Start)
                .ToArray() ?? [];

        if (_selectedProposal is null ||
            !_selectedProposal.IsActive ||
            !proposals.Contains(_selectedProposal))
        {
            _selectedProposal = proposals.FirstOrDefault();
        }

        _proofreadingRenderer.Proposals = proposals;
        _proofreadingRenderer.Selected = _selectedProposal;

        if (_selectedProposal is null)
        {
            ProofreadingPanel.Visibility = Visibility.Collapsed;
            ProposalPositionText.Text = "";
            ProposalChangeText.Text = "";
        }
        else
        {
            int index = IndexOfProposal(proposals, _selectedProposal);
            ProposalPositionText.Text = $"{index + 1}/{proposals.Count}";
            ProposalChangeText.Text =
                $"「{_selectedProposal.Original}」→「{_selectedProposal.Suggestion}」";
            ProofreadingPanel.Visibility = Visibility.Visible;
        }

        Editor.TextArea.TextView.Redraw();
    }

    private void SelectProposal(ProofreadingProposal proposal, bool scrollIntoView)
    {
        if (!proposal.IsActive)
            return;

        _selectedProposal = proposal;
        RefreshProofreadingPresentation();

        if (!scrollIntoView)
            return;

        int line = Editor.Document.GetLineByOffset(proposal.Start).LineNumber;
        Editor.ScrollToLine(line);
    }

    private void SelectRelativeProposal(int offset)
    {
        IReadOnlyList<ProofreadingProposal> proposals =
            _activeProofreading?.Proposals
                .Where(proposal => proposal.IsActive)
                .OrderBy(proposal => proposal.Start)
                .ToArray() ?? [];
        if (proposals.Count == 0)
            return;

        ProofreadingProposal? next =
            _activeProofreading?.GetRelative(_selectedProposal, offset);
        if (next is not null)
            SelectProposal(next, scrollIntoView: true);
    }

    private static int IndexOfProposal(
        IReadOnlyList<ProofreadingProposal> proposals,
        ProofreadingProposal proposal)
    {
        for (int index = 0; index < proposals.Count; index++)
        {
            if (ReferenceEquals(proposals[index], proposal))
                return index;
        }

        return -1;
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
        Bind(Key.B, ModifierKeys.Control | ModifierKeys.Shift, OpenBillingHistory);

        Bind(Key.S, ModifierKeys.Control | ModifierKeys.Shift, ExportActiveTab);
        Bind(Key.Enter, ModifierKeys.Control, RunManualProofreading);
        Bind(Key.F8, ModifierKeys.None, () => SelectRelativeProposal(1));
        Bind(Key.F8, ModifierKeys.Shift, () => SelectRelativeProposal(-1));
        Bind(Key.OemPeriod, ModifierKeys.Control, AcceptSelectedProposal);
        Bind(Key.OemComma, ModifierKeys.Control, RejectSelectedProposal);
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

    private void Editor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeProofreading is null)
            return;

        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is null)
            return;

        int offset = Editor.Document.GetOffset(position.Value.Location);
        ProofreadingProposal? proposal = _activeProofreading.FindAtOffset(offset);
        if (proposal is null)
            return;

        SelectProposal(proposal, scrollIntoView: false);
        Editor.TextArea.Focus();
        e.Handled = true;
    }

    private void PreviousProposalButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRelativeProposal(-1);
        Editor.TextArea.Focus();
    }

    private void NextProposalButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRelativeProposal(1);
        Editor.TextArea.Focus();
    }

    private void AcceptProposalButton_Click(object sender, RoutedEventArgs e)
        => AcceptSelectedProposal();

    private void RejectProposalButton_Click(object sender, RoutedEventArgs e)
        => RejectSelectedProposal();

    private void ReasonProposalButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReasonProposalButton.ContextMenu is not { } menu)
            return;

        menu.PlacementTarget = ReasonProposalButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void RejectWithReasonMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProposal is not { IsActive: true } proposal ||
            !TryGetReason(generatesAlternative: false, out string reason))
        {
            return;
        }

        if (!CanReactTo(proposal))
            return;

        _reactions.Add(
            _tabs.Active?.Id,
            proposal,
            ProofreadingReaction.RejectWithReason,
            reason);
        _activeProofreading?.Reject(proposal);
        SetTransientStatus("理由を記録して拒否しました");
        Editor.TextArea.Focus();
        MaybeOfferStyleGuideGeneration();
    }

    private async void AlternativeWithReasonMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_alternativeInProgress ||
            _proofreadingRunInProgress ||
            _styleGuideGenerationInProgress ||
            _selectedProposal is not { IsActive: true } proposal ||
            !TryGetReason(generatesAlternative: true, out string reason))
        {
            return;
        }

        SuppressAutoHide();
        string? apiKey = GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                this,
                $"{ActiveProviderName()} APIキーが設定されていません。設定画面で登録してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ReleaseAutoHide();
            return;
        }

        if (_settings.Current.ConfirmPaidApiCalls)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"別案生成のため{ActiveProviderName()} APIを1回呼び出します。料金が発生します。\n\n" +
                BuildPricingSummary() + "\n" +
                "実行後に使用トークン数と料金を表示します。\n\n実行しますか？",
                "API料金の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                ReleaseAutoHide();
                return;
            }
        }

        _alternativeInProgress = true;
        SetProposalActionsEnabled(false);
        UpdateTrayIconState();
        ApiUsageCost? failedApiCost = null;
        try
        {
            SetProofreadingStatus("別案を生成しています…");
            var stopwatch = Stopwatch.StartNew();
            GeminiAlternativeResult result;
            try
            {
                result = await _proofreadingClient.GenerateAlternativeAsync(proposal, reason);
            }
            catch (GeminiClientException ex)
            {
                stopwatch.Stop();
                _apiErrorSticky = true;
                failedApiCost = RecordFailedApiCall(
                    ApiCallTrigger.Realternative,
                    ex,
                    stopwatch.Elapsed);
                throw;
            }

            _apiErrorSticky = false;

            if (!CanReactTo(proposal))
            {
                RecordedApiCall recorded = RecordSuccessfulApiCall(
                    ApiCallTrigger.Realternative,
                    result.Usage,
                    result.Elapsed,
                    suggestionCount: 0,
                    discardedCount: 1);
                ShowAlternativeCost(result, recorded.Cost,
                    "生成中に本文が変更されたため、別案は適用しませんでした。");
                return;
            }

            _reactions.Add(
                _tabs.Active?.Id,
                proposal,
                ProofreadingReaction.RejectWithReason,
                reason);

            if (_activeProofreading?.TryReplaceSuggestion(
                    proposal,
                    result.Alternative) != true)
            {
                RecordedApiCall recorded = RecordSuccessfulApiCall(
                    ApiCallTrigger.Realternative,
                    result.Usage,
                    result.Elapsed,
                    suggestionCount: 0,
                    discardedCount: 1);
                ShowAlternativeCost(result, recorded.Cost,
                    "有効な別案へ差し替えられませんでした。");
                return;
            }

            RecordedApiCall recordedSuccess = RecordSuccessfulApiCall(
                ApiCallTrigger.Realternative,
                result.Usage,
                result.Elapsed,
                suggestionCount: 1,
                discardedCount: 0);
            ShowAlternativeCost(result, recordedSuccess.Cost, "別案を表示しました。");
        }
        catch (GeminiClientException ex)
        {
            string knownUsage = ex.Usage is not null && failedApiCost is not null
                ? BuildUsageText([failedApiCost])
                : "応答を受け取れなかったため、使用量と料金は確認できませんでした。";
            MessageBox.Show(
                this,
                ex.Message + "\n\n" + knownUsage,
                "別案を生成できませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetProofreadingStatus(ex.Usage is null
                ? "別案生成に失敗しました（使用量・料金は未確認）"
                : "別案生成に失敗しました " +
                  (failedApiCost is null ? "（料金未確認）" : FormatCostWithJpy(failedApiCost)),
                force: true);
        }
        finally
        {
            _alternativeInProgress = false;
            SetProposalActionsEnabled(true);
            UpdateTrayIconState();
            ReleaseAutoHide();
            Activate();
            Editor.TextArea.Focus();
            // _reactions.Add はこのメソッドの本体（_alternativeInProgress中）で呼ばれているため、
            // しきい値判定はここまで遅延させる（要件3.4.2、_alternativeInProgress解除後に判定する設計）。
            MaybeOfferStyleGuideGeneration();
        }
    }

    private void AcceptSelectedProposal()
    {
        if (_selectedProposal is not { IsActive: true } proposal ||
            !CanReactTo(proposal))
        {
            return;
        }

        _reactions.Add(
            _tabs.Active?.Id,
            proposal,
            ProofreadingReaction.Accept);
        if (_activeProofreading?.TryApply(proposal) == true)
            SetTransientStatus("修正を許可しました");
        Editor.TextArea.Focus();
        MaybeOfferStyleGuideGeneration();
    }

    private void RejectSelectedProposal()
    {
        if (_selectedProposal is not { IsActive: true } proposal ||
            !CanReactTo(proposal))
        {
            return;
        }

        _reactions.Add(
            _tabs.Active?.Id,
            proposal,
            ProofreadingReaction.Reject);
        _activeProofreading?.Reject(proposal);
        SetTransientStatus("修正を拒否しました");
        Editor.TextArea.Focus();
        MaybeOfferStyleGuideGeneration();
    }

    private bool CanReactTo(ProofreadingProposal proposal)
        => proposal.IsActive &&
           _activeProofreading is not null &&
           _activeProofreading.Proposals.Contains(proposal) &&
           string.Equals(
               Editor.Document.GetText(proposal.Start, proposal.Length),
               proposal.Original,
               StringComparison.Ordinal);

    private bool TryGetReason(bool generatesAlternative, out string reason)
    {
        SuppressAutoHide();
        try
        {
            var dialog = new ProofreadingReasonDialog(
                _reactions.GetRecentReasons(),
                generatesAlternative)
            {
                Owner = this,
            };
            bool accepted = dialog.ShowDialog() == true;
            reason = accepted ? dialog.Reason : "";
            return accepted;
        }
        finally
        {
            ReleaseAutoHide();
        }
    }

    private void SetProposalActionsEnabled(bool enabled)
    {
        AcceptProposalButton.IsEnabled = enabled;
        RejectProposalButton.IsEnabled = enabled;
        ReasonProposalButton.IsEnabled = enabled;
    }

    private void ShowAlternativeCost(
        GeminiAlternativeResult result,
        ApiUsageCost cost,
        string message)
    {
        string summary =
            $"{message}\n\n入力 {result.Usage.PromptTokens:N0}、" +
            $"出力・推論 {result.Usage.BillableOutputTokens:N0} tokens\n" +
            $"料金 {FormatCostWithJpy(cost)}";

        MessageBox.Show(
            this,
            summary,
            "別案生成の使用量",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        SetProofreadingStatus(
            $"別案 ↑{result.Usage.PromptTokens:N0} " +
            $"↓{result.Usage.BillableOutputTokens:N0} tok  " +
            FormatCostWithJpy(cost),
            force: true);
    }

    // ================= 校正の実行制御 =================

    private void ConfigureProofreadingSchedule()
    {
        AppSettings settings = _settings.Current;
        _proofreadingSchedule.Debounce =
            TimeSpan.FromMilliseconds(settings.ProofreadingDebounceMs);
        _proofreadingSchedule.MinimumSendInterval =
            TimeSpan.FromSeconds(settings.ProofreadingMinimumIntervalSeconds);
        if (!settings.AutoProofreadingEnabled)
            _proofreadingTimer.Stop();
    }

    private void ScheduleAutomaticProofreading()
    {
        _proofreadingTimer.Stop();
        if (!_settings.Current.AutoProofreadingEnabled ||
            _proofreadingRunInProgress ||
            _tabs.Active is not { } tab)
        {
            return;
        }

        // 発火条件5（月間上限）。ここでタイマーの再開始そのものを止めないと、
        // NotifyChanged 済みの変更が残ったまま「タイマー発火→ガードで却下→
        // ScheduleAutomaticProofreadingを再度呼ぶ」を100ms間隔で繰り返すビジーループになる。
        if (IsMonthlyLimitReached())
            return;

        DateTimeOffset? due = _proofreadingSchedule.GetAutomaticDueAt(tab.Id);
        if (due is null)
            return;

        TimeSpan delay = due.Value - DateTimeOffset.Now;
        if (delay < TimeSpan.FromMilliseconds(100))
            delay = TimeSpan.FromMilliseconds(100);
        _proofreadingTimer.Interval = delay;
        _proofreadingTimer.Start();
    }

    private void RunManualProofreading()
        => _ = RunProofreadingAsync(manual: true);

    /// <summary>
    /// 要件3.4.2: リアクションが一定件数たまるごとにスタイルガイド生成を提案する。
    /// リアクション記録の直後（3箇所）と、別案生成完了後（_alternativeInProgress解除後）から呼ぶ。
    /// 校正・別案生成・スタイルガイド生成のいずれかが進行中は判定自体をスキップし、
    /// 次にこのメソッドが呼ばれるタイミングへ先送りする（しきい値は減らないので取りこぼさない）。
    /// </summary>
    private void MaybeOfferStyleGuideGeneration()
    {
        if (_proofreadingRunInProgress ||
            _alternativeInProgress ||
            _styleGuideGenerationInProgress ||
            !_settings.Current.StyleGuideAutoGenerateEnabled)
        {
            return;
        }

        long total;
        long cursor;
        try
        {
            total = _reactions.GetTotalCount();
            cursor = _styleGuides.GetReviewCursor();
        }
        catch (Exception)
        {
            // 補助機能の読み取り失敗でリアクション記録そのものは壊さない。
            return;
        }

        if (total - cursor < _settings.Current.StyleGuideGenerationThreshold)
            return;

        _ = RunStyleGuideGenerationAsync(total);
    }

    /// <summary>
    /// スタイルガイドの自動生成。要件3.4.2により、「課金API実行前の確認を表示する」設定に関わらず
    /// 必ず確認ダイアログを出す（ConfirmPaidApiCallsとは独立）。月間上限に達している間は生成せず、
    /// 承諾・辞退・上限到達のいずれでもカーソルを進め、次のしきい値まで再確認しない。
    /// </summary>
    private async Task RunStyleGuideGenerationAsync(long totalReactionsAtCheck)
    {
        if (IsMonthlyLimitReached())
        {
            TryAdvanceReviewCursor(totalReactionsAtCheck);
            return;
        }

        SuppressAutoHide();
        bool confirmed;
        try
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"リアクションが{_settings.Current.StyleGuideGenerationThreshold}件以上たまりました。" +
                $"{ActiveProviderName()} APIを1回呼び出して、あなたの文体ルール（スタイルガイド）を生成しますか？\n\n" +
                BuildPricingSummary() + "\n" +
                "実行後に使用トークン数と料金を表示します。生成後は設定画面でいつでも閲覧・編集・削除できます。",
                "スタイルガイドの自動生成",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            confirmed = confirmation == MessageBoxResult.Yes;
        }
        finally
        {
            ReleaseAutoHide();
        }

        // 承諾・辞退のどちらでも今回分の蓄積は消費済み扱いにする。辞退のたびに毎回確認を
        // 出さないための措置（次にまた閾値ぶん積み上がるまで再確認しない）。
        TryAdvanceReviewCursor(totalReactionsAtCheck);
        if (!confirmed)
            return;

        string? apiKey = GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(
                this,
                $"{ActiveProviderName()} APIキーが設定されていません。設定画面で登録してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<FewShotCandidate> candidates =
            _reactions.GetFewShotCandidates(StyleGuideSourceSelector.MaxReactions);
        StyleGuideSourceSelection source = StyleGuideSourceSelector.Select(candidates);
        if (source.Examples.Count == 0)
        {
            SetProofreadingStatus("スタイルガイド生成に使えるリアクションがありません", force: true);
            return;
        }

        _styleGuideGenerationInProgress = true;
        UpdateTrayIconState();
        try
        {
            SetProofreadingStatus("スタイルガイドを生成しています…");
            var stopwatch = Stopwatch.StartNew();
            GeminiStyleGuideResult result;
            try
            {
                result = await _proofreadingClient.GenerateStyleGuideAsync(source.Examples);
            }
            catch (GeminiClientException ex)
            {
                stopwatch.Stop();
                _apiErrorSticky = true;
                RecordFailedApiCall(ApiCallTrigger.StyleGuide, ex, stopwatch.Elapsed);
                MessageBox.Show(
                    this,
                    "スタイルガイドを生成できませんでした。\n\n" + ex.Message,
                    "JP Scratch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                SetProofreadingStatus("スタイルガイド生成に失敗しました", force: true);
                return;
            }

            _apiErrorSticky = false;
            RecordedApiCall recorded = RecordSuccessfulApiCall(
                ApiCallTrigger.StyleGuide,
                result.Usage,
                result.Elapsed,
                suggestionCount: 1,
                discardedCount: 0);

            // 課金済みの生成結果をここで失うのが最悪なので、保存の成否を必ずユーザーへ伝える。
            // RecordApiCallと違い黙って握りつぶさない（課金ログはロギングの補助情報だが、
            // こちらは今回の支払いに対する唯一の成果物のため）。
            bool saved;
            try
            {
                _styleGuides.Generate(result.Content, source.Examples.Count);
                saved = true;
            }
            catch (Exception)
            {
                saved = false;
            }

            if (saved)
            {
                MessageBox.Show(
                    this,
                    "スタイルガイドを生成しました。設定画面で内容を確認・編集できます。\n\n" +
                    $"入力 {result.Usage.PromptTokens:N0}、出力・推論 {result.Usage.BillableOutputTokens:N0} tokens\n" +
                    $"料金 {FormatCostWithJpy(recorded.Cost)}",
                    "スタイルガイドの生成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetProofreadingStatus("スタイルガイドを生成しました " + FormatCostWithJpy(recorded.Cost), force: true);
            }
            else
            {
                MessageBox.Show(
                    this,
                    "スタイルガイドは生成されましたが、保存に失敗しました。" +
                    "料金は発生済みです。内容を手元に控えてください。\n\n" +
                    result.Content,
                    "スタイルガイドを保存できませんでした",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                SetProofreadingStatus(
                    "スタイルガイドの保存に失敗しました " + FormatCostWithJpy(recorded.Cost), force: true);
            }
        }
        finally
        {
            _styleGuideGenerationInProgress = false;
            UpdateTrayIconState();
        }
    }

    /// <summary>
    /// カーソル更新は補助的な既読管理であり、失敗しても確認フロー自体は止めない
    /// （最悪の場合、次回も同じ件数ぶんで再確認が出るだけで、データを失うわけではない）。
    /// </summary>
    private void TryAdvanceReviewCursor(long totalReactionsAtCheck)
    {
        try
        {
            _styleGuides.AdvanceReviewCursor(totalReactionsAtCheck);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// トレイアイコンの状態を今の条件から計算し直す（要件 3.1.1）。
    /// 条件が変わりうる箇所（校正の開始・終了、API の成否、当月累計・上限額の更新）から呼ぶ。
    /// 状態が変わらなければ <see cref="TrayIconService.SetState"/> 側で握り潰されるので、
    /// 呼びすぎても実害はない。MainWindow のコンストラクタはトレイ初期化より前に走るが、
    /// その場合も状態は覚えられ、初期化時に反映される。
    /// </summary>
    private void UpdateTrayIconState()
        => _tray.SetState(TrayIconStateResolver.Resolve(
            proofreading: _proofreadingRunInProgress || _alternativeInProgress ||
                          _styleGuideGenerationInProgress,
            apiError: _apiErrorSticky,
            limitReached: IsMonthlyLimitReached()));

    private async Task RunProofreadingAsync(bool manual)
    {
        if (_proofreadingRunInProgress ||
            _alternativeInProgress ||
            _styleGuideGenerationInProgress ||
            _tabs.Active is not { } tab)
        {
            return;
        }

        if (!manual &&
            (!_settings.Current.AutoProofreadingEnabled ||
             !_proofreadingSchedule.IsAutomaticDue(tab.Id, DateTimeOffset.Now) ||
             IsMonthlyLimitReached()))
        {
            // 発火条件5（月間上限）を含め、ここで弾かれた場合もScheduleAutomaticProofreadingを
            // 呼ぶが、そちら側も同じ判定を持つため、上限到達中はタイマーが再始動しない。
            ScheduleAutomaticProofreading();
            return;
        }

        if (NativeMethods.HasImeComposition(_handle))
        {
            SetProofreadingStatus("IME変換の確定後に校正します", force: true);
            if (!manual)
            {
                _proofreadingTimer.Interval = TimeSpan.FromMilliseconds(500);
                _proofreadingTimer.Start();
            }
            return;
        }

        string snapshot = tab.Document.Text;
        if (snapshot.Length == 0)
        {
            if (!manual)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            return;
        }

        bool selectionRun = manual && Editor.SelectionLength > 0;
        ProofreadingPlan plan = selectionRun
            ? tab.ProofreadingPlanner.CreateSelectionPlan(
                snapshot,
                Editor.SelectionStart,
                Editor.SelectionLength)
            : tab.ProofreadingPlanner.CreateAutomaticPlan(snapshot);

        if (plan.Requests.Count == 0)
        {
            if (!manual)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            SetProofreadingStatus("校正が必要な変更はありません", force: true);
            return;
        }

        string? apiKey = GetActiveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (!manual)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            SetProofreadingStatus(
                $"{ActiveProviderName()} APIキーを設定してください",
                force: true);
            return;
        }

        if (!ConfirmProofreadingApiUse(plan.Requests.Count, manual))
        {
            if (!manual)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            SetProofreadingStatus("校正をキャンセルしました", force: true);
            return;
        }

        // 要件3.4.4: スタイルガイド・カスタム指示は文書全体で共通なので実行中1回だけ読む。
        // few-shotだけは校正対象ごとに語句の重なりが変わるため、候補プールをここで一度読み、
        // リクエストごとの選定（CPUのみ）はループ内で行う。
        // ここは補助機能（学習素材）の読み取りであり、失敗しても校正そのものは殺さない
        // （MaybeOfferStyleGuideGenerationと同じ方針）。_proofreadingRunInProgress = true と
        // それに対応するfinallyの間に置くと、ここで例外が出た場合にfinallyへ到達できず、
        // 入口ガードにより自動校正が永久に弾かれ続ける（再起動以外に復帰手段がなくなる）ため、
        // フラグを立てる前に済ませる。
        string? styleGuideContent;
        string? customInstruction;
        IReadOnlyList<FewShotCandidate> fewShotCandidates;
        try
        {
            styleGuideContent = _styleGuides.GetActive()?.Content;
            customInstruction = string.IsNullOrWhiteSpace(_settings.Current.CustomInstruction)
                ? null
                : _settings.Current.CustomInstruction;
            fewShotCandidates = _reactions.GetFewShotCandidates();
        }
        catch (Exception)
        {
            // 学習素材が読めなくても、学習を反映しないv2相当の校正として続行する。
            styleGuideContent = null;
            customInstruction = null;
            fewShotCandidates = [];
        }

        _proofreadingRunInProgress = true;
        _proofreadingTimer.Stop();
        SetProposalActionsEnabled(false);
        UpdateTrayIconState();

        List<(ProofreadingRequest Request, GeminiProofreadingResult Result)> results = [];
        List<long> successfulApiCallIds = [];
        List<ApiUsageCost> responseCosts = [];
        ApiCallTrigger trigger = manual ? ApiCallTrigger.Manual : ApiCallTrigger.Auto;
        bool resultsApplied = false;
        try
        {
            for (int index = 0; index < plan.Requests.Count; index++)
            {
                if (!ReferenceEquals(tab, _tabs.Active) ||
                    !string.Equals(tab.Document.Text, snapshot, StringComparison.Ordinal))
                {
                    MarkApiCallsDiscarded(successfulApiCallIds);
                    SetProofreadingStatus("本文が変更されたため校正結果を破棄しました", force: true);
                    return;
                }

                TimeSpan intervalDelay =
                    _proofreadingSchedule.GetDelayBeforeSend(DateTimeOffset.Now);
                if (intervalDelay > TimeSpan.Zero)
                {
                    SetProofreadingStatus(
                        $"次の校正送信まで {Math.Ceiling(intervalDelay.TotalSeconds):0} 秒");
                    await Task.Delay(intervalDelay);
                }

                if (!ReferenceEquals(tab, _tabs.Active) ||
                    !string.Equals(tab.Document.Text, snapshot, StringComparison.Ordinal))
                {
                    MarkApiCallsDiscarded(successfulApiCallIds);
                    SetProofreadingStatus("本文が変更されたため残りの校正を中止しました", force: true);
                    return;
                }

                SetProofreadingStatus(
                    $"校正しています… {index + 1}/{plan.Requests.Count}");
                ProofreadingRequest request = plan.Requests[index];
                FewShotSelection fewShotSelection = FewShotSelector.Select(
                    fewShotCandidates, request.SourceText);
                request = request with
                {
                    SystemInstructionOverride = ProofreadingPrompt.BuildSystemInstruction(
                        styleGuideContent, customInstruction, fewShotSelection.Examples),
                };
                var stopwatch = Stopwatch.StartNew();
                GeminiProofreadingResult result;
                try
                {
                    result = await _proofreadingClient.ProofreadAsync(request);
                }
                catch (GeminiClientException ex)
                {
                    stopwatch.Stop();
                    _apiErrorSticky = true;
                    ApiUsageCost? failedCost = RecordFailedApiCall(trigger, ex, stopwatch.Elapsed);
                    if (failedCost is not null)
                        responseCosts.Add(failedCost);
                    throw;
                }

                _apiErrorSticky = false;

                RecordedApiCall recordedApiCall = RecordSuccessfulApiCall(
                    trigger,
                    result.Usage,
                    result.Elapsed,
                    suggestionCount: result.Diff.Accepted ? result.Diff.Changes.Count : 0,
                    discardedCount: result.Diff.Accepted ? 0 : 1);
                if (recordedApiCall.Id is long id)
                    successfulApiCallIds.Add(id);
                responseCosts.Add(recordedApiCall.Cost);
                _proofreadingSchedule.MarkSent(DateTimeOffset.Now);
                results.Add((request, result));
            }

            if (!selectionRun)
            {
                tab.ProofreadingPlanner.MarkSent(plan);
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            }

            if (!ReferenceEquals(tab, _tabs.Active) ||
                !string.Equals(tab.Document.Text, snapshot, StringComparison.Ordinal))
            {
                MarkApiCallsDiscarded(successfulApiCallIds);
                SetProofreadingStatus("本文が変更されたため校正結果を破棄しました", force: true);
                return;
            }

            string corrected = ProofreadingResultMerger.Merge(plan, results);
            DocumentDiffResult loaded =
                tab.Proofreading.LoadCorrectedDocument(corrected);
            if (!loaded.Accepted)
            {
                MarkApiCallsDiscarded(successfulApiCallIds);
                SetProofreadingStatus("安全検査に失敗したため校正結果を破棄しました", force: true);
                return;
            }

            // ここ以降の例外は、提案（0件を含む）が既にUIへ反映された後のもの。
            resultsApplied = true;
            int proposals = loaded.Accepted ? loaded.Changes.Count : 0;
            ShowProofreadingUsage(proposals, responseCosts);
        }
        catch (GeminiClientException ex)
        {
            if (!resultsApplied)
                MarkApiCallsDiscarded(successfulApiCallIds);
            if (!selectionRun)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            string knownUsage = responseCosts.Count == 0
                ? "使用量・料金は確認できませんでした。"
                : BuildUsageText(responseCosts);
            MessageBox.Show(
                this,
                ex.Message + "\n\n" + knownUsage,
                "校正できませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetProofreadingStatus(responseCosts.Count == 0
                ? "校正に失敗しました（使用量・料金は未確認）"
                : "校正に失敗しました " + FormatCostWithJpy(responseCosts),
                force: true);
        }
        catch
        {
            if (!resultsApplied)
                MarkApiCallsDiscarded(successfulApiCallIds);
            throw;
        }
        finally
        {
            _proofreadingRunInProgress = false;
            SetProposalActionsEnabled(true);
            UpdateTrayIconState();
            ScheduleAutomaticProofreading();
        }
    }

    private bool ConfirmProofreadingApiUse(int requestCount, bool manual)
    {
        if (!_settings.Current.ConfirmPaidApiCalls)
            return true;

        SuppressAutoHide();
        try
        {
            string trigger = manual ? "手動校正" : "自動校正";
            decimal limit = _settings.Current.MonthlyLimitUsd;
            // 自動実行はScheduleAutomaticProofreading/RunProofreadingAsyncの発火条件5で
            // 到達時点で既に止めてあるため、ここまで到達するのは基本的に手動実行のときだけ。
            string limitWarning = IsMonthlyLimitReached()
                ? $"月間上限（${UsageFormatting.FormatUsd(limit)}）に達しています" +
                  $"（当月累計 ${UsageFormatting.FormatUsd(_monthUsageUsd)}）。" +
                  "このまま実行すると上限を超えます。\n\n"
                : "";
            MessageBoxResult result = MessageBox.Show(
                this,
                $"{trigger}で{ActiveProviderName()} APIを{requestCount}回呼び出します。料金が発生します。\n\n" +
                limitWarning +
                BuildPricingSummary() + "\n" +
                "複数回の場合は各送信の間隔を空け、実行後に合計料金を表示します。\n\n" +
                "実行しますか？",
                "API料金の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }
        finally
        {
            ReleaseAutoHide();
        }
    }

    /// <summary>
    /// APIの結果を課金ログへ残す。ログ保存に失敗しても、既に得た校正結果や既存のUI処理は壊さない。
    /// </summary>
    private RecordedApiCall RecordSuccessfulApiCall(
        ApiCallTrigger trigger,
        GeminiUsage usage,
        TimeSpan elapsed,
        int suggestionCount,
        int discardedCount)
    {
        ApiUsageCost cost = CreateUsageCost(
            usage.PromptTokens, usage.BillableOutputTokens);
        return new RecordedApiCall(RecordApiCall(new ApiCallLogEntry(
            trigger,
            _proofreadingClient.Model,
            usage.PromptTokens,
            usage.BillableOutputTokens,
            cost.UsdCost,
            ToDurationMilliseconds(elapsed),
            ApiCallStatus.Ok,
            null,
            suggestionCount,
            discardedCount,
            cost.FxRate)), cost);
    }

    private ApiUsageCost? RecordFailedApiCall(
        ApiCallTrigger trigger,
        GeminiClientException exception,
        TimeSpan elapsed)
    {
        // キー未設定はHTTP送信より前の失敗なので、API呼び出しログには含めない。
        if (exception.Error == GeminiClientError.MissingApiKey)
            return null;

        GeminiUsage? usage = exception.Usage;
        int promptTokens = usage?.PromptTokens ?? 0;
        int outputTokens = usage?.BillableOutputTokens ?? 0;
        ApiUsageCost? cost = null;
        if (usage is not null)
        {
            try
            {
                cost = CreateUsageCost(promptTokens, outputTokens);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // 応答前失敗はUSD 0で、レートがあっても円額を0として固定する。
        cost ??= new ApiUsageCost(0, 0, 0m, _fxRates.GetCachedRate(), IsUsageKnown: false);

        RecordApiCall(new ApiCallLogEntry(
            trigger,
            _proofreadingClient.Model,
            promptTokens,
            outputTokens,
            cost.UsdCost,
            ToDurationMilliseconds(exception.Elapsed ?? elapsed),
            exception.Error == GeminiClientError.Timeout
                ? ApiCallStatus.Timeout
                : ApiCallStatus.Error,
            exception.Message,
            0,
            0,
            cost.FxRate));
        return cost;
    }

    private long? RecordApiCall(ApiCallLogEntry entry)
    {
        long id;
        try
        {
            id = _apiCalls.Add(entry);
        }
        catch (Exception)
        {
            // API呼び出し後に課金ログだけが失敗しても、校正結果の表示・既存のエラー処理は続ける。
            return null;
        }

        RefreshUsageDisplay();
        return id;
    }

    private void MarkApiCallsDiscarded(IEnumerable<long> ids)
    {
        bool changed = false;
        foreach (long id in ids)
        {
            try
            {
                changed |= _apiCalls.MarkDiscarded(id);
            }
            catch (Exception)
            {
                // 破棄数の更新が失敗しても、校正実行の既存処理は続ける。
            }
        }

        if (changed)
            RefreshUsageDisplay();
    }

    private static long ToDurationMilliseconds(TimeSpan duration)
        => (long)Math.Ceiling(Math.Max(0, duration.TotalMilliseconds));

    private void ShowProofreadingUsage(int proposals, IReadOnlyList<ApiUsageCost> costs)
    {
        string usage = BuildUsageText(costs);
        string compactUsage = usage == "使用量・料金は未確認"
            ? usage
            : usage[(usage.LastIndexOf('$'))..];
        SetProofreadingStatus(
            $"提案 {proposals}件  " +
            compactUsage,
            force: true);
    }

    private static string BuildUsageText(IReadOnlyList<ApiUsageCost> costs)
    {
        IReadOnlyList<ApiUsageCost> known = costs.Where(cost => cost.IsUsageKnown).ToArray();
        if (known.Count == 0)
            return "使用量・料金は未確認";

        int promptTokens = known.Sum(cost => cost.PromptTokens);
        int outputTokens = known.Sum(cost => cost.OutputTokens);
        string label = known.Count == costs.Count ? "" : "（既知分）";
        string unknown = known.Count == costs.Count ? "" : "（一部料金未確認）";
        return $"入力 {promptTokens:N0}、出力・推論 {outputTokens:N0} tokens{label} / " +
               $"料金 {FormatCostWithJpy(known)}{unknown}";
    }

    private ApiUsageCost CreateUsageCost(int promptTokens, int outputTokens)
    {
        PricingQuote quote = _pricing.Calculate(
            _proofreadingClient.Model, promptTokens, outputTokens);
        // 応答単位で一度だけキャッシュを読む。このsnapshotをログと全ての応答表示で共有する。
        FxRate? fxRate = _fxRates.GetCachedRate();
        if (fxRate is null)
            _ = RefreshFxRateAsync();
        return new ApiUsageCost(promptTokens, outputTokens, quote.UsdCost, fxRate, IsUsageKnown: true);
    }

    private string BuildPricingSummary()
    {
        ModelPricing pricing =
            _pricing.GetPricing(_proofreadingClient.Model);
        return
            $"{ProofreadingModelCatalog.DisplayName(_proofreadingClient.Model)} 単価（{pricing.UpdatedAt}）: " +
            $"入力 ${pricing.InputUsdPerMillion:0.####}／100万トークン、" +
            $"出力・推論 ${pricing.OutputUsdPerMillion:0.####}／100万トークン";
    }

    /// <summary>起動後に静かに日次レートを更新し、既存のUSD表示を壊さずに再描画する。</summary>
    internal async Task RefreshFxRateAsync()
    {
        try
        {
            await _fxRates.EnsureTodayAsync();
            RefreshUsageDisplay();
        }
        catch (Exception)
        {
            // 為替は補助情報であり、終了競合を含む失敗をUIへ出さない。
        }
    }

    private static string FormatCostWithJpy(ApiUsageCost cost)
        => !cost.IsUsageKnown
            ? "使用量・料金は未確認"
            : cost.JpyCost is decimal jpyCost
            ? $"${UsageFormatting.FormatUsd(cost.UsdCost)} ({UsageFormatting.FormatJpy(jpyCost)})" +
              UsageFormatting.FormatRateReference(cost.FxRate)
            : $"${UsageFormatting.FormatUsd(cost.UsdCost)} (¥—)";

    private static string FormatCostWithJpy(IReadOnlyList<ApiUsageCost> costs)
    {
        IReadOnlyList<ApiUsageCost> known = costs.Where(cost => cost.IsUsageKnown).ToArray();
        if (known.Count == 0)
            return "使用量・料金は未確認";

        decimal usdCost = known.Sum(cost => cost.UsdCost);
        if (known.Any(cost => cost.UsdCost != 0m && cost.JpyCost is null))
            return $"${UsageFormatting.FormatUsd(usdCost)} (¥—)" +
                   (known.Count == costs.Count ? "" : "（一部料金未確認）");

        decimal jpyCost = known.Sum(cost => cost.JpyCost ?? 0m);
        return $"${UsageFormatting.FormatUsd(usdCost)} ({UsageFormatting.FormatJpy(jpyCost)})" +
               FormatRateReferences(known) +
               (known.Count == costs.Count ? "" : "（一部料金未確認）");
    }

    private static string FormatRateReferences(IReadOnlyList<ApiUsageCost> costs)
    {
        (decimal Rate, DateOnly Date)[] rates = costs
            .Where(cost => cost.JpyCost is not null && cost.FxRate is not null)
            .Select(cost => (cost.FxRate!.UsdJpy, cost.FxRate.RateDate))
            .Distinct()
            .OrderBy(value => value.RateDate)
            .ToArray();
        if (rates.Length == 1)
            return UsageFormatting.FormatRateReference(new FxRate(rates[0].Date, rates[0].Rate, default));
        if (rates.Length > 1)
        {
            return $" (ログ固定レート合計 / {rates[0].Date:MM-dd}〜" +
                   $"{rates[^1].Date:MM-dd}・{rates.Length}レート)";
        }
        return "";
    }

    /// <summary>
    /// 永続ログから常設の利用状況を更新する。DB読み取りや表示更新が失敗した場合は、
    /// 校正・別案生成の操作を妨げず、最後に表示できた値を保つ。
    /// </summary>
    private bool RefreshUsageDisplay(DateTimeOffset? currentTime = null)
    {
        try
        {
            DateTimeOffset now = currentTime ?? DateTimeOffset.Now;
            DateTimeOffset todayStart = LocalStartOfDay(now);
            DateTimeOffset monthStart = LocalStartOfMonth(now);
            ApiCallLog? latest = _apiCalls.GetLatest();
            ApiCallUsageSummary session = _apiCalls.GetUsageSummary(_sessionStartedAt);
            ApiCallUsageSummary today = _apiCalls.GetUsageSummary(
                todayStart, LocalStartOfNextDay(now));
            ApiCallUsageSummary month = _apiCalls.GetUsageSummary(
                monthStart, LocalStartOfNextMonth(now));

            _monthUsageUsd = month.UsdCost;
            decimal limit = _settings.Current.MonthlyLimitUsd;
            UsageLimitState limitState = UsageLimitService.Evaluate(
                _monthUsageUsd, limit, _settings.Current.MonthlyLimitWarningRatio);

            string latestText = latest is null
                ? "直—"
                : $"直↑{latest.PromptTokens:N0}↓{latest.OutputTokens:N0}" +
                  $"${UsageFormatting.FormatUsd(latest.UsdCost)} " +
                  $"({UsageFormatting.FormatJpy(latest)}{UsageFormatting.FormatRateDateSuffix(latest)})";
            StatusUsage.Text =
                $"{latestText}｜起${UsageFormatting.FormatUsd(session.UsdCost)} ({UsageFormatting.FormatJpy(session)})｜" +
                $"日${UsageFormatting.FormatUsd(today.UsdCost)} ({UsageFormatting.FormatJpy(today)})｜" +
                $"月${UsageFormatting.FormatUsd(month.UsdCost)} ({UsageFormatting.FormatJpy(month)})";
            // 上限到達の警告は StatusUsage とは別の固定要素に出す。狭いウィンドウで StatusUsage が
            // CharacterEllipsis により省略されても、「自動停止」の理由が読めなくなることがないように
            // するため（実機で幅480px程度のとき、末尾に連結した文言ごと消えていた）。
            bool limitReached =
                limitState == UsageLimitState.Reached && _settings.Current.AutoProofreadingEnabled;
            StatusUsageLimitWarning.Visibility = limitReached ? Visibility.Visible : Visibility.Collapsed;
            StatusUsageLimitWarning.ToolTip = limitReached ? FormatUsageLimitTooltip(limit, limitState) : null;
            StatusUsage.ToolTip = string.Join(
                Environment.NewLine,
                FormatUsageTooltip("直近", latest),
                FormatUsageTooltip("起動後", session),
                FormatUsageTooltip("今日", today),
                FormatUsageTooltip("今月", month),
                FormatCachedRateTooltip(),
                FormatUsageLimitTooltip(limit, limitState),
                "クリックで課金履歴");

            UpdateUsageLimitProgressBar(limit, limitState);
            // 当月累計と上限額はここでしか更新されないので、トレイアイコンの再計算もここに置く
            // （起動時・校正後・日付や月の切替・設定変更のいずれもこの経路を通る）。
            UpdateTrayIconState();
            NotifyMonthlyLimitReachedIfNeeded(now, limitState, limit);

            _usageDisplayDate = LocalDate(now);
            return true;
        }
        catch (Exception)
        {
            // 集計表示は補助情報であり、読み取り失敗で既存UIを壊さない。
            return false;
        }
    }

    /// <summary>発火条件5（月間上限）: 送信前の当月累計が上限以上かどうか。事前見積りはしない。</summary>
    private bool IsMonthlyLimitReached()
        => UsageLimitService.IsReached(_monthUsageUsd, _settings.Current.MonthlyLimitUsd);

    /// <summary>
    /// ステータスバーの進捗バーへ反映する。上限が無制限（0以下）ならバーごと隠す
    /// （<c>Visibility="Collapsed"</c> なのでレイアウトは崩れない）。
    /// 色は SetResourceReference で動的に差し替える。StaticResource 相当の固定値にすると、
    /// 状態が変わらないままテーマだけ切り替わったときに古い色のまま固まってしまう。
    /// </summary>
    private void UpdateUsageLimitProgressBar(decimal limit, UsageLimitState state)
    {
        double? percent = UsageLimitService.ProgressPercent(_monthUsageUsd, limit);
        if (percent is null)
        {
            UsageLimitProgressBar.Visibility = Visibility.Collapsed;
            return;
        }

        UsageLimitProgressBar.Visibility = Visibility.Visible;
        UsageLimitProgressBar.Value = percent.Value;
        UsageLimitProgressBar.SetResourceReference(
            ForegroundProperty,
            state switch
            {
                UsageLimitState.Reached => "UsageProgressReachedBrush",
                UsageLimitState.Warning => "UsageProgressWarningBrush",
                _ => "UsageProgressNormalBrush",
            });
        UsageLimitProgressBar.ToolTip = FormatUsageLimitTooltip(limit, state);
    }

    private string FormatUsageLimitTooltip(decimal limit, UsageLimitState state)
    {
        if (limit <= 0m)
            return "月間上限: 無制限";

        decimal remaining = Math.Max(0m, limit - _monthUsageUsd);
        string stateText = state switch
        {
            UsageLimitState.Reached => "（到達 — 自動校正を停止中。手動は確認のうえ実行可）",
            UsageLimitState.Warning => "（接近中）",
            _ => "",
        };
        return $"月間上限: ${UsageFormatting.FormatUsd(limit)}　" +
               $"残り ${UsageFormatting.FormatUsd(remaining)}{stateText}";
    }

    /// <summary>
    /// 上限到達をトレイ通知する。「年月＋上限額」単位で一度だけに抑え、入力停止のたびに
    /// 自動チェックのガードへ引っかかっても通知が繰り返されないようにする。
    /// 月が変わるか上限額の設定が変わればまた通知できる（<see cref="UsageLimitNotificationTracker"/>）。
    /// <para>
    /// <see cref="TrayIconService.ShowMessage"/> が実際に発行できた（<c>true</c>）ときだけ
    /// <see cref="UsageLimitNotificationTracker.MarkNotified"/> を呼ぶ。先にマークしてから撃つ順序だと、
    /// tray が未初期化（起動直後）で黙って失敗した場合に「通知済み」だけが記録され、その月は
    /// 二度と通知されなくなる（実機で踏んだ不具合）。<see cref="RecheckUsageLimitNotificationAfterTrayReady"/>
    /// と組み合わせて、発行できなかった回はここで記録を残さず後で再試行できるようにする。
    /// </para>
    /// </summary>
    private void NotifyMonthlyLimitReachedIfNeeded(DateTimeOffset now, UsageLimitState state, decimal limit)
    {
        if (state != UsageLimitState.Reached)
            return;
        if (!_usageLimitNotifications.ShouldNotify(now, limit))
            return;

        bool delivered = _tray.ShowMessage(
            "月間上限に達しました",
            $"当月の利用額が上限 ${UsageFormatting.FormatUsd(limit)} に達したため、自動校正を停止しました。" +
            "手動実行は確認のうえ可能です。",
            isWarning: true);
        if (delivered)
            _usageLimitNotifications.MarkNotified(now, limit);
    }

    /// <summary>
    /// App.OnStartup が tray アイコンを初期化した直後に呼ぶ。<c>MainWindow</c> のコンストラクタは
    /// <c>TrayIconService.Initialize()</c>（<c>App.xaml.cs</c>）より先に走るため、起動時点で
    /// 月間上限に既に到達していても、コンストラクタ内の初回 <see cref="RefreshUsageDisplay"/> では
    /// 通知を発行できない。上のコメントの通り未発行なら通知済みとして記録していないので、
    /// ここでもう一度評価し直すだけで「tray が使えるようになった後に必ず1回通知される」を満たせる。
    /// </summary>
    internal void RecheckUsageLimitNotificationAfterTrayReady() => RefreshUsageDisplay();

    /// <summary>日付が変わったときだけ永続ログを再集計し、毎分のDB読取を避ける。</summary>
    private void RefreshUsageForRollover()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (LocalDate(now) == _usageDisplayDate)
            return;

        if (RefreshUsageDisplay(now))
            _usageDisplayDate = LocalDate(now);

        // 上限到達中はScheduleAutomaticProofreadingがタイマーの再始動そのものを止めているため、
        // 月替りで上限が解除されても、次のキー入力（NotifyChanged）が来るまで自動校正が
        // 再開しない。ロールオーバーのついでに再評価し、未送信の変更を取りこぼさないようにする。
        // ScheduleAutomaticProofreading自体は幂等（現在のタブの状態から再計算するだけ）なので、
        // 月替りでない日次ロールオーバーで呼んでも副作用はない。
        ScheduleAutomaticProofreading();

        // 日次判定はサービス側に任せ、通信失敗時も既存表示と校正をそのまま続ける。
        _ = RefreshFxRateAsync();

        // 常駐したままでも月をまたげば新しい明細が保持期限を越える。ここで呼ばないと、
        // 一度も再起動しない限り圧縮が走らない。
        CompactApiLogsInBackground();
    }

    /// <summary>
    /// 保持期限を過ぎた <c>api_calls</c> の明細を日次サマリへ圧縮する（要件 3.6.2）。
    /// 期間合計は <see cref="ApiCallRepository.GetUsageSummary"/> が両テーブルを合算するため変わらない。
    ///
    /// 起動時（<c>App.OnStartup</c>）・保持期間を変更したとき（<see cref="OnSettingsChanged"/>）・
    /// 日付が変わったとき（<see cref="RefreshUsageForRollover"/>）の3経路から呼ぶ。
    /// 起動時だけにすると、設定画面で保持期間を短くしても再起動するまで何も起きず、
    /// 「設定が効いていない」ように見える（実機で踏んだ）。
    ///
    /// <see cref="Database"/> は内部で直列化しているので、UIスレッドの読み書きと競合しても壊れない。
    /// 何度呼んでも結果が同じ（<see cref="ApiCallRepository.Compact"/> は既存サマリを取り込んでから
    /// 書き直す）なので、余分に呼んでも合計は狂わない。失敗しても本文編集・校正・課金表示は
    /// 続けられるため、握りつぶして次の機会に再試行する。
    /// </summary>
    internal void CompactApiLogsInBackground()
    {
        DateTimeOffset? cutoff = ApiLogRetention.ComputeCutoff(
            DateTimeOffset.Now, _settings.Current.ApiLogRetentionMonths);
        if (cutoff is null) return;

        _ = Task.Run(() =>
        {
            try
            {
                _apiCalls.Compact(cutoff.Value);
            }
            catch (Exception ex) when (
                ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
            {
            }
        });
    }

    private static DateOnly LocalDate(DateTimeOffset value)
    {
        DateTime local = value.LocalDateTime;
        return new DateOnly(local.Year, local.Month, local.Day);
    }

    // ローカル日/月の境界計算は Services/UsagePeriod.cs に集約し、課金履歴画面のクエリと共有する。
    private static DateTimeOffset LocalStartOfDay(DateTimeOffset value) => UsagePeriod.StartOfDay(value);

    private static DateTimeOffset LocalStartOfMonth(DateTimeOffset value) => UsagePeriod.StartOfMonth(value);

    private static DateTimeOffset LocalStartOfNextDay(DateTimeOffset value)
    {
        DateTime localStart = value.LocalDateTime.Date;
        return new DateTimeOffset(localStart.AddDays(1));
    }

    private static DateTimeOffset LocalStartOfNextMonth(DateTimeOffset value)
    {
        DateTime localStart = new(
            value.LocalDateTime.Year, value.LocalDateTime.Month, 1,
            0, 0, 0, DateTimeKind.Local);
        return new DateTimeOffset(localStart.AddMonths(1));
    }

    private static string FormatUsageTooltip(string label, ApiCallLog? entry)
        => entry is null
            ? $"{label}: 記録なし"
            : $"{label}: 1回（{UsageFormatting.FormatStatusCounts(entry.Status)}）  " +
              $"入力 {entry.PromptTokens:N0} / 出力 {entry.OutputTokens:N0} tokens  " +
              $"${UsageFormatting.FormatUsd(entry.UsdCost)} ({UsageFormatting.FormatJpy(entry)})" +
              UsageFormatting.FormatRateReference(entry.UsdJpyRate is decimal rate && entry.RateDate is DateOnly date
                  ? new FxRate(date, rate, default) : null) +
              $"  提案 {entry.SuggestionCount:N0} / 破棄 {entry.DiscardedCount:N0}";

    private static string FormatUsageTooltip(string label, ApiCallUsageSummary summary)
        => $"{label}: {summary.TotalCalls:N0}回（{UsageFormatting.FormatStatusCounts(summary)}）  " +
           $"入力 {summary.PromptTokens:N0} / 出力 {summary.OutputTokens:N0} tokens  " +
           $"${UsageFormatting.FormatUsd(summary.UsdCost)} ({UsageFormatting.FormatJpy(summary)})  " +
           UsageFormatting.FormatSummaryRateReference(summary) +
           $"  提案 {summary.SuggestionCount:N0} / 破棄 {summary.DiscardedCount:N0}";

    private string FormatCachedRateTooltip()
    {
        FxRate? rate = _fxRates.GetCachedRate();
        return rate is null
            ? "現在のキャッシュ: なし（JPY換算は次回取得後のログから表示）"
            : "現在のキャッシュ: " + UsageFormatting.FormatRateReference(rate).Trim();
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

        SuppressAutoHide();
        try
        {
            var dialog = new SettingsWindow(_settings, _credentials, _pricing, _styleGuides, _reactions) { Owner = this };
            dialog.ShowDialog();
        }
        finally
        {
            ReleaseAutoHide();
        }

        Activate();
        Editor.TextArea.Focus();
    }

    private void OpenCrossTabSearch(string initialTerm)
    {
        if (_crossTabSearch is null)
        {
            // 生成のたびに1回だけ確保する。既に開いている状態でこのメソッドが再度呼ばれても
            // （例: Ctrl+Shift+F の再押下）多重に加算しない。加算と解除を1:1に保つのが目的。
            SuppressAutoHide();
            try
            {
                _crossTabSearch = new CrossTabSearchWindow(_tabs, _repository) { Owner = this };
                _crossTabSearch.Closed += (_, _) =>
                {
                    _crossTabSearch = null;
                    ReleaseAutoHide();
                };
                _crossTabSearch.HitSelected += JumpToHit;
                _crossTabSearch.Show();
            }
            catch
            {
                // 生成・表示のどこで失敗しても抑止カウントを積んだままにしない。Show() より前の失敗では
                // Closed が発火しないため、ここで明示的に解放してから同じ例外を再スローする。
                _crossTabSearch = null;
                ReleaseAutoHide();
                throw;
            }
        }
        else
        {
            _crossTabSearch.Activate();
        }

        _crossTabSearch.SetTerm(initialTerm);
    }

    /// <summary>課金履歴画面を開く（要件 3.1.1 / 3.6.2）。トレイメニュー・ステータスバー・Ctrl+Shift+B から呼ぶ。</summary>
    public void OpenBillingHistory()
    {
        if (_billingHistory is null)
        {
            // 生成のたびに1回だけ確保する。既に開いている状態でこのメソッドが再度呼ばれても
            // （例: Ctrl+Shift+B の再押下）多重に加算しない。加算と解除を1:1に保つのが目的。
            SuppressAutoHide();
            try
            {
                // BillingHistoryWindow のコンストラクタは末尾で LoadHistory() を呼びSQLiteを読むため、
                // DBロック・破損・XAMLリソース解決失敗などで例外が飛びうる。生成・表示のどこで
                // 失敗しても抑止カウントを積んだままにしない（Show() より前の失敗では Closed が
                // 発火しないため、ここで明示的に解放してから同じ例外を再スローする）。
                _billingHistory = new BillingHistoryWindow(_apiCalls) { Owner = this };
                _billingHistory.Closed += (_, _) =>
                {
                    _billingHistory = null;
                    ReleaseAutoHide();
                };
                _billingHistory.Show();
            }
            catch
            {
                _billingHistory = null;
                ReleaseAutoHide();
                throw;
            }
        }
        else
        {
            _billingHistory.Activate();
            _billingHistory.Refresh();
        }
    }

    private void StatusUsage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenBillingHistory();

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

        SuppressAutoHide();
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
            ReleaseAutoHide();
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

    /// <summary>校正処理中の一時的な状態をステータスバーへ表示する。</summary>
    private void SetProofreadingStatus(string message, bool force = false)
        => SetTransientStatus(message);

    private string? GetActiveApiKey()
        => ProofreadingModelCatalog.IsOpenAi(_proofreadingClient.Model)
            ? _credentials.GetOpenAiApiKey(_settings.Current.OpenAiApiKeySource)
            : _credentials.GetApiKey(_settings.Current.GeminiApiKeySource);

    private string ActiveProviderName()
        => ProofreadingModelCatalog.IsOpenAi(_proofreadingClient.Model)
            ? "OpenAI"
            : "Gemini";

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
