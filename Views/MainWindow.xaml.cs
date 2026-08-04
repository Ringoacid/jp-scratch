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
using ICSharpCode.AvalonEdit.Document;
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
    private readonly ProofreadingInlineDiffGenerator _proofreadingInline = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _usageRolloverTimer;
    private readonly DispatcherTimer _proofreadingTimer;
    private readonly ProofreadingSchedule _proofreadingSchedule = new();

    /// <summary>
    /// 最後に <see cref="RefreshUsageDisplay"/> が読み取った当月累計USD。発火条件5（月間上限）の
    /// 判定・進捗バー・確認ダイアログの表示が、それぞれ別にDBを読みに行かずこれを共有する。
    /// </summary>
    private decimal _monthUsageUsd;

    /// <summary>
    /// 当月累計を一度でも正常に読み取れたか。起動時の集計読み取りが失敗すると
    /// <see cref="_monthUsageUsd"/> が 0 のままになり、このフラグが無いと月間上限が効かない
    /// （fail-open）まま自動校正が課金される。一度も読めていない間は自動送信を見送る（fail-close）。
    /// 手動校正はユーザーの明示的な操作なのでブロックしない。
    /// </summary>
    private bool _monthUsageKnown;

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
    /// 課金APIの実行確認モーダルを表示中か。
    ///
    /// WPF のモーダルは入れ子のメッセージループを回すので、ダイアログを読んでいる間も
    /// DispatcherTimer は発火し、ユーザー操作も届く。進行中フラグ（_proofreadingRunInProgress 等）を
    /// 立てるのは確認を通ったあとなので、その間は入口ガードが素通りし、同じ段落が二重に送信されて
    /// 二重課金になる（さらに並走した2実行が MarkSent と LoadCorrectedDocument を後勝ちで
    /// 上書きし合い、先行実行の課金済み提案が黙って消える）。
    ///
    /// そこで「フラグを立てる → try/finally」の不変条件をモーダルにも適用し、
    /// ダイアログを出す前にこのフラグを立てる。CLAUDE.md の「進行中フラグを true にする行と
    /// try/finally の間には何も置かない」は、**モーダル表示も『何か』に含む**と読むこと。
    /// </summary>
    private bool _paidApiDialogOpen;

    /// <summary>
    /// 「許可」による本文置換の最中だけ true。この間の TabTextChanged では自動校正の
    /// デバウンスを再始動しない（モデル自身の出力であり、ユーザーの新しい入力ではない）。
    /// </summary>
    private bool _applyingProposal;

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
        _tabs.TabRemoved += OnTabRemoved;
        _tabs.SaveFailed += OnTabSaveFailed;

        Editor.TextArea.TextView.LineTransformers.Add(_ideographicSpace);
        Editor.TextArea.TextView.ElementGenerators.Add(_proofreadingInline);
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

        // ウィンドウ非表示は保存タイミングのひとつ（要件 3.2.4）。
        // ここで例外を漏らすと Hide() へ到達せず「ウィンドウが隠れないまま予期しないエラー
        // ダイアログが出る」ことになるため、保存の失敗で非表示そのものを止めない。
        // タブ単位の I/O 失敗は SaveDirty 内で隔離され、通知も SaveDirty が発火させる。
        // 隠れた後ではステータスバーを読めないため、この間の失敗通知はトレイへ回す
        // （印を立てる行と try の間には何も置かない — CLAUDE.md の不変条件）。
        _savingForHide = true;
        try
        {
            _tabs.SaveDirty();
        }
        catch (Exception)
        {
            OnTabSaveFailed(new TabSaveFailure(
                _tabs.Tabs.Where(tab => tab.IsDirty).Select(tab => tab.Title).ToArray(),
                WillRetry: false,
                IsFirstFailure: true));
        }
        finally
        {
            _savingForHide = false;
        }

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
        _proofreadingInline.OriginalBrush = Brush("ProofreadingOriginalBrush");
        _proofreadingInline.StrikeBrush = Brush("ProofreadingStrikeBrush");
        _proofreadingInline.SuggestionBrush = Brush("ProofreadingSuggestionBrush");
        _proofreadingInline.SelectedBackgroundBrush = Brush("ProofreadingSelectionBackgroundBrush");
        _proofreadingInline.SelectedBorderBrush = Brush("ProofreadingSelectionBorderBrush");

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
        // 「許可」による置換はモデル自身の出力なので、新しい変更として数えない（作業A・Bの対）。
        if (!_applyingProposal)
            _proofreadingSchedule.NotifyChanged(tab.Id, DateTimeOffset.Now);
        if (ReferenceEquals(tab, _tabs.Active))
            ScheduleAutomaticProofreading();
    }

    /// <summary>
    /// 自動保存が 1 枚でも落としたときの通知（要件 3.2.4 は「保存」をユーザーに見せない設計なので、
    /// 失敗を黙ると編集が静かに消える）。自動保存は数百ミリ秒ごとに走るためモーダルは出さず、
    /// ステータスバーへ強制表示する。
    ///
    /// ウィンドウが隠れている（またはこれから隠れる）ときはステータスバーを読めないため、
    /// トレイのバルーンへ回す。非表示時の保存はまさにその経路で失敗しうるので、ここを
    /// ステータスバーだけにすると、編集が失われうる通知を誰も見ないまま常駐へ戻る。
    /// </summary>
    private void OnTabSaveFailed(TabSaveFailure failure)
    {
        if (failure.Titles.Count == 0) return;

        string what = failure.Titles.Count == 1
            ? $"「{failure.Titles[0]}」を保存できませんでした"
            : $"{failure.Titles.Count} 個のタブを保存できませんでした";

        SetProofreadingStatus(
            what + (failure.WillRetry
                ? "（自動で再試行します）"
                : "（自動再試行は打ち切りました。編集・タブ切替・終了時に再試行します）"),
            force: true);

        // 非表示処理の一部としての失敗は毎回知らせる（ユーザー操作 1 回につき最大 1 通）。
        // 隠れたままの自動再試行はバックオフのたびに失敗しうるので、連続失敗の 1 回目だけに絞る。
        bool notifyViaTray = _savingForHide || (!IsVisible && failure.IsFirstFailure);
        if (!notifyViaTray) return;

        _tray.ShowMessage(
            "保存できませんでした",
            TabManager.FormatTitles(failure.Titles) + Environment.NewLine +
            "本文ファイルには前回保存できた内容が残っています。" +
            "同期ソフトやウイルス対策がファイルを掴んでいないか確認してください。",
            isWarning: true);
    }

    /// <summary>
    /// 非表示処理の一部として保存しているか。<see cref="Hide"/> の直前は <c>IsVisible</c> がまだ
    /// true なので、この印が無いと「隠れる直前の保存失敗」をステータスバーへ出して見逃させる。
    /// </summary>
    private bool _savingForHide;

    private void OnTabRemoved(ScratchTab tab)
    {
        // 閉じたタブのデバウンス状態を残さない。長時間の常駐で辞書が無制限に肥大するのを防ぐ。
        _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
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

        // 描画層は失効した提案に触れないよう、必ず不変のスナップショットを経由させる。
        // ProofreadingProposal.Start / Length は失効後に例外を投げるため、
        // 描画の途中で読むと VisualLine の構築ごと落ちる。
        // diffs は proposals と1対1・同じ順序であることが前提。片方にだけ
        // filter や並べ替えを足すと、選択中と別の提案がハイライトされる。
        ProofreadingInlineDiff[] diffs = proposals
            .Select(proposal => new ProofreadingInlineDiff(
                proposal.Start,
                proposal.Length,
                proposal.Original,
                proposal.Suggestion))
            .ToArray();
        _proofreadingInline.Diffs = diffs;

        if (_selectedProposal is null)
        {
            _proofreadingInline.Selected = null;
            ProofreadingPanel.Visibility = Visibility.Collapsed;
            ProposalPositionText.Text = "";
            ProposalChangeText.Text = "";
        }
        else
        {
            int index = IndexOfProposal(proposals, _selectedProposal);
            _proofreadingInline.Selected = index >= 0 ? diffs[index] : null;
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
        if ((sender as FrameworkElement)?.DataContext is ScratchTab tab) TryCloseTab(tab);
        e.Handled = true;
    }

    /// <summary>
    /// タブを閉じる（ゴミ箱へ）。本文ファイルの移動に失敗したら、タブは閉じずにステータスバーで
    /// 理由を伝える。失敗を握ると「UI から消えたのに本文ファイルは残る」乖離が起き、後で本文が
    /// 失われたように見えるため、必ずユーザーへ伝える（要件 3.2.4: 本文はサルベージ可能）。
    /// </summary>
    private void TryCloseTab(ScratchTab tab)
    {
        try
        {
            _tabs.Close(tab);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetTransientStatus("本文ファイルをゴミ箱へ移動できなかったため、タブを閉じませんでした");
        }
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
        if ((sender as FrameworkElement)?.DataContext is ScratchTab tab) TryCloseTab(tab);
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
        Bind(Key.W, ModifierKeys.Control, () => { if (_tabs.Active is { } t) TryCloseTab(t); });
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
        Bind(Key.M, ModifierKeys.Control | ModifierKeys.Shift, ReportMissedCorrection);
        Bind(Key.F8, ModifierKeys.None, () => SelectRelativeProposal(1));
        Bind(Key.F8, ModifierKeys.Shift, () => SelectRelativeProposal(-1));
        Bind(Key.OemPeriod, ModifierKeys.Control, AcceptSelectedProposal);
        Bind(Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift, AcceptAllProposals);
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
        // インライン差分は範囲全体が1つの表示要素になるため、クリック位置によっては
        // 範囲の終端オフセットが返る。1文字手前でも探して取りこぼさない。
        ProofreadingProposal? proposal = _activeProofreading.FindAtOffset(offset)
            ?? (offset > 0 ? _activeProofreading.FindAtOffset(offset - 1) : null);
        if (proposal is null)
            return;

        SelectProposal(proposal, scrollIntoView: false);
        Editor.TextArea.Focus();
        e.Handled = true;
    }

    /// <summary>
    /// 右クリックメニュー（proofreading-ux-fixes-plan.md §10）の表示直前処理。
    /// - 右クリック位置が既存選択範囲内なら選択を維持する。選択外なら通常のエディタ動作に
    ///   合わせてキャレットを移動する。
    /// - 大文字・小文字変換は選択がないとき無効化する（切り取り・コピー・貼り付け・削除・
    ///   すべて選択は標準コマンドのため、選択・クリップボード状態で自動的に切り替わる）。
    /// </summary>
    private void Editor_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Editor.ContextMenu is not { } menu)
            return;

        // ContextMenuEventArgs にはカーソル位置が無いため、マウス位置を直接取得する。
        var position = Editor.GetPositionFromPoint(Mouse.GetPosition(Editor));
        if (position is not null)
        {
            int offset = Editor.Document.GetOffset(position.Value.Location);
            int selectionStart = Editor.SelectionStart;
            int selectionEnd = selectionStart + Editor.SelectionLength;
            if (Editor.SelectionLength == 0 ||
                offset < selectionStart ||
                offset >= selectionEnd)
            {
                Editor.Select(offset, 0);
                Editor.CaretOffset = offset;
            }
        }

        bool hasSelection = Editor.SelectionLength > 0;
        foreach (object item in menu.Items)
        {
            if (item is not MenuItem menuItem)
                continue;
            if (menuItem.Tag is "upper" or "lower")
                menuItem.IsEnabled = hasSelection;
        }
    }

    private void Editor_UppercaseMenuItem_Click(object sender, RoutedEventArgs e)
        => TransformSelectionCase(upper: true);

    private void Editor_LowercaseMenuItem_Click(object sender, RoutedEventArgs e)
        => TransformSelectionCase(upper: false);

    /// <summary>
    /// 選択範囲の英字だけを大文字/小文字へ変換する（proofreading-ux-fixes-plan.md §10.2）。
    /// 日本語や記号は保持し、invariant な1文字単位の変換で文字数変化を起こさない
    /// （ß の大文字化のような文化依存の拡張をしない）。変換は1回の Undo で戻せる。
    /// </summary>
    private void TransformSelectionCase(bool upper)
    {
        if (Editor.SelectionLength == 0)
            return;

        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        string selected = Editor.SelectedText;

        var builder = new System.Text.StringBuilder(selected.Length);
        foreach (char character in selected)
        {
            builder.Append(upper
                ? char.IsLower(character) ? char.ToUpperInvariant(character) : character
                : char.IsUpper(character) ? char.ToLowerInvariant(character) : character);
        }

        string transformed = builder.ToString();
        if (string.Equals(transformed, selected, StringComparison.Ordinal))
            return;

        Editor.Document.Replace(start, length, transformed);
        Editor.Select(start, transformed.Length);
        Editor.TextArea.Focus();
    }

    private void MissedCorrectionMenuItem_Click(object sender, RoutedEventArgs e)
        => ReportMissedCorrection();

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

    private void AcceptAllProposalsButton_Click(object sender, RoutedEventArgs e)
        => AcceptAllProposals();

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

        bool recorded = TryAddReaction(proposal, ProofreadingReaction.RejectWithReason, reason);
        _activeProofreading?.Reject(proposal);
        SetTransientStatus(recorded ? "理由を記録して拒否しました" : "拒否しました（理由の記録に失敗）");
        Editor.TextArea.Focus();
        MaybeOfferStyleGuideGeneration();
    }

    /// <summary>
    /// 校正漏れ報告（proofreading-ux-fixes-plan.md §9）。選択範囲の置換・挿入・削除を
    /// 学習データ（reactions）へ記録し、その後に本文を変更する。この操作自体は API を呼ばない。
    /// 保存に失敗した場合は本文も変更しない（記録だけ失敗して本文だけ変わる状態を作らない）。
    /// 本文の変更は1回の Undo で戻せる。Undo しても記録は残る。
    /// </summary>
    private void ReportMissedCorrection()
    {
        if (_tabs.Active is not { } tab)
            return;

        bool hasSelection = Editor.SelectionLength > 0;
        int selectionStart = Editor.SelectionStart;
        int selectionLength = Editor.SelectionLength;
        string original = hasSelection ? Editor.SelectedText : "";

        // 左文脈・右文脈（§9.4）。空・空白のみなら null として保存する。
        string leftContext = tab.Document.GetText(0, selectionStart);
        string rightContext = tab.Document.GetText(
            selectionStart + selectionLength,
            tab.Document.TextLength - selectionStart - selectionLength);

        SuppressAutoHide();
        try
        {
            // プレビュー用に前後の文脈（改行を含む原文）も渡す。
            var dialog = new MissedCorrectionDialog(
                original,
                hasSelection,
                leftContext,
                rightContext)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true)
                return;

            string corrected = dialog.Corrected;
            MissedCorrectionAction.Determine(original, corrected, out bool allowed);
            if (!allowed)
                return;

            // 記録（学習データ）を先に保存する。失敗したら本文は変更しない。
            try
            {
                _reactions.AddMissedCorrection(
                    tab.Id,
                    original,
                    corrected,
                    NormalizeContext(leftContext),
                    NormalizeContext(rightContext),
                    dialog.Reason);
            }
            catch (Exception)
            {
                MessageBox.Show(
                    this,
                    "校正漏れの記録に失敗したため、本文は変更しませんでした。",
                    "JP Scratch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 本文を一回の Undo で戻せるよう、1回の変更にまとめる。
            if (hasSelection)
                tab.Document.Replace(selectionStart, selectionLength, corrected);
            else
                tab.Document.Insert(selectionStart, corrected);

            SetTransientStatus("校正漏れを記録しました");
            Editor.TextArea.Focus();
            MaybeOfferStyleGuideGeneration();
        }
        finally
        {
            ReleaseAutoHide();
        }
    }

    private static string? NormalizeContext(string context)
        => string.IsNullOrWhiteSpace(context) ? null : context.Trim();

    private async void AlternativeWithReasonMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_alternativeInProgress ||
            _proofreadingRunInProgress ||
            _styleGuideGenerationInProgress ||
            _paidApiDialogOpen ||
            _selectedProposal is not { IsActive: true } proposal)
        {
            return;
        }

        SuppressAutoHide();

        // 理由入力・APIキー未設定の案内・課金確認は、いずれもモーダル＝入れ子のメッセージループを
        // 回す。**最初のモーダルより前に**再入ガードを立てるのが要点で、理由を書いている数十秒の
        // 間に自動校正が走り出すと、その LoadCorrectedDocument が提案一覧を作り直してしまう。
        // すると課金して得た別案の差し替え先（元の提案）が既に失効しており、成果物だけが失われる。
        // 早期 return を try の中に置かないのは、finally でフラグを戻したあとに
        // ReleaseAutoHide と再スケジュールをまとめて行うため。
        string reason = "";
        bool confirmed = false;
        _paidApiDialogOpen = true;
        try
        {
            // フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            _proofreadingTimer.Stop();

            if (TryGetReason(generatesAlternative: true, out reason))
            {
                // 別案生成は自動側のモデルを使う（要件3.5.1）。ピン留めより前に呼ぶので用途を明示する。
                if (string.IsNullOrWhiteSpace(GetActiveApiKey(ProofreadingPurpose.Automatic)))
                {
                    MessageBox.Show(
                        this,
                        $"{ActiveProviderName(ProofreadingPurpose.Automatic)} APIキーが設定されていません。設定画面で登録してください。",
                        "JP Scratch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else if (!_settings.Current.ConfirmPaidApiCalls)
                {
                    confirmed = true;
                }
                else
                {
                    MessageBoxResult confirmation = MessageBox.Show(
                        this,
                        $"別案生成のため{ActiveProviderName(ProofreadingPurpose.Automatic)} APIを1回呼び出します。料金が発生します。\n\n" +
                        BuildPricingSummary(ProofreadingPurpose.Automatic) + "\n" +
                        "実行後に使用トークン数と料金を表示します。\n\n実行しますか？",
                        "API料金の確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    confirmed = confirmation == MessageBoxResult.Yes;
                }
            }
        }
        finally
        {
            _paidApiDialogOpen = false;
        }

        if (!confirmed)
        {
            ReleaseAutoHide();
            ScheduleAutomaticProofreading();
            return;
        }

        _alternativeInProgress = true;
        ApiUsageCost? failedApiCost = null;
        try
        {
            // 進行中フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            // ここで例外が出ても finally が必ず _alternativeInProgress を戻す。
            SetProposalActionsEnabled(false);
            UpdateTrayIconState();
            PinModelForRun(ProofreadingPurpose.Automatic);   // 別案生成は自動側（要件3.5.1）
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
                ApiUsageCost? discardedCost = TryRecordAlternativeCall(result, 0, 1);
                ShowAlternativeCost(result, discardedCost,
                    "生成中に本文が変更されたため、別案は適用しませんでした。");
                return;
            }

            // 課金済みの成果物（別案の差し替え）を最優先で適用する。リアクション記録・課金ログは
            // 補助情報なので、記録に失敗しても適用を妨げない。従来は _reactions.Add を先に呼んで
            // いたため、ここで例外（DB失敗・単価未登録など）が飛ぶと、課金だけ発生して別案が
            // 失われていた。順序を「適用 → 補助記録」へ入れ替え、各記録をガードする。
            ProofreadingProposal? replacement = _activeProofreading?.TryReplaceSuggestion(
                proposal,
                result.Alternative);
            bool applied = replacement is not null;
            if (replacement is not null)
            {
                // 差し替えで元の提案は失効するため、選択し直さないと
                // RefreshProofreadingPresentation が選択を先頭の提案へ飛ばす。
                // 課金して得た別案が未選択のままだと、続けて Ctrl+. を押したときに
                // 別の提案が適用されてしまう。スクロールは既に見えている位置なので不要。
                SelectProposal(replacement, scrollIntoView: false);
            }
            // 理由つき拒否はユーザーの判断＝v3の学習データなので、差し替えの成否とは独立に
            // 記録する。差し替えに失敗しても「この提案をこの理由で拒否した」事実は有効な
            // 学習素材であり、無条件に記録していた従来の挙動へ戻す。
            bool recorded = TryAddReaction(proposal, ProofreadingReaction.RejectWithReason, reason);

            ApiUsageCost? cost = TryRecordAlternativeCall(result, applied ? 1 : 0, applied ? 0 : 1);
            ShowAlternativeCost(
                result,
                cost,
                applied
                    ? "別案を表示しました。"
                    : "有効な別案へ差し替えられませんでした。" +
                      (recorded ? "" : "\n（リアクションの記録には失敗しました）"));
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
        catch (Exception ex)
        {
            // 予期しない内部エラー。async void ハンドラから未処理で漏らすと「予期しないエラー」
            // ダイアログになるだけで理由が分からないため、ここで明示的に伝える。
            MessageBox.Show(
                this,
                "別案生成で予期しないエラーが発生しました。\n\n" + ex.Message,
                "別案を生成できませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetProofreadingStatus("別案生成でエラーが発生しました", force: true);
        }
        finally
        {
            _alternativeInProgress = false;
            UnpinModelAfterRun();
            SetProposalActionsEnabled(true);
            UpdateTrayIconState();
            // 別案生成の間は ScheduleAutomaticProofreading が抑止されている（ビジーループ回避）。
            // 抑止を解いた今、止めたままのタイマーを必ず張り直す。
            ScheduleAutomaticProofreading();
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
        // 校正実行中（_proofreadingRunInProgress）はキーバインド（Ctrl+.）がボタンの無効化を
        // 素通りするため、一括許可（AcceptAllProposals）と同じガードを入口に置く。
        // 実行中に本文を変えると MarkSent が送信済みハッシュをスナップショット時点のもので
        // 置き換え、引き継ぎが消えてその段落が次回再送・再課金される。
        if (_proofreadingRunInProgress ||
            _selectedProposal is not { IsActive: true } proposal ||
            !CanReactTo(proposal))
        {
            return;
        }

        if (_tabs.Active is not { } tab)
            return;

        // TryApply 後は proposal.Start が失効して例外を投げるため、先に控える。
        int appliedOffset = proposal.Start;
        string beforeText = tab.Document.Text;

        bool recorded = TryAddReaction(proposal, ProofreadingReaction.Accept);
        if (TryApplyWithCarryForward(tab, proposal, appliedOffset, beforeText))
            SetTransientStatus(
                recorded ? "修正を許可しました" : "修正を許可しました（リアクション記録は失敗）");
        Editor.TextArea.Focus();
        MaybeOfferStyleGuideGeneration();
    }

    /// <summary>
    /// 提案を適用し、送信済みハッシュを引き継ぐ。適用中は自動校正のデバウンスを止める。
    /// 作業A（送信済みへの引き継ぎ）と作業B（変更通知の抑止）の対。片方だけでは
    /// 「別の場所を1文字打った瞬間に同じ段落が再送・再課金される」バグが残る。
    /// 注意: このヘルパーは finally で無条件に _applyingProposal を false へ戻すため、
    /// 既に _applyingProposal スコープ内（一括許可 AcceptAllProposals のループ）から呼んではいけない。
    /// 一括許可側は TryApply + CarryForwardAppliedEdit をインライン展開している（DRY に直す際も
    /// この制約を守ること）。
    /// </summary>
    private bool TryApplyWithCarryForward(
        ScratchTab tab,
        ProofreadingProposal proposal,
        int appliedOffset,
        string beforeText)
    {
        _applyingProposal = true;
        try
        {
            if (_activeProofreading?.TryApply(proposal) != true)
                return false;
            tab.ProofreadingPlanner.CarryForwardAppliedEdit(
                beforeText,
                tab.Document.Text,
                appliedOffset);
            return true;
        }
        finally
        {
            _applyingProposal = false;
        }
    }

    /// <summary>
    /// 現在アクティブな提案をすべて許可する。1件ずつ許可すると本文変更が N 回起きるため、
    /// 1回の更新にまとめる。リアクションは学習データなので提案ごとに個別に記録する。
    /// </summary>
    private void AcceptAllProposals()
    {
        // 校正実行中（_proofreadingRunInProgress）はキーバインドがボタン無効化を素通りするため、
        // 入口で同じガードを置く。実行中に本文を変えると MarkSent(plan, index) が _lastSentHashes を
        // スナップショット時点の段落ハッシュで丸ごと置き換えるため、適用した引き継ぎが全て消え、
        // N段落が次回再送・再課金される（単体許可の fail open「余分に1回」を N 倍にしないため）。
        if (_proofreadingRunInProgress ||
            _activeProofreading is not { } session || _tabs.Active is not { } tab)
            return;

        // 適用中に session.Proposals が変化する（TryApply が Remove する）ため、先にコピーする。
        ProofreadingProposal[] targets = session.Proposals
            .Where(proposal => proposal.IsActive)
            .OrderBy(proposal => proposal.Start)
            .ToArray();
        if (targets.Length == 0)
            return;

        int applied = 0;
        int recordFailures = 0;
        _applyingProposal = true;
        try
        {
            // 1回の Ctrl+Z でまとめて戻せるように Undo をグループ化する。
            // using は必ず try の内側に置く（EndUpdate でまとめて発火する TextChanged が
            // フラグ解除後に届くと、一括許可の全変更がユーザーの入力として数えられる）。
            using (tab.Document.RunUpdate())
            {
                foreach (ProofreadingProposal proposal in targets)
                {
                    // 直前の適用で失効している場合があるため毎回確認する。
                    if (!proposal.IsActive || !CanReactTo(proposal))
                        continue;

                    int appliedOffset = proposal.Start;
                    string beforeText = tab.Document.Text;
                    // 記録は適用より先。既存の単体許可と同じ順序で、
                    // リアクション（学習データ）の記録は適用の成否と独立に行う。直さないこと。
                    if (!TryAddReaction(proposal, ProofreadingReaction.Accept))
                        recordFailures++;
                    if (session.TryApply(proposal) != true)
                        continue;

                    // 適用のたびに呼ぶ（まとめて1回では「適用段落以外のハッシュ一致」が成立しない）。
                    tab.ProofreadingPlanner.CarryForwardAppliedEdit(
                        beforeText,
                        tab.Document.Text,
                        appliedOffset);
                    applied++;
                }
            }
        }
        finally
        {
            _applyingProposal = false;
        }

        SetTransientStatus(recordFailures == 0
            ? $"{applied}件の修正を許可しました"
            : $"{applied}件の修正を許可しました（リアクション記録は{recordFailures}件失敗）");
        Editor.TextArea.Focus();
        // ループ内で N 回呼ばず、最後に1回だけ判定する。
        MaybeOfferStyleGuideGeneration();
    }

    private void RejectSelectedProposal()
    {
        if (_selectedProposal is not { IsActive: true } proposal ||
            !CanReactTo(proposal))
        {
            return;
        }

        bool recorded = TryAddReaction(proposal, ProofreadingReaction.Reject);
        _activeProofreading?.Reject(proposal);
        SetTransientStatus(recorded ? "修正を拒否しました" : "修正を拒否しました（リアクション記録は失敗）");
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
                generatesAlternative,
                ActiveProviderName())
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
        AcceptAllProposalsButton.IsEnabled = enabled;
        AcceptProposalButton.IsEnabled = enabled;
        RejectProposalButton.IsEnabled = enabled;
        ReasonProposalButton.IsEnabled = enabled;
    }

    private void ShowAlternativeCost(
        GeminiAlternativeResult result,
        ApiUsageCost? cost,
        string message)
    {
        string costText = cost is null ? "確認できませんでした" : FormatCostWithJpy(cost);
        string summary =
            $"{message}\n\n入力 {result.Usage.PromptTokens:N0}、" +
            $"出力・推論 {result.Usage.BillableOutputTokens:N0} tokens\n" +
            $"料金 {costText}";

        MessageBox.Show(
            this,
            summary,
            "別案生成の使用量",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        SetProofreadingStatus(
            $"別案 ↑{result.Usage.PromptTokens:N0} " +
            $"↓{result.Usage.BillableOutputTokens:N0} tok  " +
            (cost is null ? "料金未確認" : FormatCostWithJpy(cost)),
            force: true);
    }

    /// <summary>
    /// 別案生成の課金ログを記録する。単価未登録・DB失敗などの記録失敗は握りつぶして
    /// <c>null</c> を返す（料金は「確認できません」として表示する）。課金済みの別案そのものは
    /// 呼び出し側で既に適用済みのため、ここが失敗しても結果は失われない。
    /// </summary>
    private ApiUsageCost? TryRecordAlternativeCall(
        GeminiAlternativeResult result,
        int suggestionCount,
        int discardedCount)
    {
        try
        {
            return RecordSuccessfulApiCall(
                ApiCallTrigger.Realternative,
                result.Usage,
                result.Elapsed,
                suggestionCount,
                discardedCount).Cost;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// リアクション（許可・拒否・理由つき拒否）の学習データを記録する。記録失敗（DBエラー等）は
    /// 提案操作の適用や料金表示を妨げない（補助情報扱い）が、黙って消すとリアクション総数が
    /// 伸びずスタイルガイド自動生成のしきい値が静かに遠のくため、成否を bool で返して呼び出し側が
    /// ステータス表示へ反映する。
    /// </summary>
    private bool TryAddReaction(
        ProofreadingProposal proposal,
        ProofreadingReaction reaction,
        string? reason = null)
    {
        try
        {
            _reactions.Add(_tabs.Active?.Id, proposal, reaction, reason);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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
        // 他の課金処理・確認ダイアログの最中はタイマーを止めたままにする。ここで再開すると、
        // 「発火 → RunProofreadingAsync の入口ガードで却下 → 再スケジュール」を100ms間隔で
        // 繰り返すビジーループになる（月間上限のガードと同じ理由）。
        // 止めたぶんは、各処理の finally が完了時にこのメソッドを呼び直して必ず戻す。
        if (!_settings.Current.AutoProofreadingEnabled ||
            _proofreadingRunInProgress ||
            _alternativeInProgress ||
            _styleGuideGenerationInProgress ||
            _paidApiDialogOpen ||
            _tabs.Active is not { } tab)
        {
            return;
        }

        // 発火条件5（月間上限）。ここでタイマーの再開始そのものを止めないと、
        // NotifyChanged 済みの変更が残ったまま「タイマー発火→ガードで却下→
        // ScheduleAutomaticProofreadingを再度呼ぶ」を100ms間隔で繰り返すビジーループになる。
        if (IsMonthlyLimitReached())
            return;

        // 当月累計が一度も読めていない（起動時の集計読み取りが失敗した）間は、自動送信を
        // fail-close で見送る。0 のまま走らせると月間上限が効かずに課金され続けるため。
        // 次の RefreshUsageDisplay が成功した時点で _monthUsageKnown が立ち、再開される。
        if (!_monthUsageKnown)
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
    /// 1回の実行で使う用途とモデルを固定する。_proofreadingClient は常に
    /// <see cref="ProofreadingClientRouter"/> だがフィールド型はインターフェースのため、
    /// ルーター固有のピン留めを as で取り出して呼ぶ。
    ///
    /// 用途の割り当ては要件 3.5.1 のとおり。校正本体は手動/自動に従い、理由つき別案生成は
    /// 高速な自動側、スタイルガイド自動生成は品質を優先して手動側を使う。
    /// </summary>
    private void PinModelForRun(ProofreadingPurpose purpose)
        => (_proofreadingClient as ProofreadingClientRouter)?.PinModel(purpose);

    private void UnpinModelAfterRun()
        => (_proofreadingClient as ProofreadingClientRouter)?.UnpinModel();

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
            // 課金確認モーダルの表示中は判定自体を先送りする（モーダルはメッセージループを
            // 回すので、ここへ再入すると2つ目の課金ダイアログが重なって出る）。
            _paidApiDialogOpen ||
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

        _ = RunStyleGuideGenerationThenRescheduleAsync(total);
    }

    /// <summary>
    /// スタイルガイド生成のあと、自動校正タイマーを必ず張り直す。
    ///
    /// 生成の経路には「確認を辞退」「APIキー未設定」「素材の読み取り失敗」「使える例が無い」と
    /// 早期 return が多く、そのすべてが確認モーダルの前で止めたタイマーを止めたままにする。
    /// <see cref="RunStyleGuideGenerationAsync"/> の中の finally は
    /// _styleGuideGenerationInProgress を戻す役目に専念させ、再スケジュールはここで一箇所に集める
    /// （抑止フラグが下りきったあとに呼ぶ必要があるため、外側でなければならない）。
    /// </summary>
    private async Task RunStyleGuideGenerationThenRescheduleAsync(long totalReactionsAtCheck)
    {
        try
        {
            await RunStyleGuideGenerationAsync(totalReactionsAtCheck);
        }
        finally
        {
            ScheduleAutomaticProofreading();
        }
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
        // 確認モーダルの前にタイマーを止め、再入ガードのフラグを立てる（RunProofreadingAsync と同じ理由）。
        bool confirmed;
        _paidApiDialogOpen = true;
        try
        {
            // フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            _proofreadingTimer.Stop();
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                $"リアクションが{_settings.Current.StyleGuideGenerationThreshold}件以上たまりました。" +
                $"{ActiveProviderName(ProofreadingPurpose.Manual)} APIを1回呼び出して、あなたの文体ルール（スタイルガイド）を生成しますか？\n\n" +
                BuildPricingSummary(ProofreadingPurpose.Manual) + "\n" +
                "実行後に使用トークン数と料金を表示します。生成後は設定画面でいつでも閲覧・編集・削除できます。",
                "スタイルガイドの自動生成",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            confirmed = confirmation == MessageBoxResult.Yes;
        }
        finally
        {
            _paidApiDialogOpen = false;
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
                $"{ActiveProviderName(ProofreadingPurpose.Manual)} APIキーが設定されていません。設定画面で登録してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<FewShotCandidate> candidates;
        try
        {
            candidates = _reactions.GetFewShotCandidates(StyleGuideSourceSelector.MaxReactions);
        }
        catch (Exception)
        {
            // ここで失敗するとカーソルは既に進んでいる（次回の確認は次のしきい値まで来ない）ため、
            // 黙って消さず、生成を再実行できる状態であることを伝える。
            SetProofreadingStatus("スタイルガイドの素材を読み込めませんでした", force: true);
            return;
        }

        StyleGuideSourceSelection source = StyleGuideSourceSelector.Select(candidates);
        if (source.Examples.Count == 0)
        {
            SetProofreadingStatus("スタイルガイド生成に使えるリアクションがありません", force: true);
            return;
        }

        // 生成中（数秒）にフォーカスが外れて自動非表示になると、完了時のメッセージが見えないため、
        // API 呼び出しの間は自動非表示を抑止する。フラグ設定より前に出しておく（フラグと無関係で
        // あり、これより後で例外が出ても finally の ReleaseAutoHide と必ず対になる）。
        SuppressAutoHide();
        _styleGuideGenerationInProgress = true;
        try
        {
            // 進行中フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            // ここで例外が出ても finally が必ず _styleGuideGenerationInProgress を戻す。
            UpdateTrayIconState();
            PinModelForRun(ProofreadingPurpose.Manual);      // スタイルガイド生成は手動側（要件3.5.1）
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
                try
                {
                    RecordFailedApiCall(ApiCallTrigger.StyleGuide, ex, stopwatch.Elapsed);
                }
                catch (Exception)
                {
                    // 課金ログの記録失敗は補助情報。API エラー自体の通知（下のダイアログ）は止めない。
                }
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
            // 課金ログの記録失敗（単価未登録・DBエラー等）は補助情報。生成結果そのものの保存を
            // 妨げないようガードし、料金は「確認できません」として表示する。
            ApiUsageCost? recordedCost;
            try
            {
                recordedCost = RecordSuccessfulApiCall(
                    ApiCallTrigger.StyleGuide,
                    result.Usage,
                    result.Elapsed,
                    suggestionCount: 1,
                    discardedCount: 0).Cost;
            }
            catch (Exception)
            {
                recordedCost = null;
            }
            string costText = recordedCost is null
                ? "料金は確認できませんでした"
                : FormatCostWithJpy(recordedCost);

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
                    $"料金 {costText}",
                    "スタイルガイドの生成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetProofreadingStatus("スタイルガイドを生成しました " + costText, force: true);
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
                    "スタイルガイドの保存に失敗しました " + costText, force: true);
            }
        }
        catch (Exception ex)
        {
            // 予期しない内部エラー。fire-and-forget（_ = ...）から未観測タスク例外として消えると
            // ユーザーに何も伝わらないため、ここで拾って伝える（RunProofreadingAsync・別案生成と
            // 同じ経路に揃える）。
            MessageBox.Show(
                this,
                "スタイルガイド生成で予期しないエラーが発生しました。\n\n" + ex.Message,
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetProofreadingStatus("スタイルガイド生成でエラーが発生しました", force: true);
        }
        finally
        {
            ReleaseAutoHide();
            _styleGuideGenerationInProgress = false;
            UnpinModelAfterRun();
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

    /// <summary>
    /// 他の課金処理と競合して自動校正が弾かれたとき、短い間隔で再挑戦させる。
    ///
    /// ここで <see cref="ScheduleAutomaticProofreading"/> を呼ぶことはできない（同じ進行中フラグで
    /// 抑止され、タイマーを止めたまま戻ってくる＝次の打鍵まで自動校正が止まる）。pending は
    /// 消費していないので、IME 変換中と同じく短い間隔でタイマーを張り直して待つ。
    /// 競合が解けたあとの発火では通常の判定へ進み、送るものが無ければ自然に止まる。
    /// </summary>
    private void RetryAutomaticProofreadingLater()
    {
        // 確認モーダルの表示中はタイマーを動かさない（モーダルはメッセージループを回すので
        // 発火してしまう）。ダイアログを出した側が閉じたあとに必ず張り直す。
        if (!_settings.Current.AutoProofreadingEnabled || _paidApiDialogOpen)
            return;

        _proofreadingTimer.Interval = TimeSpan.FromSeconds(1);
        _proofreadingTimer.Start();
    }

    private async Task RunProofreadingAsync(bool manual)
    {
        if (_proofreadingRunInProgress ||
            _alternativeInProgress ||
            _styleGuideGenerationInProgress ||
            _paidApiDialogOpen ||
            _tabs.Active is not { } tab)
        {
            // ここで弾かれた場合、pending（未校正の変更）はまだ消費していない。以前はこの経路だけが
            // 再スケジュールしておらず（直下の発火条件ガードは呼んでいる）、別案生成や
            // スタイルガイド生成と競合すると次の打鍵まで自動校正が止まっていた。
            // _proofreadingRunInProgress の場合は自分の finally が確実に呼び直すので何もしない。
            if (!manual && !_proofreadingRunInProgress)
                RetryAutomaticProofreadingLater();
            return;
        }

        if (!manual &&
            (!_settings.Current.AutoProofreadingEnabled ||
             !_proofreadingSchedule.IsAutomaticDue(tab.Id, DateTimeOffset.Now) ||
             // 当月累計が一度も読めていない間は自動送信を見送る（fail-close。ScheduleAutomaticProofreading と同じ判定）。
             !_monthUsageKnown ||
             IsMonthlyLimitReached()))
        {
            // 発火条件5（月間上限）を含め、ここで弾かれた場合もScheduleAutomaticProofreadingを
            // 呼ぶが、そちら側も同じ判定を持つため、上限到達中はタイマーが再始動しない。
            ScheduleAutomaticProofreading();
            return;
        }

        if (NativeMethods.HasImeComposition(_handle))
        {
            SetProofreadingStatus("IME変換の確定後に校正します");
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
            SetProofreadingStatus("校正が必要な変更はありません");
            return;
        }

        // 実行開始（ピン留め）より前なので、用途を明示してモデルを解決する。
        ProofreadingPurpose runPurpose =
            manual ? ProofreadingPurpose.Manual : ProofreadingPurpose.Automatic;
        string? apiKey = GetActiveApiKey(runPurpose);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (!manual)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            SetProofreadingStatus(
                $"{ActiveProviderName(runPurpose)} APIキーを設定してください",
                force: true);
            return;
        }

        // 確認モーダルの前にタイマーを止め、再入ガードのフラグを立てる。モーダル表示中も
        // メッセージループは回るため、これが無いとダイアログを読んでいる数秒の間に
        // タイマーが発火して2つ目の実行が始まり、同じ段落を二重に送信・二重課金する。
        bool confirmed;
        _paidApiDialogOpen = true;
        try
        {
            // フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            _proofreadingTimer.Stop();
            confirmed = ConfirmProofreadingApiUse(plan.Requests.Count, manual);
        }
        finally
        {
            _paidApiDialogOpen = false;
        }

        if (!confirmed)
        {
            if (!manual)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            SetProofreadingStatus("校正をキャンセルしました");
            // 止めたタイマーを戻す（キャンセルで pending は消費済みなので、送るものが無ければ
            // ここでは何も起きない。手動キャンセル時に未送信が残っていれば再スケジュールされる）。
            ScheduleAutomaticProofreading();
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
        List<(ProofreadingRequest Request, GeminiProofreadingResult Result, long ApiCallId, TextAnchor? Anchor)> completed = [];
        List<long> successfulApiCallIds = [];
        List<ApiUsageCost> responseCosts = [];
        ApiCallTrigger trigger = manual ? ApiCallTrigger.Manual : ApiCallTrigger.Auto;
        bool resultsApplied = false;

        // 部分結果保持（proofreading-ux-fixes-plan.md §7.2）用の状態。
        // 各リクエストの対象範囲（パート）の先頭へ TextAnchor を張り、本文編集による位置の
        // ずれをアンカーに追従させる。送信直前と適用時に「アンカー位置の原文が今も
        // request.SourceText と一致するか」を見ることで、**リクエスト対象の内部が編集された場合だけ**
        // その結果を破棄する（段落全体のハッシュ照合だと、2,000文字超で複数リクエストに分割された
        // 段落の後半を編集しただけで、無関係な前半の課金済み結果まで破棄されてしまう）。
        TextAnchor?[]? requestAnchors = null;
        // 未送信（＝再校正対象）に残すパートの集合。本文編集で破棄・中止したパートだけを入れる。
        // 段落 index だけで持つと、2,000 文字超で複数リクエストへ分割された段落の後半が失敗した
        // だけで、課金済みの前半まで未送信へ戻り、次回の校正で再送＝二重課金になる。
        HashSet<(int ParagraphIndex, int PartIndex)> unsentParts = [];
        // 「編集された部分は、入力が止まってから再校正します」を一度だけ出すためのフラグ。
        bool editDiscardHappened = false;
        // 校正中に別タブへ切り替わったか（結果は元タブのセッションへ安全に保持できる）。
        bool tabSwitchedDuringRun = false;

        try
        {
            // 進行中フラグを立てた行と try の間には何も置かない（CLAUDE.md の不変条件）。
            // ここで例外が出ても finally が必ず _proofreadingRunInProgress を戻す。
            _proofreadingTimer.Stop();
            // 実行中に設定画面でモデルを切り替えても、同一実行の途中でプロバイダ・単価が揺れないように
            // 実行開始時のモデルへ固定する（finally で解除）。
            PinModelForRun(manual ? ProofreadingPurpose.Manual : ProofreadingPurpose.Automatic);
            SetProposalActionsEnabled(false);
            UpdateTrayIconState();

            // プランは送信時スナップショット基準の座標を持つ。確認ダイアログなどの間に本文が
            // 変わっていた場合、この座標は失効しておりアンカーを範囲外へ作ると例外になる。
            // その場合は全リクエストを未送信として中止する（編集は OnTabTextChanged がデバウンスを
            // 再始動済みなので、finally の ScheduleAutomaticProofreading が再送する）。
            if (string.Equals(tab.Document.Text, snapshot, StringComparison.Ordinal))
            {
                requestAnchors = new TextAnchor?[plan.Requests.Count];
                for (int index = 0; index < plan.Requests.Count; index++)
                {
                    requestAnchors[index] = CreateAnchor(tab.Document, plan.Requests[index].SourceStart);
                }
            }
            else
            {
                editDiscardHappened = true;
            }

            for (int index = 0; index < plan.Requests.Count; index++)
            {
                if (!ReferenceEquals(tab, _tabs.Active))
                {
                    tabSwitchedDuringRun = true;
                    break;
                }

                TimeSpan intervalDelay =
                    _proofreadingSchedule.GetDelayBeforeSend(DateTimeOffset.Now);
                if (intervalDelay > TimeSpan.Zero)
                {
                    SetProofreadingStatus(
                        $"次の校正送信まで {Math.Ceiling(intervalDelay.TotalSeconds):0} 秒");
                    await Task.Delay(intervalDelay);
                }

                if (!ReferenceEquals(tab, _tabs.Active))
                {
                    tabSwitchedDuringRun = true;
                    break;
                }

                // 送信直前に対象範囲（パート）が今も無変更かを確認する。編集されていた場合は、
                // まだ送信していない残りの API 呼び出しを中止し、編集ブロックは入力停止後の自動
                // 再校正へ委ねる。選択範囲の手動校正は従来どおり全文一致を要求する。
                TextAnchor? validatedAnchor = null;
                if (!selectionRun)
                {
                    if (requestAnchors is null ||
                        requestAnchors[index] is not { } anchor ||
                        !IsPartIntact(anchor, plan.Requests[index]))
                    {
                        editDiscardHappened = true;
                        break;
                    }
                    validatedAnchor = anchor;
                }
                else if (!string.Equals(tab.Document.Text, snapshot, StringComparison.Ordinal))
                {
                    editDiscardHappened = true;
                    break;
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
                completed.Add((request, result, recordedApiCall.Id ?? -1, validatedAnchor));
            }

            if (!TryApplyCompletedResults(out int proposalCount))
            {
                SetProofreadingStatus("安全検査に失敗したため校正結果を破棄しました", force: true);
                return;
            }

            if (editDiscardHappened)
            {
                // 破棄通知は一回の処理につき一度だけ表示する。料金・破棄件数の詳細は課金履歴で
                // 確認できるようにする（proofreading-ux-fixes-plan.md §7.3）。
                SetProofreadingStatus("編集された部分は、入力が止まってから再校正します", force: true);
            }
            else
            {
                ShowProofreadingUsage(proposalCount, responseCosts);
            }
        }
        catch (GeminiClientException ex)
        {
            // 途中で API が失敗しても、**それより前に成功した応答は既に課金されている**。
            // ここで丸ごと破棄すると、その段落は未送信のまま残り、次の自動校正でもう一度
            // 送られて二度目の課金になる（10段落中9件成功・1件タイムアウトで9件ぶんが再課金）。
            // 部分結果保持（proofreading-ux-fixes-plan.md §7.2）は本文編集による中断だけを
            // 想定していたが、API 失敗も「途中まで成功した」という同じ状態なので同じ扱いにする。
            // 失敗したリクエスト以降は未送信として残るため、次回そこだけが再送される。
            bool salvaged = false;
            if (!resultsApplied && completed.Count > 0)
            {
                try
                {
                    salvaged = TryApplyCompletedResults(out _);
                }
                catch (Exception)
                {
                    // 救済そのものが失敗しても、API エラーの通知（下）は必ず出す。
                    salvaged = false;
                }
            }

            if (!resultsApplied)
                MarkApiCallsDiscarded(successfulApiCallIds);
            if (!selectionRun)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);
            string knownUsage = responseCosts.Count == 0
                ? "使用量・料金は確認できませんでした。"
                : BuildUsageText(responseCosts);
            // 件数は書かない。アンカー照合で一部が破棄されることがあり、completed.Count は
            // 「反映できた件数」より多くなりうる（金額に関わる話で盛って伝えない）。
            string salvageNote = salvaged
                ? "\n\n途中まで完了していた応答は破棄していません。有効なぶんは反映済みで、" +
                  "送信できなかった箇所だけを次回あらためて校正します。"
                : "";
            MessageBox.Show(
                this,
                ex.Message + "\n\n" + knownUsage + salvageNote,
                "校正できませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetProofreadingStatus(responseCosts.Count == 0
                ? "校正に失敗しました（使用量・料金は未確認）"
                : "校正に失敗しました " + FormatCostWithJpy(responseCosts),
                force: true);
        }
        catch (Exception ex)
        {
            // 予期しない内部エラー。以前は throw して fire-and-forget の手動経路では
            // 未観測タスク例外として黙って消えていた（自動経路との可視性の非対称）。
            // ここで拾ってユーザーへ伝える。本文はこの時点では変更されていない（置換は
            // ユーザー操作時のみ）ため、データは安全。
            if (!resultsApplied)
                MarkApiCallsDiscarded(successfulApiCallIds);
            MessageBox.Show(
                this,
                "校正処理で予期しないエラーが発生しました。\n\n" + ex.Message,
                "校正できませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetProofreadingStatus("校正処理でエラーが発生しました", force: true);
        }
        finally
        {
            _proofreadingRunInProgress = false;
            UnpinModelAfterRun();
            SetProposalActionsEnabled(true);
            UpdateTrayIconState();
            ScheduleAutomaticProofreading();
        }

        // 課金済みの応答（completed）を現在の本文へ反映する。正常終了時と、途中で API が失敗して
        // 残りを送れなかった場合の両方から呼ぶ（後者では既に課金された成功ぶんを救済する）。
        // 戻り値は「結果を適用できたか」＝安全検査を通ったか。
        bool TryApplyCompletedResults(out int proposalCount)
        {
            proposalCount = 0;

            // 中止された（送信しなかった）リクエストのパートは未送信として残し、次回の自動プランで
            // 再試行させる。本文編集で中断した場合、その編集は OnTabTextChanged がデバウンスを
            // 再始動済みなので、finally の ScheduleAutomaticProofreading が入力停止後に再送する。
            // API 失敗の場合も同じで、失敗したリクエスト以降だけが次回の対象になる。
            for (int index = completed.Count; index < plan.Requests.Count; index++)
            {
                unsentParts.Add(
                    (plan.Requests[index].ParagraphIndex, plan.Requests[index].PartIndex));
            }

            // 完了済みリクエストを現在の本文と照合し、編集されていない範囲の結果だけを現在位置へ
            // 対応付けて保持する。編集された範囲の結果は破棄し、既に課金された呼び出しは
            // discarded_cnt へ正確に記録する（料金の詳細は課金履歴で確認できる）。
            List<(int CurrentPartStart, GeminiProofreadingResult Result)> validResults = [];
            List<long> discardedCallIds = [];
            foreach ((ProofreadingRequest request, GeminiProofreadingResult result, long apiCallId, TextAnchor? anchor) in completed)
            {
                if (selectionRun)
                {
                    if (editDiscardHappened)
                    {
                        if (apiCallId >= 0) discardedCallIds.Add(apiCallId);
                        continue;
                    }
                    validResults.Add((request.SourceStart, result));
                    continue;
                }

                if (anchor is not { } intactAnchor || !IsPartIntact(intactAnchor, request))
                {
                    // 対象範囲（パート）が編集された＝結果を破棄。同じ段落の別パート（無変更）の
                    // 結果は保持する。編集された場合は未送信として次回再校正する。
                    if (apiCallId >= 0) discardedCallIds.Add(apiCallId);
                    unsentParts.Add((request.ParagraphIndex, request.PartIndex));
                    editDiscardHappened = true;
                    continue;
                }

                validResults.Add((intactAnchor.Offset, result));
            }

            if (discardedCallIds.Count > 0)
                MarkApiCallsDiscarded(discardedCallIds);

            if (!selectionRun)
            {
                // 送信成功したパート（破棄・中止されていないパート）だけを送信済みとして記録する。
                // 破棄・中止されたパートは未送信のまま残り、次回の自動プラン（または入力停止後の
                // 再スケジュール）で再送される。二重課金を防ぐため、無変更パートの再送はしない。
                tab.ProofreadingPlanner.MarkSent(plan, unsentParts);
            }

            // 編集・中止が無く全リクエストが完了した場合は pending を消費する。編集があった場合は
            // 消費しない（入力停止後の再校正を finally の ScheduleAutomaticProofreading が担う）。
            if (!editDiscardHappened && !tabSwitchedDuringRun)
                _proofreadingSchedule.MarkAutomaticHandled(tab.Id);

            string corrected = selectionRun && editDiscardHappened
                ? tab.Document.Text
                : ProofreadingResultMerger.MergePartial(tab.Document.Text, validResults);
            DocumentDiffResult loaded =
                tab.Proofreading.LoadCorrectedDocument(corrected);
            if (!loaded.Accepted)
            {
                // 安全検査に失敗した場合、この実行の結果は適用されない。課金済みの呼び出しは
                // 破棄として記録する（破棄件数と金額は課金履歴で確認できる）。
                MarkApiCallsDiscarded(successfulApiCallIds);
                return false;
            }

            // ここ以降の例外は、提案（0件を含む）が既にUIへ反映された後のもの。
            resultsApplied = true;
            proposalCount = loaded.Changes.Count;
            return true;
        }
    }

    /// <summary>
    /// 校正実行の間、本文編集による位置のずれを追従させるアンカーを張る。
    /// 挿入はアンカー位置より前（＝対象範囲の外）として扱い、削除で消えても対象の
    /// 原文が無くなるので IsDeleted として判定できるようにする。
    /// </summary>
    private static TextAnchor CreateAnchor(TextDocument document, int offset)
    {
        TextAnchor anchor = document.CreateAnchor(offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        anchor.SurviveDeletion = true;
        return anchor;
    }

    /// <summary>
    /// リクエストの対象範囲（パート）が今も無変更かどうかを判定する。
    /// アンカー位置の現在オフセットから <see cref="ProofreadingRequest.SourceLength"/> 文字が
    /// <see cref="ProofreadingRequest.SourceText"/> と一致するかを見る（部分結果保持の単位は
    /// 段落ではなくリクエストごと。2,000文字超で分割された段落の別パートが無関係に破棄されない）。
    /// </summary>
    private static bool IsPartIntact(TextAnchor anchor, ProofreadingRequest request)
    {
        if (anchor.IsDeleted)
            return false;

        int offset = anchor.Offset;
        if (offset < 0 ||
            request.SourceLength <= 0 ||
            offset + request.SourceLength > anchor.Document.TextLength)
        {
            return false;
        }

        return string.Equals(
            anchor.Document.GetText(offset, request.SourceLength),
            request.SourceText,
            StringComparison.Ordinal);
    }

    private bool ConfirmProofreadingApiUse(int requestCount, bool manual)
    {
        if (!_settings.Current.ConfirmPaidApiCalls)
            return true;

        SuppressAutoHide();
        try
        {
            string trigger = manual ? "手動校正" : "自動校正";
            // 用途で使うモデルが変わるため、確認ダイアログの単価も用途で解決する。
            ProofreadingPurpose purpose =
                manual ? ProofreadingPurpose.Manual : ProofreadingPurpose.Automatic;
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
                $"{trigger}で{ActiveProviderName(purpose)} APIを{requestCount}回呼び出します。料金が発生します。\n\n" +
                limitWarning +
                BuildPricingSummary(purpose) + "\n" +
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

        // 円建て単価のモデル（PLaMo）は、USD へ換算してから記録する（DBの保存通貨はUSDのまま）。
        // レートが取れない間は換算できないので、推測レートで誤った金額を記録するくらいなら
        // 「料金未確認」として残す（要件 3.5.2）。
        decimal? usdCost = quote.ToUsd(fxRate?.UsdJpy);
        if (usdCost is null)
            return new ApiUsageCost(promptTokens, outputTokens, 0m, fxRate, IsUsageKnown: false);

        return new ApiUsageCost(promptTokens, outputTokens, usdCost.Value, fxRate, IsUsageKnown: true);
    }

    private string BuildPricingSummary(ProofreadingPurpose? purpose = null)
    {
        string model = ModelForPurpose(purpose);
        ModelPricing pricing = _pricing.GetPricing(model);
        string unit = string.Equals(pricing.Currency, PricingCurrency.Jpy, StringComparison.Ordinal)
            ? "¥"
            : "$";
        return
            $"{ProofreadingModelCatalog.DisplayName(model)} 単価（{pricing.UpdatedAt}）: " +
            $"入力 {unit}{pricing.InputUsdPerMillion:0.####}／100万トークン、" +
            $"出力・推論 {unit}{pricing.OutputUsdPerMillion:0.####}／100万トークン\n" +
            "※ 表示料金は概算です。キャッシュ関連料金などは考慮していないため、" +
            "実際の請求額が表示額を上回る場合があります。";
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
            _monthUsageKnown = true;
            decimal limit = _settings.Current.MonthlyLimitUsd;
            UsageLimitState limitState = UsageLimitService.Evaluate(
                _monthUsageUsd, limit, _settings.Current.MonthlyLimitWarningRatio);

            // 主表示は設定で選択された項目・通貨だけを出す（proofreading-ux-fixes-plan.md §8）。
            // 既定は「当月＋為替、円表示」。ツールチップには選択されていない詳細項目も含める
            // （主表示を再び過密にしない）。
            StatusUsage.Text = StatusBarUsageFormatter.Format(
                new StatusBarDisplayOptions(
                    _settings.Current.StatusBarShowLatest,
                    _settings.Current.StatusBarShowSession,
                    _settings.Current.StatusBarShowToday,
                    _settings.Current.StatusBarShowMonth,
                    _settings.Current.StatusBarShowFx,
                    _settings.Current.StatusBarCurrency),
                latest,
                session,
                today,
                month,
                _fxRates.GetCachedRate());
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
        // 設定を読めなかった起動では、設定値に依存する破壊的処理を走らせない。
        // ApiLogRetentionMonths の既定は 12 か月だが、実際の設定が 0（＝無期限保持）でも、
        // 一時的な settings.json の読み取り失敗だけで 12 か月より前の明細が日次サマリへ
        // 不可逆に圧縮されてしまう（個々の呼び出しの内訳は復元できない）。呼び出し元 3 経路の
        // うち設定変更経路は Replace() が IsReadFailed を解除するため、ここで止めても支障はない。
        if (_settings.IsReadFailed) return;

        DateTimeOffset? cutoff = ApiLogRetention.ComputeCutoff(
            DateTimeOffset.Now, _settings.Current.ApiLogRetentionMonths);
        if (cutoff is null) return;

        // 3経路がそれぞれ Task.Run で走るため、同時に 2 本以上動きうる。Compact 側は抽出から
        // 削除までを 1 トランザクションに収めてあるので合計は狂わないが、無駄に長い書き込み
        // トランザクションを重ねる意味はない。先行が走っている間は黙って見送る
        // （見送っても次の機会＝設定変更・日付ロールオーバー・次回起動で必ず再試行される）。
        if (Interlocked.CompareExchange(ref _compactInProgress, 1, 0) != 0) return;

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
            finally
            {
                Volatile.Write(ref _compactInProgress, 0);
            }
        });
    }

    /// <summary>課金明細の圧縮が走っているか（0 = 走っていない）。<see cref="Interlocked"/> で操作する。</summary>
    private int _compactInProgress;

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
        try
        {
            if (_tabs.RestoreLastClosed() is null) SetTransientStatus("復元できるタブがありません");
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // InvalidDataException は「タブIDの形式が不正」。RestoreLastClosed 側で飛ばしている
            // ので通常ここへは来ないが、拾い漏らすと共通の「予期しないエラー」ダイアログになり、
            // Ctrl+Shift+T が壊れた行を踏むたびにクラッシュ扱いで報告される。
            SetTransientStatus("タブを復元できませんでした（本文ファイルを移動できません）");
        }
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

            // GUI smoke test only: after the settings dialog has completed, exercise the
            // same Application shutdown path as the tray's Exit command.
            if (Environment.GetCommandLineArgs().Any(
                    arg => string.Equals(arg, "--gui-test-exit-after-settings", StringComparison.Ordinal)))
            {
                Application.Current.Shutdown();
            }
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

    /// <summary>
    /// 校正処理中の一時的な状態をステータスバーへ表示する。
    /// <paramref name="force"/> が true のときは、長時間の送信中に定期更新（ヒント文）へ
    /// 上書きされないよう、通常より長く保持する。
    /// </summary>
    private void SetProofreadingStatus(string message, bool force = false)
    {
        StatusRight.Text = message;
        _statusMessageUntil = DateTime.UtcNow.AddSeconds(force ? 30 : 4);
    }

    /// <summary>
    /// 用途に対応するモデルID。<paramref name="purpose"/> を渡さない場合は、実行中にピン留めされた
    /// モデル（ピン留め前なら自動用）を返す。
    ///
    /// **課金確認ダイアログのように実行開始（ピン留め）より前に呼ぶ箇所では、必ず用途を渡すこと。**
    /// 渡さないと手動校正でも自動用モデルの名前と単価を表示してしまう。
    /// </summary>
    private string ModelForPurpose(ProofreadingPurpose? purpose)
        => purpose is { } value && _proofreadingClient is ProofreadingClientRouter router
            ? router.ModelFor(value)
            : _proofreadingClient.Model;

    private string? GetActiveApiKey(ProofreadingPurpose? purpose = null)
    {
        ApiProvider provider =
            ProofreadingModelCatalog.ProviderOf(ModelForPurpose(purpose));
        return _credentials.GetApiKey(provider, ApiKeySourceFor(provider));
    }

    private ApiKeySource ApiKeySourceFor(ApiProvider provider)
        => provider switch
        {
            ApiProvider.Google => _settings.Current.GeminiApiKeySource,
            ApiProvider.OpenAi => _settings.Current.OpenAiApiKeySource,
            ApiProvider.Anthropic => _settings.Current.AnthropicApiKeySource,
            ApiProvider.PreferredNetworks => _settings.Current.PlamoApiKeySource,
            _ => ApiKeySource.Unspecified,
        };

    private string ActiveProviderName(ProofreadingPurpose? purpose = null)
        => ProofreadingModelCatalog.ProviderDisplayName(
            ProofreadingModelCatalog.ProviderOf(ModelForPurpose(purpose)));

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
