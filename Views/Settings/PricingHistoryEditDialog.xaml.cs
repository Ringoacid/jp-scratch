using System.Globalization;
using System.Windows;
using JpScratch.Services;

namespace JpScratch.Views;

public partial class PricingHistoryEditDialog : Window
{
    private readonly bool _useCatalog;

    internal DateOnly EffectiveFrom { get; private set; }
    internal decimal InputPricePerMillion { get; private set; }
    internal decimal OutputPricePerMillion { get; private set; }

    internal PricingHistoryEditDialog(
        string modelName,
        string currency,
        bool useCatalog,
        DateOnly? effectiveFrom = null,
        decimal? inputPrice = null,
        decimal? outputPrice = null)
    {
        _useCatalog = useCatalog;
        InitializeComponent();

        string unit = $"{currency} / 1M tokens";
        InputUnitText.Text = unit;
        OutputUnitText.Text = unit + "（thinkingを含む）";
        EffectiveDatePicker.SelectedDate =
            (effectiveFrom ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToDateTime(TimeOnly.MinValue);
        InputPriceBox.Text = inputPrice?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";
        OutputPriceBox.Text = outputPrice?.ToString("0.########", CultureInfo.InvariantCulture) ?? "";

        if (useCatalog)
        {
            Title = "公式価格へ戻す";
            DescriptionText.Text = $"{modelName}を、指定日から公式価格へ戻します。過去のユーザー価格履歴は残ります。";
            InputPriceRow.Visibility = Visibility.Collapsed;
            OutputPriceRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            DescriptionText.Text = $"{modelName}のユーザー価格を追加または編集します。指定日を含めて適用されます。";
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Visibility = Visibility.Collapsed;
        if (EffectiveDatePicker.SelectedDate is not DateTime selected)
        {
            ShowError("適用開始日を入力してください。");
            EffectiveDatePicker.Focus();
            return;
        }

        EffectiveFrom = DateOnly.FromDateTime(selected);
        if (!_useCatalog)
        {
            if (!TryParsePrice(InputPriceBox.Text, out decimal input))
            {
                ShowError($"入力単価は0以上{PricingService.MaxUnitPriceUsdPerMillion:#,0}以下の数値で入力してください。");
                InputPriceBox.Focus();
                return;
            }

            if (!TryParsePrice(OutputPriceBox.Text, out decimal output))
            {
                ShowError($"出力単価は0以上{PricingService.MaxUnitPriceUsdPerMillion:#,0}以下の数値で入力してください。");
                OutputPriceBox.Focus();
                return;
            }

            InputPricePerMillion = input;
            OutputPricePerMillion = output;
        }

        DialogResult = true;
    }

    private static bool TryParsePrice(string text, out decimal price)
        => decimal.TryParse(
               text.Trim(),
               NumberStyles.AllowDecimalPoint,
               CultureInfo.InvariantCulture,
               out price) &&
           price >= 0 &&
           price <= PricingService.MaxUnitPriceUsdPerMillion;

    private void ShowError(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
