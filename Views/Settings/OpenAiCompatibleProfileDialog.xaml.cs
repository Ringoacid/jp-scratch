using System.Globalization;
using System.Windows;
using JpScratch.Models;

namespace JpScratch.Views;

public partial class OpenAiCompatibleProfileDialog : Window
{
    private readonly OpenAiCompatibleProfile? _original;
    internal OpenAiCompatibleProfile? Profile { get; private set; }
    internal string NewApiKey { get; private set; } = "";
    internal bool DeleteStoredKey { get; private set; }

    internal OpenAiCompatibleProfileDialog(OpenAiCompatibleProfile? profile, bool hasStoredKey)
    {
        _original = profile;
        InitializeComponent();
        NameBox.Text = profile?.Name ?? "";
        EndpointBox.Text = profile?.EndpointUrl ?? "";
        ModelBox.Text = profile?.ModelId ?? "";
        InputPriceBox.Text = profile?.InputUsdPerMillion.ToString(CultureInfo.InvariantCulture) ?? "";
        OutputPriceBox.Text = profile?.OutputUsdPerMillion.ToString(CultureInfo.InvariantCulture) ?? "";
        DeleteKeyCheck.Visibility = hasStoredKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(InputPriceBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
                out decimal input) ||
            !decimal.TryParse(OutputPriceBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
                out decimal output))
        {
            ShowError("入力・出力単価はUSDの数値で入力してください。");
            return;
        }

        var profile = new OpenAiCompatibleProfile
        {
            Id = _original?.Id ?? Guid.NewGuid().ToString("N"),
            Name = NameBox.Text.Trim(),
            EndpointUrl = EndpointBox.Text.Trim(),
            ModelId = ModelBox.Text.Trim(),
            InputUsdPerMillion = input,
            OutputUsdPerMillion = output,
            PricingUpdatedAt = _original is { } old &&
                old.InputUsdPerMillion == input && old.OutputUsdPerMillion == output
                    ? old.PricingUpdatedAt
                    : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };
        if (!OpenAiCompatibleProfile.IsValid(profile))
        {
            ShowError("表示名・モデルID・URL・単価を確認してください。URLは /chat/completions で終わる完全なhttp(s)アドレス、単価は0以上で入力します。");
            return;
        }
        if (DeleteKeyCheck.IsChecked == true && ApiKeyBox.Password.Length > 0)
        {
            ShowError("キーの入力と削除は同時に指定できません。");
            return;
        }

        Profile = profile;
        NewApiKey = ApiKeyBox.Password.Trim();
        DeleteStoredKey = DeleteKeyCheck.IsChecked == true;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
