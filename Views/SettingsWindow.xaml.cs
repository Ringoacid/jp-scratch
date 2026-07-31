using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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
    private readonly CredentialService _credentials;
    private readonly PricingService _pricing;
    private bool _deleteStoredKey;
    private bool _deleteOpenAiStoredKey;
    private bool _loadingCredentialControls;
    private bool _loadingOpenAiCredentialControls;

    // モデル単価編集用。入力途中の値はモデルごとに「生テキスト」で保持し、検証はOK押下時にまとめて
    // 行う（コンボを切り替えるたびに検証して選択を巻き戻すような作りにしない）。
    private Dictionary<string, ModelPricing> _pricingOriginal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Input, string Output, string UpdatedAt)> _pricingText =
        new(StringComparer.Ordinal);
    private string? _selectedPricingModel;
    private bool _loadingPricingControls;

    internal SettingsWindow(SettingsService service, CredentialService credentials, PricingService pricing)
    {
        _service = service;
        _credentials = credentials;
        _pricing = pricing;
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

        _loadingCredentialControls = true;
        CredentialSourceCombo.ItemsSource = new[]
        {
            "アプリに保存したキー",
            $"環境変数 {CredentialService.EnvironmentVariableName}",
        };
        CredentialSourceCombo.SelectedIndex =
            s.GeminiApiKeySource == GeminiApiKeySource.EnvironmentVariable ? 1 : 0;
        _loadingCredentialControls = false;
        RefreshCredentialStatus();

        _loadingOpenAiCredentialControls = true;
        OpenAiCredentialSourceCombo.ItemsSource = new[]
        {
            "アプリに保存したキー",
            $"環境変数 {CredentialService.OpenAiEnvironmentVariableName}",
        };
        OpenAiCredentialSourceCombo.SelectedIndex =
            s.OpenAiApiKeySource == GeminiApiKeySource.EnvironmentVariable ? 1 : 0;
        _loadingOpenAiCredentialControls = false;
        RefreshOpenAiCredentialStatus();

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

        s.GeminiApiKeySource = CredentialSourceCombo.SelectedIndex == 1
            ? GeminiApiKeySource.EnvironmentVariable
            : GeminiApiKeySource.Stored;
        s.OpenAiApiKeySource = OpenAiCredentialSourceCombo.SelectedIndex == 1
            ? GeminiApiKeySource.EnvironmentVariable
            : GeminiApiKeySource.Stored;

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

        // 副作用（credentials.dat / pricing.json への書き込み）より前に、入力の検証をすべて済ませる。
        // 検証エラーで戻るときに「片方だけ書き込み済み」という中途半端な状態を作らない
        // （例: 単価欄の入力ミスで戻る前にAPIキーだけ credentials.dat へ書き込まれてしまう、等）。
        if (!TryBuildPricingTable(out Dictionary<string, ModelPricing> pricingTable)) return;
        if (!TryApplyCredentialChanges(updated)) return;
        if (!TrySavePricing(pricingTable)) return;
        _service.Replace(updated);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => Close();

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
        if (updated.GeminiApiKeySource == GeminiApiKeySource.EnvironmentVariable &&
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

        if (updated.OpenAiApiKeySource == GeminiApiKeySource.EnvironmentVariable &&
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
