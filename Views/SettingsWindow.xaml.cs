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
using JpScratch.Controls;

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
    // 資格情報の欄はプロバイダーごとに複製せず、選択中モデルのプロバイダーへ切り替える1枚で扱う。
    // そのため「入力途中のキー」「削除指示」「取得元の選択」はプロバイダー別に持ち、
    // パネル切替時に退避・復元する。単一の bool のままだと、Gemini を選んで削除を押し
    // OpenAI へ切り替えて OK を押すと OpenAI のキーが消える。
    private readonly Dictionary<ApiProvider, string> _pendingApiKeys = [];
    private readonly HashSet<ApiProvider> _deleteStoredKeys = [];
    private readonly Dictionary<ApiProvider, ApiKeySource> _pendingKeySources = [];
    private ApiProvider? _shownCredentialProvider;
    private bool _loadingCredentialControls;
    private bool _loadingProofreadingModelControls;
    private bool _settingsApplied;

    // 料金履歴はOKを押すまでメモリ内で編集し、キャンセル時はpricing.jsonへ触れない。
    private readonly Dictionary<string, List<PricingEvent>> _pricingEvents =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<PricingEvent>> _pricingOriginalEvents =
        new(StringComparer.Ordinal);
    private string? _selectedPricingModel;
    private bool _loadingPricingControls;
    private bool _pricingControlsLoaded;
    private bool _discardSettings;

    private sealed record PricingHistoryRow(
        DateOnly EffectiveFrom,
        string EffectiveFromText,
        string SourceText,
        string InputText,
        string InputDeltaText,
        string OutputText,
        string OutputDeltaText,
        string StatusText,
        PricingEvent? UserEvent);

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
        ProofreadingParallelismBox.Text =
            s.ProofreadingParallelism.ToString(CultureInfo.InvariantCulture);
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

        // 取得元の現在値をプロバイダー別に取り込む。以降はこの辞書が編集中の正本になる。
        foreach (ApiProvider provider in Enum.GetValues<ApiProvider>())
            _pendingKeySources[provider] = ApiKeySourceOf(s, provider);

        CustomInstructionBox.Text = s.CustomInstruction;
        StyleGuideAutoGenerateCheck.IsChecked = s.StyleGuideAutoGenerateEnabled;
        StyleGuideThresholdBox.Text =
            s.StyleGuideGenerationThreshold.ToString(CultureInfo.InvariantCulture);
        LoadStyleGuideControls();
        LoadRejectionTrendControls();

        string[] modelNames = ProofreadingModelCatalog.SupportedModels
            .Select(ProofreadingModelCatalog.DisplayName)
            .ToArray();
        _loadingProofreadingModelControls = true;
        AutoProofreadingModelCombo.ItemsSource = modelNames;
        AutoProofreadingModelCombo.SelectedItem =
            ProofreadingModelCatalog.DisplayName(s.AutoProofreadingModel);
        ManualProofreadingModelCombo.ItemsSource = modelNames;
        ManualProofreadingModelCombo.SelectedItem =
            ProofreadingModelCatalog.DisplayName(s.ManualProofreadingModel);
        AutoTimeoutBox.Text =
            s.AutoProofreadingTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ManualTimeoutBox.Text =
            s.ManualProofreadingTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _loadingProofreadingModelControls = false;
        RefreshTimeoutHint();

        // 単価・資格情報パネルは「自動用／手動用のどちらで使うか」をモデルのコンボから読むため、
        // 上のコンボを埋めた後に読み込む。先に呼ぶと SelectedItem が未設定で、使用中バッジが
        // 既定モデル基準の誤った表示になる。
        LoadPricingControls();

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
        s.AutoProofreadingModel =
            SelectedModelId(AutoProofreadingModelCombo) ?? s.AutoProofreadingModel;
        s.ManualProofreadingModel =
            SelectedModelId(ManualProofreadingModelCombo) ?? s.ManualProofreadingModel;
        // 範囲外は SettingsService.Normalize が 5〜300 秒へ丸める。
        s.AutoProofreadingTimeoutSeconds =
            (int)ParseNumber(AutoTimeoutBox.Text, s.AutoProofreadingTimeoutSeconds);
        s.ManualProofreadingTimeoutSeconds =
            (int)ParseNumber(ManualTimeoutBox.Text, s.ManualProofreadingTimeoutSeconds);
        s.ProofreadingDebounceMs =
            (int)ParseNumber(
                ProofreadingDebounceBox.Text,
                s.ProofreadingDebounceMs);
        s.ProofreadingMinimumIntervalSeconds =
            (int)ParseNumber(
                ProofreadingIntervalBox.Text,
                s.ProofreadingMinimumIntervalSeconds);
        s.ProofreadingParallelism =
            (int)ParseNumber(
                ProofreadingParallelismBox.Text,
                s.ProofreadingParallelism);
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

        // 表示中のプロバイダーの選択を辞書へ戻してから、全プロバイダー分を書き出す。
        StashShownCredentialProvider();
        s.GeminiApiKeySource = _pendingKeySources[ApiProvider.Google];
        s.OpenAiApiKeySource = _pendingKeySources[ApiProvider.OpenAi];
        s.AnthropicApiKeySource = _pendingKeySources[ApiProvider.Anthropic];
        s.PlamoApiKeySource = _pendingKeySources[ApiProvider.PreferredNetworks];

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
        bool pricingSaved = false;
        if (_pricingControlsLoaded && _pricingEvents.Count > 0)
        {
            if (!TrySavePricingHistory()) return false;
            pricingSaved = true;
        }
        if (!TryApplyCredentialChanges(updated))
        {
            if (pricingSaved) TryRestoreOriginalPricingHistory();
            return false;
        }
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

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _discardSettings = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _discardSettings = true;
        DialogResult = false;
        Close();
    }

    private void SettingsWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_settingsApplied || _discardSettings) return;
        _discardSettings = true;
    }

    private void CredentialSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCredentialControls) return;
        if (_shownCredentialProvider is { } provider)
        {
            _pendingKeySources[provider] = CredentialSourceCombo.SelectedIndex == 1
                ? ApiKeySource.EnvironmentVariable
                : ApiKeySource.Stored;
        }

        RefreshCredentialStatus();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingCredentialControls) return;
        if (_shownCredentialProvider is { } provider && ApiKeyBox.Password.Length > 0)
            _deleteStoredKeys.Remove(provider);
        RefreshCredentialStatus();
    }

    private void DeleteStoredKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shownCredentialProvider is { } provider) _deleteStoredKeys.Add(provider);
        ApiKeyBox.Clear();
        RefreshCredentialStatus();
    }

    private void ProofreadingModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProofreadingModelControls) return;
        RefreshTimeoutHint();
        RefreshCredentialStatus();
    }

    /// <summary>
    /// 用途ごとの推奨タイムアウトを表示する。**設定値は上書きしない**（モデルを切り替えるたびに
    /// ユーザーが入れた値が黙って消えるのを避けるため。要件 3.5.1）。
    /// </summary>
    private void RefreshTimeoutHint()
    {
        if (TimeoutHintText is null) return;

        ModelDescriptor auto = ProofreadingModelCatalog.Get(
            SelectedModelId(AutoProofreadingModelCombo));
        ModelDescriptor manual = ProofreadingModelCatalog.Get(
            SelectedModelId(ManualProofreadingModelCombo));

        TimeoutHintText.Text =
            $"推奨: 自動 {auto.RecommendedTimeout.TotalSeconds:0} 秒 / " +
            $"手動 {manual.RecommendedTimeout.TotalSeconds:0} 秒（5〜300 秒）。\n" +
            "1 回の自動校正は段落ごとに分割し、同時送信数ずつまとめて送ります。実行時間の目安は" +
            "「タイムアウト × まとめて送る回数（バッチ数）」で、まとめて送る回数は" +
            "「分割数 ÷ 同時送信数」を切り上げた整数（最低 1 回）です。ただし、2,000 文字を超える" +
            "段落の複数パートは同一段落内で順番待ちになるため、最大ではまとめて送る回数が" +
            "分割数と同じになることがあります。さらに、実行開始時には最小送信間隔の待ちが" +
            "加わる場合があります。レート制限や一時的な通信エラーで 1 回だけ再送したバッチには、" +
            "タイムアウト 1 回分と 1〜5 秒の待ちが加わります。" +
            "入力中の自動校正には応答の速いモデルをおすすめします。";
    }

    private static string? SelectedModelId(System.Windows.Controls.ComboBox combo)
        => combo.SelectedItem is not string displayName
            ? null
            : ProofreadingModelCatalog.SupportedModels
                .FirstOrDefault(model =>
                    ProofreadingModelCatalog.DisplayName(model) == displayName);

    private static ApiKeySource ApiKeySourceOf(AppSettings s, ApiProvider provider)
        => provider switch
        {
            ApiProvider.Google => s.GeminiApiKeySource,
            ApiProvider.OpenAi => s.OpenAiApiKeySource,
            ApiProvider.Anthropic => s.AnthropicApiKeySource,
            ApiProvider.PreferredNetworks => s.PlamoApiKeySource,
            _ => ApiKeySource.Unspecified,
        };

    /// <summary>表示中のプロバイダーの入力内容を辞書へ退避する。パネルを切り替える**前**に呼ぶ。</summary>
    private void StashShownCredentialProvider()
    {
        if (_shownCredentialProvider is not { } provider) return;

        _pendingApiKeys[provider] = ApiKeyBox.Password;
        _pendingKeySources[provider] = CredentialSourceCombo.SelectedIndex == 1
            ? ApiKeySource.EnvironmentVariable
            : ApiKeySource.Stored;
    }

    private void RefreshCredentialStatus()
    {
        if (CredentialStatusText is null || _shownCredentialProvider is not { } provider) return;

        string stored = _deleteStoredKeys.Contains(provider)
            ? "保存済みキー: OKを押すと削除"
            : _credentials.StoredKeyState(provider) switch
            {
                StoredCredentialState.Available => "保存済みキー: あり（値は表示しません）",
                StoredCredentialState.Unreadable => "保存済みキー: 読み取れません（削除または置き換えが必要です）",
                _ => "保存済みキー: なし",
            };

        string variable = ProofreadingModelCatalog.EnvironmentVariableName(provider);
        string environment = _credentials.EnvironmentKeyAvailable(provider)
            ? $"環境変数 {variable}: 検出済み"
            : $"環境変数 {variable}: 見つかりません";

        string pending = ApiKeyBox?.Password.Length > 0
            ? "\n新しいキー: OKを押すと暗号化して保存"
            : "";

        CredentialStatusText.Text = $"{stored}\n{environment}{pending}";
        RefreshCredentialUsage(provider);
    }

    /// <summary>
    /// このプロバイダーのキーがどちらの校正で必要になるかを示す（要件 3.5.5）。
    /// 「キーが未設定です」だけでは、どちらの校正が止まるのか判断できないため。
    /// </summary>
    private void RefreshCredentialUsage(ApiProvider provider)
    {
        if (CredentialUsageText is null) return;

        bool usedByAuto = ProofreadingModelCatalog.ProviderOf(
            SelectedModelId(AutoProofreadingModelCombo)) == provider;
        bool usedByManual = ProofreadingModelCatalog.ProviderOf(
            SelectedModelId(ManualProofreadingModelCombo)) == provider;

        string name = ProofreadingModelCatalog.ProviderDisplayName(provider);
        CredentialUsageText.Text = (usedByAuto, usedByManual) switch
        {
            (true, true) => $"{name} のキーは自動校正・手動校正の両方で使います。",
            (true, false) => $"{name} のキーは自動校正（入力中・別案生成）で使います。",
            (false, true) => $"{name} のキーは手動校正（Ctrl+Enter・スタイルガイド生成）で使います。",
            _ => $"{name} のキーは現在どちらの校正でも使っていません。",
        };
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
        _pricingEvents.Clear();
        _pricingOriginalEvents.Clear();
        foreach ((string model, IReadOnlyList<PricingEvent> events) in
                 _pricing.SnapshotUserEvents())
        {
            PricingEvent[] snapshot = events.ToArray();
            _pricingEvents[model] = snapshot.ToList();
            _pricingOriginalEvents[model] = snapshot;
        }

        List<string> models = _pricing.Snapshot().Keys
            .Where(model => model != PricingService.DefaultModel)
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToList();
        if (_pricingEvents.ContainsKey(PricingService.DefaultModel))
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

        StashShownCredentialProvider();
        _selectedPricingModel = PricingModelCombo.SelectedItem as string;
        ShowSelectedPricingModel();
    }

    private void ShowSelectedPricingModel()
    {
        UpdateCredentialPanelVisibility();
        PricingHistoryList.ItemsSource = null;
        PricingEditButton.IsEnabled = false;
        PricingDeleteButton.IsEnabled = false;
        PricingRestoreButton.IsEnabled =
            _selectedPricingModel is not null &&
            ProofreadingModelCatalog.IsSupported(_selectedPricingModel);

        if (_selectedPricingModel is null ||
            !_pricingEvents.ContainsKey(_selectedPricingModel))
        {
            PricingCurrentSummaryText.Text = "";
            PricingHistoryChart.SetData([], PricingCurrency.Usd);
            return;
        }

        string model = _selectedPricingModel;
        List<PricingHistoryRow> rows = BuildPricingHistoryRows(model);
        PricingHistoryList.ItemsSource = rows;

        string currency = CurrencyForModel(model);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (TryResolveStagedPricing(model, today, out ModelPricing? current))
        {
            PricingCurrentSummaryText.Text =
                $"現在: 入力 {FormatPrice(current!.InputUsdPerMillion, currency)} / " +
                $"出力 {FormatPrice(current.OutputUsdPerMillion, currency)}";
        }
        else
        {
            PricingCurrentSummaryText.Text = "現在の単価は未設定です";
        }

        DateOnly[] dates = rows.Select(row => row.EffectiveFrom).Distinct().Order().ToArray();
        var chartPoints = new List<PricingChartPoint>();
        foreach (DateOnly date in dates)
        {
            if (TryResolveStagedPricing(model, date, out ModelPricing? price))
            {
                chartPoints.Add(new PricingChartPoint(
                    date,
                    price!.InputUsdPerMillion,
                    price.OutputUsdPerMillion));
            }
        }
        PricingHistoryChart.SetData(chartPoints, currency);
    }

    /// <summary>
    /// 選択中モデルのプロバイダーへ資格情報パネルを切り替える。
    /// **退避（<see cref="StashShownCredentialProvider"/>）は切り替えの前に済ませておくこと**。
    /// 順序を誤ると、入力途中のキーが別プロバイダーのスロットへ入る。
    /// </summary>
    private void UpdateCredentialPanelVisibility()
    {
        if (_selectedPricingModel is null)
        {
            CredentialPanel.Visibility = Visibility.Collapsed;
            _shownCredentialProvider = null;
            return;
        }

        ApiProvider provider = ProofreadingModelCatalog.ProviderOf(_selectedPricingModel);
        CredentialPanel.Visibility = Visibility.Visible;
        _shownCredentialProvider = provider;

        _loadingCredentialControls = true;
        CredentialSourceLabel.Text =
            $"{ProofreadingModelCatalog.ProviderDisplayName(provider)} APIキーの取得元";
        CredentialSourceCombo.ItemsSource = new[]
        {
            "アプリに保存したキー",
            $"環境変数 {ProofreadingModelCatalog.EnvironmentVariableName(provider)}",
        };
        CredentialSourceCombo.SelectedIndex =
            _pendingKeySources.TryGetValue(provider, out ApiKeySource source) &&
            source == ApiKeySource.EnvironmentVariable
                ? 1
                : 0;
        ApiKeyBox.Password = _pendingApiKeys.TryGetValue(provider, out string? pending)
            ? pending
            : "";
        _loadingCredentialControls = false;

        RefreshCredentialStatus();
    }

    private List<PricingHistoryRow> BuildPricingHistoryRows(string model)
    {
        var items = new List<(
            DateOnly Date,
            bool IsCatalog,
            bool IsPromotional,
            PricingEvent? UserEvent,
            decimal Input,
            decimal Output,
            string Currency,
            bool IsEffective)>();

        if (ProofreadingModelCatalog.TryGet(model, out ModelDescriptor descriptor))
        {
            foreach (CatalogPricingHistoryEntry catalog in descriptor.PricingHistory())
            {
                PricingEvent? controlling = _pricingEvents[model]
                    .LastOrDefault(entry => entry.EffectiveFrom <= catalog.EffectiveFrom);
                bool effective = controlling is null ||
                    controlling.Type == PricingEventType.UseCatalog;
                items.Add((
                    catalog.EffectiveFrom,
                    IsCatalog: true,
                    catalog.IsPromotional,
                    UserEvent: null,
                    catalog.InputPricePerMillion,
                    catalog.OutputPricePerMillion,
                    catalog.Currency,
                    effective));
            }
        }

        foreach (PricingEvent userEvent in _pricingEvents[model])
        {
            decimal input = userEvent.InputPricePerMillion ?? 0m;
            decimal output = userEvent.OutputPricePerMillion ?? 0m;
            string currency = CurrencyForModel(model);
            if (userEvent.Type == PricingEventType.UseCatalog &&
                TryResolveStagedPricing(model, userEvent.EffectiveFrom, out ModelPricing? restored))
            {
                input = restored!.InputUsdPerMillion;
                output = restored.OutputUsdPerMillion;
            }
            items.Add((
                userEvent.EffectiveFrom,
                IsCatalog: false,
                IsPromotional: false,
                userEvent,
                input,
                output,
                currency,
                IsEffective: true));
        }

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly? currentDate = items
            .Where(item => item.IsEffective && item.Date <= today)
            .Select(item => (DateOnly?)item.Date)
            .Max();
        var rows = new List<PricingHistoryRow>();
        foreach (var item in items.OrderBy(item => item.Date).ThenBy(item => item.IsCatalog ? 0 : 1))
        {
            string source = item.UserEvent?.Type switch
            {
                PricingEventType.Override => "ユーザー設定",
                PricingEventType.UseCatalog => "公式へ復帰",
                _ when item.IsPromotional => "公式（期間限定）",
                _ => "公式（通常）",
            };
            string inputDelta = "—";
            string outputDelta = "—";
            if (!item.IsEffective)
            {
                inputDelta = "非適用";
                outputDelta = "非適用";
            }
            else if (item.Date != DateOnly.MinValue &&
                     TryResolveStagedPricing(model, item.Date.AddDays(-1), out ModelPricing? before))
            {
                inputDelta = FormatDelta(item.Input, before!.InputUsdPerMillion, item.Currency);
                outputDelta = FormatDelta(item.Output, before.OutputUsdPerMillion, item.Currency);
            }

            string status = !item.IsEffective ? "非適用"
                : item.Date > today ? "予約"
                : item.Date == currentDate ? "現在"
                : "過去";
            rows.Add(new PricingHistoryRow(
                item.Date,
                item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                source,
                FormatPrice(item.Input, item.Currency),
                inputDelta,
                FormatPrice(item.Output, item.Currency),
                outputDelta,
                status,
                item.UserEvent));
        }
        return rows;
    }

    private bool TryResolveStagedPricing(
        string model,
        DateOnly date,
        out ModelPricing? pricing)
    {
        PricingEvent? userEvent = _pricingEvents[model]
            .LastOrDefault(entry => entry.EffectiveFrom <= date);
        string currency = CurrencyForModel(model);
        if (userEvent is { Type: PricingEventType.Override })
        {
            pricing = new ModelPricing
            {
                Currency = currency,
                InputUsdPerMillion = userEvent.InputPricePerMillion!.Value,
                OutputUsdPerMillion = userEvent.OutputPricePerMillion!.Value,
                UpdatedAt = userEvent.EffectiveFrom.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
            };
            return true;
        }

        if (ProofreadingModelCatalog.TryGet(model, out ModelDescriptor descriptor))
        {
            EffectiveModelPricing catalog = descriptor.PricingFor(date);
            pricing = new ModelPricing
            {
                Currency = catalog.Currency,
                InputUsdPerMillion = catalog.InputPricePerMillion,
                OutputUsdPerMillion = catalog.OutputPricePerMillion,
                UpdatedAt = catalog.PricingUpdatedAt,
                CatalogManaged = true,
            };
            return true;
        }

        pricing = null;
        return false;
    }

    private string CurrencyForModel(string model)
    {
        if (ProofreadingModelCatalog.TryGet(model, out ModelDescriptor descriptor))
            return descriptor.Currency;
        return _pricing.GetPricing(model).Currency;
    }

    private static string FormatPrice(decimal value, string currency)
    {
        string symbol = currency == PricingCurrency.Jpy ? "¥" : "$";
        return symbol + SettingsFieldFormatting.FormatUnitPrice(value);
    }

    private static string FormatDelta(decimal current, decimal previous, string currency)
    {
        decimal difference = current - previous;
        string sign = difference > 0 ? "+" : "";
        string amount = sign + FormatPrice(difference, currency);
        if (previous == 0m) return amount + " (—)";
        decimal percent = difference / previous * 100m;
        string percentSign = percent > 0 ? "+" : "";
        return $"{amount} ({percentSign}{percent:0.#}%)";
    }

    private void PricingHistoryList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        bool editable = PricingHistoryList.SelectedItem is PricingHistoryRow
        {
            UserEvent: not null,
        };
        PricingEditButton.IsEnabled = editable;
        PricingDeleteButton.IsEnabled = editable;
        PricingSelectionHelpText.Text = editable
            ? "選択したユーザー履歴を編集または削除できます。"
            : "公式価格は読み取り専用です。ユーザー設定の行を選ぶと編集・削除できます。";
    }

    private void PricingAddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPricingModel is not string model) return;
        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
        TryResolveStagedPricing(model, date, out ModelPricing? current);
        var dialog = new PricingHistoryEditDialog(
            ProofreadingModelCatalog.DisplayName(model),
            CurrencyForModel(model),
            useCatalog: false,
            effectiveFrom: date,
            inputPrice: current?.InputUsdPerMillion,
            outputPrice: current?.OutputUsdPerMillion)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true) return;
        UpsertPricingEvent(
            model,
            new PricingEvent(
                dialog.EffectiveFrom,
                PricingEventType.Override,
                dialog.InputPricePerMillion,
                dialog.OutputPricePerMillion),
            original: null);
    }

    private void PricingRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPricingModel is not string model ||
            !ProofreadingModelCatalog.IsSupported(model))
            return;
        var dialog = new PricingHistoryEditDialog(
            ProofreadingModelCatalog.DisplayName(model),
            CurrencyForModel(model),
            useCatalog: true)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true) return;
        UpsertPricingEvent(
            model,
            new PricingEvent(dialog.EffectiveFrom, PricingEventType.UseCatalog),
            original: null);
    }

    private void PricingEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPricingModel is not string model ||
            PricingHistoryList.SelectedItem is not PricingHistoryRow
            {
                UserEvent: PricingEvent original,
            })
            return;

        bool useCatalog = original.Type == PricingEventType.UseCatalog;
        var dialog = new PricingHistoryEditDialog(
            ProofreadingModelCatalog.DisplayName(model),
            CurrencyForModel(model),
            useCatalog,
            original.EffectiveFrom,
            original.InputPricePerMillion,
            original.OutputPricePerMillion)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true) return;
        PricingEvent updated = useCatalog
            ? new PricingEvent(dialog.EffectiveFrom, PricingEventType.UseCatalog)
            : new PricingEvent(
                dialog.EffectiveFrom,
                PricingEventType.Override,
                dialog.InputPricePerMillion,
                dialog.OutputPricePerMillion);
        UpsertPricingEvent(model, updated, original);
    }

    private void PricingDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPricingModel is not string model ||
            PricingHistoryList.SelectedItem is not PricingHistoryRow
            {
                UserEvent: PricingEvent selected,
            })
            return;

        MessageBoxResult result = MessageBox.Show(
            this,
            $"{selected.EffectiveFrom:yyyy-MM-dd}のユーザー料金履歴を削除します。過去を含む実効価格の推移が再計算されます。",
            "JP Scratch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        _pricingEvents[model].Remove(selected);
        PricingErrorText.Visibility = Visibility.Collapsed;
        ShowSelectedPricingModel();
    }

    private void UpsertPricingEvent(
        string model,
        PricingEvent updated,
        PricingEvent? original)
    {
        List<PricingEvent> events = _pricingEvents[model];
        if (events.Any(entry =>
                !ReferenceEquals(entry, original) &&
                entry.EffectiveFrom == updated.EffectiveFrom))
        {
            PricingErrorText.Text =
                $"{updated.EffectiveFrom:yyyy-MM-dd}には既にユーザー料金履歴があります。既存の行を編集してください。";
            PricingErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (original is not null) events.Remove(original);
        events.Add(updated);
        events.Sort((left, right) => left.EffectiveFrom.CompareTo(right.EffectiveFrom));
        PricingErrorText.Visibility = Visibility.Collapsed;
        ShowSelectedPricingModel();
    }

    private bool TrySavePricingHistory()
    {
        try
        {
            var snapshot = _pricingEvents.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<PricingEvent>)pair.Value.ToArray(),
                StringComparer.Ordinal);
            _pricing.ReplaceAllUserEvents(snapshot);
            PricingErrorText.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (InvalidDataException ex)
        {
            PricingErrorText.Text = ex.Message;
            PricingErrorText.Visibility = Visibility.Visible;
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
    }

    private void TryRestoreOriginalPricingHistory()
    {
        try
        {
            _pricing.ReplaceAllUserEvents(_pricingOriginalEvents);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(
                this,
                "APIキーの保存に失敗し、先に保存したモデル料金も元へ戻せませんでした。料金履歴を確認してください。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool TryApplyCredentialChanges(AppSettings updated)
    {
        // 表示中の入力を辞書へ戻してから、全プロバイダー分をまとめて適用する。
        StashShownCredentialProvider();

        // 環境変数を選んだのに実在しないプロバイダーがあれば、そこで止める。ただし、そのキーを
        // 実際に使うプロバイダー（自動用・手動用のモデル）だけを対象にする。使っていない
        // プロバイダーの設定で OK が押せなくなるのは筋が悪い。
        ApiProvider[] inUse =
        [
            ProofreadingModelCatalog.ProviderOf(updated.AutoProofreadingModel),
            ProofreadingModelCatalog.ProviderOf(updated.ManualProofreadingModel),
        ];

        foreach (ApiProvider provider in inUse.Distinct())
        {
            if (ApiKeySourceOf(updated, provider) != ApiKeySource.EnvironmentVariable ||
                _credentials.EnvironmentKeyAvailable(provider))
            {
                continue;
            }

            MessageBox.Show(
                this,
                $"環境変数 {ProofreadingModelCatalog.EnvironmentVariableName(provider)} が見つかりません。",
                "JP Scratch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            foreach (ApiProvider provider in Enum.GetValues<ApiProvider>())
            {
                if (_pendingApiKeys.TryGetValue(provider, out string? key) && key.Length > 0)
                {
                    _credentials.SaveStoredApiKey(provider, key);
                }
                else if (_deleteStoredKeys.Contains(provider))
                {
                    _credentials.DeleteStoredApiKey(provider);
                }
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
