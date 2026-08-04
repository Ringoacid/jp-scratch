using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JpScratch.Editor;
using JpScratch.Infrastructure;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>設定画面（要件 5 / v1）。設定の変更は OK またはウィンドウを閉じると保存する。</summary>
public partial class SettingsWindow : Window
{
    private const string AutoFontLabel = "（自動: メイリオ → 游ゴシック）";

    private readonly SettingsService _service;
    private readonly CredentialService _credentials;
    private readonly PricingService _pricing;
    private readonly StyleGuideRepository _styleGuides;
    private readonly ReactionRepository _reactions;
    private bool _deleteStoredKey;
    private bool _deleteOpenAiStoredKey;
    private bool _loadingCredentialControls;
    private bool _loadingOpenAiCredentialControls;
    private bool _settingsApplied;

    // モデル単価編集用。入力途中の値はモデルごとに「生テキスト」で保持し、検証はOK押下時にまとめて
    // 行う（コンボを切り替えるたびに検証して選択を巻き戻すような作りにしない）。
    private Dictionary<string, ModelPricing> _pricingOriginal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Input, string Output, string UpdatedAt)> _pricingText =
        new(StringComparer.Ordinal);
    private string? _selectedPricingModel;
    private bool _loadingPricingControls;
    private bool _pricingControlsLoaded;

    // スタイルガイドの世代管理。編集・有効化・削除はOKボタンを待たずその場でDBへ書く
    // （AppSettingsのJSON保存とは独立した操作のため）。
    private IReadOnlyList<StyleGuide> _styleGuideHistory = [];
    private bool _loadingStyleGuideControls;

    internal SettingsWindow(
        SettingsService service,
        CredentialService credentials,
        PricingService pricing,
        StyleGuideRepository styleGuides,
        ReactionRepository reactions)
    {
        _service = service;
        _credentials = credentials;
        _pricing = pricing;
        _styleGuides = styleGuides;
        _reactions = reactions;
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
        AutoProofreadingCheck.IsChecked = s.AutoProofreadingEnabled;
        ConfirmPaidApiCallsCheck.IsChecked = s.ConfirmPaidApiCalls;
        ProofreadingDebounceBox.Text =
            s.ProofreadingDebounceMs.ToString(CultureInfo.InvariantCulture);
        ProofreadingIntervalBox.Text =
            s.ProofreadingMinimumIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        // 表示は往復不変（表示→パースで値が変わらない）書式にする。"0.##" 等の低精度書式だと、
        // 0.0032 のような細かい上限額が "0" に丸まって表示され、その状態で保存すると
        // 上限0（無制限）へ黙って壊れる（レビュー指摘の再現防止）。SettingsFieldFormattingを参照。
        MonthlyLimitBox.Text = SettingsFieldFormatting.FormatMonthlyLimitUsd(s.MonthlyLimitUsd);
        MonthlyLimitWarningBox.Text = SettingsFieldFormatting.FormatWarningPercent(s.MonthlyLimitWarningRatio);
        ApiLogRetentionBox.Text = s.ApiLogRetentionMonths.ToString(CultureInfo.InvariantCulture);

        // ステータスバーの課金表示（proofreading-ux-fixes-plan.md §8）
        StatusBarLatestCheck.IsChecked = s.StatusBarShowLatest;
        StatusBarSessionCheck.IsChecked = s.StatusBarShowSession;
        StatusBarTodayCheck.IsChecked = s.StatusBarShowToday;
        StatusBarMonthCheck.IsChecked = s.StatusBarShowMonth;
        StatusBarFxCheck.IsChecked = s.StatusBarShowFx;
        StatusBarCurrencyCombo.ItemsSource = new[] { "円表示", "ドル表示", "両方表示" };
        StatusBarCurrencyCombo.SelectedIndex = (int)s.StatusBarCurrency;

        _loadingCredentialControls = true;
        CredentialSourceCombo.ItemsSource = new[]
        {
            "アプリに保存したキー",
            $"環境変数 {CredentialService.EnvironmentVariableName}",
        };
        CredentialSourceCombo.SelectedIndex =
            s.GeminiApiKeySource == ApiKeySource.EnvironmentVariable ? 1 : 0;
        _loadingCredentialControls = false;
        RefreshCredentialStatus();

        _loadingOpenAiCredentialControls = true;
        OpenAiCredentialSourceCombo.ItemsSource = new[]
        {
            "アプリに保存したキー",
            $"環境変数 {CredentialService.OpenAiEnvironmentVariableName}",
        };
        OpenAiCredentialSourceCombo.SelectedIndex =
            s.OpenAiApiKeySource == ApiKeySource.EnvironmentVariable ? 1 : 0;
        _loadingOpenAiCredentialControls = false;
        RefreshOpenAiCredentialStatus();

        CustomInstructionBox.Text = s.CustomInstruction;
        StyleGuideAutoGenerateCheck.IsChecked = s.StyleGuideAutoGenerateEnabled;
        StyleGuideThresholdBox.Text =
            s.StyleGuideGenerationThreshold.ToString(CultureInfo.InvariantCulture);
        LoadStyleGuideControls();
        LoadRejectionTrendControls();

        LoadPricingControls();
        ProofreadingModelCombo.ItemsSource = ProofreadingModelCatalog.SupportedModels
            .Select(ProofreadingModelCatalog.DisplayName)
            .ToArray();
        ProofreadingModelCombo.SelectedItem =
            ProofreadingModelCatalog.DisplayName(s.ProofreadingModel);

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
        s.AutoProofreadingEnabled = AutoProofreadingCheck.IsChecked == true;
        s.ConfirmPaidApiCalls = ConfirmPaidApiCallsCheck.IsChecked == true;
        string? selectedModelName = ProofreadingModelCombo.SelectedItem as string;
        if (selectedModelName is not null)
        {
            s.ProofreadingModel = ProofreadingModelCatalog.SupportedModels
                .FirstOrDefault(model => ProofreadingModelCatalog.DisplayName(model) == selectedModelName)
                ?? s.ProofreadingModel;
        }
        s.ProofreadingDebounceMs =
            (int)ParseNumber(
                ProofreadingDebounceBox.Text,
                s.ProofreadingDebounceMs);
        s.ProofreadingMinimumIntervalSeconds =
            (int)ParseNumber(
                ProofreadingIntervalBox.Text,
                s.ProofreadingMinimumIntervalSeconds);
        s.MonthlyLimitUsd = SettingsFieldFormatting.ParseDecimalOrDefault(
            MonthlyLimitBox.Text, s.MonthlyLimitUsd);
        decimal warningPercent = SettingsFieldFormatting.ParseDecimalOrDefault(
            MonthlyLimitWarningBox.Text, s.MonthlyLimitWarningRatio * 100m);
        s.MonthlyLimitWarningRatio = warningPercent / 100m;
        s.ApiLogRetentionMonths = (int)ParseNumber(
            ApiLogRetentionBox.Text, s.ApiLogRetentionMonths);

        s.StatusBarShowLatest = StatusBarLatestCheck.IsChecked == true;
        s.StatusBarShowSession = StatusBarSessionCheck.IsChecked == true;
        s.StatusBarShowToday = StatusBarTodayCheck.IsChecked == true;
        s.StatusBarShowMonth = StatusBarMonthCheck.IsChecked == true;
        s.StatusBarShowFx = StatusBarFxCheck.IsChecked == true;
        // 未知値は円表示（0）へ正規化する（SettingsService.Normalize と同一の規約）。
        s.StatusBarCurrency = (StatusBarCurrencyFormat)Math.Clamp(
            StatusBarCurrencyCombo.SelectedIndex, 0, 2);

        s.GeminiApiKeySource = CredentialSourceCombo.SelectedIndex == 1
            ? ApiKeySource.EnvironmentVariable
            : ApiKeySource.Stored;
        s.OpenAiApiKeySource = OpenAiCredentialSourceCombo.SelectedIndex == 1
            ? ApiKeySource.EnvironmentVariable
            : ApiKeySource.Stored;

        s.CustomInstruction = CustomInstructionBox.Text.Trim();
        s.StyleGuideAutoGenerateEnabled = StyleGuideAutoGenerateCheck.IsChecked == true;
        s.StyleGuideGenerationThreshold = (int)ParseNumber(
            StyleGuideThresholdBox.Text, s.StyleGuideGenerationThreshold);

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

    private const string HotkeyHintText = "（修飾キーと組み合わせてください）";

    /// <summary>無効なキーを押した直後に欄内へ表示したヒントと、戻すべき元の値。</summary>
    private (TextBox Box, string OriginalText)? _hotkeyHint;

    /// <summary>ヒント表示中なら元の値へ戻す（次のキー操作・フォーカス喪失のとき）。</summary>
    private void RestoreHotkeyHint(TextBox box)
    {
        if (_hotkeyHint is { } hint && ReferenceEquals(hint.Box, box))
        {
            box.Text = hint.OriginalText;
            box.ToolTip = null;
            _hotkeyHint = null;
        }
    }

    /// <summary>
    /// ホットキーの入力欄。押されたキーをそのまま割り当てる。
    /// 修飾キー単独では確定させない（Alt だけを登録しても意味がないため）。
    /// </summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        // 直前の無効キーで出したヒントが残っていれば、次の操作で元の値へ戻す。
        RestoreHotkeyHint(box);

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // ナビゲーションキーは、修飾キーが無いとき（Tab は Shift 付きも含む）だけ既定の動作
        // （Tab で次の欄へ移動、Esc でダイアログを閉じるなど）を通す。修飾キーと組み合わされた
        // 場合はホットキーとして割り当てる（Ctrl+Enter や Alt+← を登録できるようにする）。
        // ここを無条件に吞み込むと、キーボードだけでフォーカスを外せなくなる（フォーカストラップ）。
        bool noModifiers = Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift;
        if (noModifiers &&
            key is Key.Tab or Key.Escape or Key.Enter
                or Key.Left or Key.Right or Key.Up or Key.Down
                or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            return;
        }

        e.Handled = true;

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
            // 欄の中身を永続的なエラー文で置き換えない（そのまま OK を押すまで表示にゴミが残る
            // ため）。ツールチップに加えて欄内にも一時ヒントを表示し、次のキー操作かフォーカス
            // 喪失で元の値へ戻す（ホバーしない限り無反応に見える、を防ぐ）。
            box.ToolTip = "修飾キーと組み合わせてください";
            if (_hotkeyHint is not { } hint || !ReferenceEquals(hint.Box, box))
                _hotkeyHint = (box, box.Text);
            box.Text = HotkeyHintText;
            return;
        }

        box.ToolTip = null;
        box.Text = spec.DisplayName;
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) RestoreHotkeyHint(box);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAndClose();
    }

    private bool TrySaveSettings()
    {
        // 現在値のコピーに書き込んでから差し替える。途中で例外が出ても設定が半端に壊れない。
        var updated = _service.Current.Clone();
        ApplyTo(updated);

        // 副作用（credentials.dat / pricing.json への書き込み）より前に、入力の検証をすべて済ませる。
        // 検証エラーで戻るときに「片方だけ書き込み済み」という中途半端な状態を作らない
        // （例: 単価欄の入力ミスで戻る前にAPIキーだけ credentials.dat へ書き込まれてしまう、等）。
        // 書き込みの順序は「単価 → APIキー」にしている。後段（キー）が失敗してキャンセルした
        // 場合でも、より重要なキーだけが先に書き込まれた状態を残さないため。
        // 単価コントロールの初期化に失敗した状態で閉じる場合、空の単価表を保存して
        // pricing.json を壊さない。通常の操作では LoadPricingControls が必ず先に完了する。
        if (_pricingControlsLoaded && _pricingText.Count > 0)
        {
            if (!TryBuildPricingTable(out Dictionary<string, ModelPricing> pricingTable)) return false;
            if (!TrySavePricing(pricingTable)) return false;
        }
        if (!TryApplyCredentialChanges(updated)) return false;
        _service.Replace(updated);
        return true;
    }

    private void SaveAndClose()
    {
        if (_settingsApplied || !TrySaveSettings()) return;

        _settingsApplied = true;
        DialogResult = true;
        Close();
    }

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => SaveAndClose();

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_settingsApplied) return;

        if (TrySaveSettings())
        {
            _settingsApplied = true;
            return;
        }

        e.Cancel = true;
    }

    private void CredentialSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingCredentialControls) RefreshCredentialStatus();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ApiKeyBox.Password.Length > 0) _deleteStoredKey = false;
        RefreshCredentialStatus();
    }

    private void OpenAiCredentialSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingOpenAiCredentialControls) RefreshOpenAiCredentialStatus();
    }

    private void OpenAiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (OpenAiApiKeyBox.Password.Length > 0) _deleteOpenAiStoredKey = false;
        RefreshOpenAiCredentialStatus();
    }

    private void DeleteOpenAiStoredKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _deleteOpenAiStoredKey = true;
        OpenAiApiKeyBox.Clear();
        RefreshOpenAiCredentialStatus();
    }

    private void DeleteStoredKeyButton_Click(object sender, RoutedEventArgs e)
    {
        _deleteStoredKey = true;
        ApiKeyBox.Clear();
        RefreshCredentialStatus();
    }

    private void RefreshCredentialStatus()
    {
        if (CredentialStatusText is null) return;

        string stored = _deleteStoredKey
            ? "保存済みキー: OKを押すと削除"
            : _credentials.StoredKeyState switch
            {
                StoredCredentialState.Available => "保存済みキー: あり（値は表示しません）",
                StoredCredentialState.Unreadable => "保存済みキー: 読み取れません（削除または置き換えが必要です）",
                _ => "保存済みキー: なし",
            };

        string environment = _credentials.EnvironmentKeyAvailable
            ? $"環境変数 {CredentialService.EnvironmentVariableName}: 検出済み"
            : $"環境変数 {CredentialService.EnvironmentVariableName}: 見つかりません";

        string pending = ApiKeyBox?.Password.Length > 0
            ? "\n新しいキー: OKを押すと暗号化して保存"
            : "";

        CredentialStatusText.Text = $"{stored}\n{environment}{pending}";
    }

    private void RefreshOpenAiCredentialStatus()
    {
        if (OpenAiCredentialStatusText is null) return;

        string stored = _deleteOpenAiStoredKey
            ? "保存済みキー: OKを押すと削除"
            : _credentials.OpenAiStoredKeyState switch
            {
                StoredCredentialState.Available => "保存済みキー: あり（値は表示しません）",
                StoredCredentialState.Unreadable => "保存済みキー: 読み取れません（削除または置き換えが必要です）",
                _ => "保存済みキー: なし",
            };

        string environment = _credentials.OpenAiEnvironmentKeyAvailable
            ? $"環境変数 {CredentialService.OpenAiEnvironmentVariableName}: 検出済み"
            : $"環境変数 {CredentialService.OpenAiEnvironmentVariableName}: 見つかりません";

        string pending = OpenAiApiKeyBox?.Password.Length > 0
            ? "\n新しいキー: OKを押すと暗号化して保存"
            : "";

        OpenAiCredentialStatusText.Text = $"{stored}\n{environment}{pending}";
    }

    /// <summary>
    /// スタイルガイドの世代コンボを初期化する。有効な世代（あれば）を選択状態にする。
    /// </summary>
    private void LoadStyleGuideControls()
    {
        _loadingStyleGuideControls = true;

        _styleGuideHistory = _styleGuides.ListAll();
        StyleGuideHistoryCombo.ItemsSource = _styleGuideHistory
            .Select(FormatStyleGuideLabel)
            .ToArray();

        int activeIndex = IndexOf(_styleGuideHistory, guide => guide.IsActive);
        StyleGuideHistoryCombo.SelectedIndex = _styleGuideHistory.Count == 0
            ? -1
            : activeIndex >= 0 ? activeIndex : 0;

        _loadingStyleGuideControls = false;
        ShowSelectedStyleGuide();
    }

    private static int IndexOf(IReadOnlyList<StyleGuide> guides, Func<StyleGuide, bool> predicate)
    {
        for (int i = 0; i < guides.Count; i++)
        {
            if (predicate(guides[i])) return i;
        }
        return -1;
    }

    private static string FormatStyleGuideLabel(StyleGuide guide)
    {
        string active = guide.IsActive ? "★ " : "";
        string edited = guide.IsUserEdited ? "・編集済み" : "";
        return $"{active}{guide.GeneratedAt.LocalDateTime:yyyy-MM-dd HH:mm}（{guide.SourceReactions}件から{edited}）";
    }

    private StyleGuide? SelectedStyleGuide()
    {
        int index = StyleGuideHistoryCombo.SelectedIndex;
        return index >= 0 && index < _styleGuideHistory.Count ? _styleGuideHistory[index] : null;
    }

    private void ShowSelectedStyleGuide()
    {
        StyleGuide? selected = SelectedStyleGuide();
        if (selected is null)
        {
            StyleGuideContentBox.Text = "";
            StyleGuideStatusText.Text = "スタイルガイドはまだ生成されていません。";
            return;
        }

        StyleGuideContentBox.Text = selected.Content;
        string active = selected.IsActive ? "有効（校正に使用中）" : "無効（履歴のみ）";
        string edited = selected.IsUserEdited ? "・手編集あり" : "";
        StyleGuideStatusText.Text =
            $"{selected.GeneratedAt.LocalDateTime:yyyy-MM-dd HH:mm} 生成・" +
            $"{selected.SourceReactions}件から・{active}{edited}";
    }

    private void StyleGuideHistoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingStyleGuideControls) ShowSelectedStyleGuide();
    }

    private void StyleGuideSaveButton_Click(object sender, RoutedEventArgs e)
    {
        StyleGuide? selected = SelectedStyleGuide();
        if (selected is null)
        {
            MessageBox.Show(this, "編集できるスタイルガイドがありません。「今すぐ生成」はメインウィンドウのリアクション蓄積から提案されます。",
                "JP Scratch", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(StyleGuideContentBox.Text))
        {
            MessageBox.Show(this, "空の内容は保存できません。削除する場合は「この世代を削除」を使ってください。",
                "JP Scratch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _styleGuides.UpdateContent(selected.Id, StyleGuideContentBox.Text);
        LoadStyleGuideControls();
    }

    private void StyleGuideActivateButton_Click(object sender, RoutedEventArgs e)
    {
        StyleGuide? selected = SelectedStyleGuide();
        if (selected is null) return;

        _styleGuides.SetActive(selected.Id);
        LoadStyleGuideControls();
    }

    private void StyleGuideDeactivateButton_Click(object sender, RoutedEventArgs e)
    {
        _styleGuides.Deactivate();
        LoadStyleGuideControls();
    }

    private void StyleGuideDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        StyleGuide? selected = SelectedStyleGuide();
        if (selected is null) return;

        MessageBoxResult confirm = MessageBox.Show(
            this,
            $"{selected.GeneratedAt.LocalDateTime:yyyy-MM-dd HH:mm} の世代を削除します。元に戻せません。",
            "JP Scratch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        _styleGuides.Delete(selected.Id);
        LoadStyleGuideControls();
    }

    /// <summary>
    /// 学習効果の可視化（要件3.4「完了の判断基準」＝拒否率の低下）。
    /// <see cref="ReactionRepository.GetRejectionRateTrend"/>の区間を新しい順に並べ、
    /// 各区間の分母（<c>Total</c>）はどれも完全なので、表示件数を絞っても拒否率の計算自体は狂わない
    /// （切り詰めているのは表示だけだと分かるよう、超過分があれば件数を明示する。CSVエクスポートで
    /// 学んだ「上限を掛けたら何が落ちたか必ず示す」規約）。
    /// </summary>
    private const int MaxDisplayedRejectionBuckets = 24;

    private void LoadRejectionTrendControls()
    {
        RejectionTrendPanel.Children.Clear();

        IReadOnlyList<RejectionRateBucket> buckets;
        try
        {
            buckets = _reactions.GetRejectionRateTrend();
        }
        catch (Exception)
        {
            RejectionTrendPanel.Children.Add(NewTrendInfoText("拒否率の推移を読み込めませんでした。"));
            return;
        }

        if (buckets.Count == 0)
        {
            RejectionTrendPanel.Children.Add(NewTrendInfoText("リアクションがまだありません。"));
            return;
        }

        List<RejectionRateBucket> newestFirst = buckets.Reverse().ToList();
        if (newestFirst.Count > MaxDisplayedRejectionBuckets)
        {
            RejectionTrendPanel.Children.Add(NewTrendInfoText(
                $"全{newestFirst.Count}区間中、直近{MaxDisplayedRejectionBuckets}区間を表示します。"));
            newestFirst = newestFirst.Take(MaxDisplayedRejectionBuckets).ToList();
        }

        foreach (RejectionRateBucket bucket in newestFirst)
        {
            RejectionTrendPanel.Children.Add(BuildRejectionTrendRow(bucket));
        }
    }

    private static TextBlock NewTrendInfoText(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        return block;
    }

    private FrameworkElement BuildRejectionTrendRow(RejectionRateBucket bucket)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

        var label = new TextBlock
        {
            Text = bucket.IsComplete ? bucket.Label : $"{bucket.Label}（進行中）",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        Grid.SetColumn(label, 0);

        var bar = new ProgressBar
        {
            Style = (Style)FindResource("UsageProgressBar"),
            Minimum = 0,
            Maximum = 100,
            Value = bucket.RejectionRate * 100,
            Height = 14,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        bar.SetResourceReference(Control.ForegroundProperty, RejectionRateBrushKey(bucket.RejectionRate));
        Grid.SetColumn(bar, 1);

        var summary = new TextBlock
        {
            Text = $"拒否 {bucket.Rejected}/{bucket.Total}件（{bucket.RejectionRate * 100:0}%）",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
        };
        summary.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        Grid.SetColumn(summary, 2);

        grid.Children.Add(label);
        grid.Children.Add(bar);
        grid.Children.Add(summary);
        return grid;
    }

    private static string RejectionRateBrushKey(double rate) => rate switch
    {
        >= 0.4 => "UsageProgressReachedBrush",
        >= 0.2 => "UsageProgressWarningBrush",
        _ => "UsageProgressNormalBrush",
    };

    /// <summary>
    /// モデル単価コンボを初期化する。既定モデル（<see cref="PricingService.DefaultModel"/>）を先頭、
    /// 残りは序数順に並べる。既定モデルが無ければ先頭を選択するが、<c>PricingService.Load</c> が
    /// 常に既定モデルを補うため通常は起きない。
    /// </summary>
    private void LoadPricingControls()
    {
        _loadingPricingControls = true;

        _pricingOriginal = new Dictionary<string, ModelPricing>(_pricing.Snapshot(), StringComparer.Ordinal);
        _pricingText.Clear();
        foreach ((string model, ModelPricing pricing) in _pricingOriginal)
        {
            _pricingText[model] = (
                SettingsFieldFormatting.FormatUnitPrice(pricing.InputUsdPerMillion),
                SettingsFieldFormatting.FormatUnitPrice(pricing.OutputUsdPerMillion),
                pricing.UpdatedAt);
        }

        List<string> models = _pricingOriginal.Keys
            .Where(model => model != PricingService.DefaultModel)
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToList();
        if (_pricingOriginal.ContainsKey(PricingService.DefaultModel))
            models.Insert(0, PricingService.DefaultModel);

        PricingModelCombo.ItemsSource = models;
        _selectedPricingModel = models.Count > 0 ? models[0] : null;
        PricingModelCombo.SelectedIndex = models.Count > 0 ? 0 : -1;

        ShowSelectedPricingModel();

        _loadingPricingControls = false;
        _pricingControlsLoaded = true;
    }

    private void PricingModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPricingControls) return;

        StashCurrentPricingModel();
        _selectedPricingModel = PricingModelCombo.SelectedItem as string;
        ShowSelectedPricingModel();
    }

    /// <summary>現在表示中の3欄を、選択中モデルの「生テキスト」として <see cref="_pricingText"/> へ退避する。</summary>
    private void StashCurrentPricingModel()
    {
        if (_selectedPricingModel is null) return;
        _pricingText[_selectedPricingModel] = (PricingInputBox.Text, PricingOutputBox.Text, PricingUpdatedAtBox.Text);
    }

    private void ShowSelectedPricingModel()
    {
        UpdateCredentialPanelVisibility();

        if (_selectedPricingModel is null || !_pricingText.TryGetValue(_selectedPricingModel, out var text))
        {
            PricingInputBox.Text = "";
            PricingOutputBox.Text = "";
            PricingUpdatedAtBox.Text = "";
            return;
        }

        PricingInputBox.Text = text.Input;
        PricingOutputBox.Text = text.Output;
        PricingUpdatedAtBox.Text = text.UpdatedAt;
    }

    private void UpdateCredentialPanelVisibility()
    {
        bool isGemini = string.Equals(
            _selectedPricingModel,
            ProofreadingModelCatalog.GeminiModel,
            StringComparison.Ordinal);
        bool isOpenAi = ProofreadingModelCatalog.IsOpenAi(_selectedPricingModel);

        GeminiCredentialPanel.Visibility = isGemini ? Visibility.Visible : Visibility.Collapsed;
        OpenAiCredentialPanel.Visibility = isOpenAi ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 全モデルの生テキストを検証する（副作用なし・ファイルには触らない）。失敗したらエラーを表示し、
    /// 該当モデルをコンボで選択し直して該当欄へフォーカスを当ててから false を返す
    /// （ウィンドウは閉じない）。
    /// </summary>
    private bool TryBuildPricingTable(out Dictionary<string, ModelPricing> table)
    {
        StashCurrentPricingModel();

        var built = new Dictionary<string, ModelPricing>(StringComparer.Ordinal);
        foreach ((string model, (string input, string output, string updatedAt)) in _pricingText)
        {
            ModelPricing original = _pricingOriginal[model];
            if (!SettingsFieldFormatting.TryBuildPricing(
                    input, output, updatedAt, original, out ModelPricing pricing, out string error))
            {
                ShowPricingError(model, error);
                table = built;
                return false;
            }

            built[model] = pricing;
        }

        table = built;
        return true;
    }

    /// <summary>
    /// 検証済みの単価表を <see cref="_pricing"/> へ保存する。IO失敗・検証二重チェック失敗は
    /// <see cref="TryApplyCredentialChanges"/> と同じ体裁の <see cref="MessageBox"/> で伝える。
    /// </summary>
    private bool TrySavePricing(Dictionary<string, ModelPricing> table)
    {
        try
        {
            _pricing.Replace(table);
        }
        catch (InvalidDataException ex)
        {
            // TryBuildPricingTableを個別に通した後の二重チェック（Replace側のValidate）で弾かれるのは、
            // 既定モデルのエントリを丸ごと削除した場合など、UIの操作だけでは起きにくいケース。
            MessageBox.Show(
                this,
                "モデル単価を保存できませんでした。" + ex.Message,
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "モデル単価を保存できませんでした。データフォルダへのアクセス権を確認してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        PricingErrorText.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ShowPricingError(string model, string error)
    {
        PricingErrorText.Text = $"{model}: {error}";
        PricingErrorText.Visibility = Visibility.Visible;

        if (model.Length > 0 && PricingModelCombo.SelectedItem as string != model)
        {
            _loadingPricingControls = true;
            PricingModelCombo.SelectedItem = model;
            _selectedPricingModel = model;
            ShowSelectedPricingModel();
            _loadingPricingControls = false;
        }

        // エラー文言（SettingsFieldFormatting.TryBuildPricing）から該当欄を判別してフォーカスする。
        if (error.StartsWith("出力単価", StringComparison.Ordinal))
            PricingOutputBox.Focus();
        else if (error.StartsWith("更新日", StringComparison.Ordinal))
            PricingUpdatedAtBox.Focus();
        else
            PricingInputBox.Focus();
    }

    private bool TryApplyCredentialChanges(AppSettings updated)
    {
        if (updated.GeminiApiKeySource == ApiKeySource.EnvironmentVariable &&
            !_credentials.EnvironmentKeyAvailable)
        {
            MessageBox.Show(
                this,
                $"環境変数 {CredentialService.EnvironmentVariableName} が見つかりません。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (updated.OpenAiApiKeySource == ApiKeySource.EnvironmentVariable &&
            !_credentials.OpenAiEnvironmentKeyAvailable)
        {
            MessageBox.Show(
                this,
                $"環境変数 {CredentialService.OpenAiEnvironmentVariableName} が見つかりません。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            if (ApiKeyBox.Password.Length > 0)
            {
                _credentials.SaveStoredApiKey(ApiKeyBox.Password);
            }
            else if (_deleteStoredKey)
            {
                _credentials.DeleteStoredApiKey();
            }

            if (OpenAiApiKeyBox.Password.Length > 0)
            {
                _credentials.SaveStoredOpenAiApiKey(OpenAiApiKeyBox.Password);
            }
            else if (_deleteOpenAiStoredKey)
            {
                _credentials.DeleteStoredOpenAiApiKey();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or CryptographicException
                                   or ArgumentException)
        {
            MessageBox.Show(
                this,
                "APIキーを保存または削除できませんでした。データフォルダへのアクセス権を確認してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

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
