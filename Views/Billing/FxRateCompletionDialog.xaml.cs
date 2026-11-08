using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using JpScratch.Services;

namespace JpScratch.Views;

internal sealed class FxRateCompletionItem : INotifyPropertyChanged
{
    private const decimal MinimumManualRate = 1m;
    private const decimal MaximumManualRate = 100000m;
    private string _rateText = "";
    private FxRate? _apiRate;

    internal FxRateCompletionItem(UnconfirmedFxDateSummary summary)
    {
        CalledDate = summary.CalledDate;
        CompletableCount = summary.CompletableCount;
        UncompletableCount = summary.UncompletableCount;
    }

    public DateOnly CalledDate { get; }
    public int CompletableCount { get; }
    public int UncompletableCount { get; }
    public string CalledDateText => CalledDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string CompletableCountText => CompletableCount.ToString("N0", CultureInfo.InvariantCulture) + "件";
    public string UncompletableCountText => UncompletableCount == 0
        ? "—"
        : UncompletableCount.ToString("N0", CultureInfo.InvariantCulture) + "件";

    public string RateText
    {
        get => _rateText;
        set
        {
            if (string.Equals(_rateText, value, StringComparison.Ordinal)) return;
            _rateText = value;
            _apiRate = null;
            OnPropertyChanged();
        }
    }

    internal FxRate? GetRate()
    {
        if (_apiRate is not null)
            return _apiRate;

        if (!decimal.TryParse(
                RateText,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal rate) ||
            rate < MinimumManualRate ||
            rate > MaximumManualRate)
        {
            return null;
        }

        return new FxRate(CalledDate, rate, DateTimeOffset.Now);
    }

    internal void SetApiRate(FxRate rate)
    {
        _rateText = rate.UsdJpy.ToString("0.################", CultureInfo.InvariantCulture);
        _apiRate = rate;
        OnPropertyChanged(nameof(RateText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>未確認の円建て課金を、API取得または手動レートで補完するダイアログ。</summary>
public partial class FxRateCompletionDialog : Window
{
    private readonly ApiCallRepository _apiCalls;
    private readonly FxRateService _fxRates;
    private bool _busy;

    internal FxRateCompletionDialog(
        ApiCallRepository apiCalls,
        FxRateService fxRates)
    {
        _apiCalls = apiCalls ?? throw new ArgumentNullException(nameof(apiCalls));
        _fxRates = fxRates ?? throw new ArgumentNullException(nameof(fxRates));
        InitializeComponent();

        IReadOnlyList<UnconfirmedFxDateSummary> summaries = _apiCalls.GetUnconfirmedFxDateSummaries();
        var items = new ObservableCollection<FxRateCompletionItem>(
            summaries.Select(summary => new FxRateCompletionItem(summary)));
        RatesList.ItemsSource = items;
        int completable = summaries.Sum(item => item.CompletableCount);
        int uncompletable = summaries.Sum(item => item.UncompletableCount);
        SummaryText.Text =
            $"補完対象 {completable:N0}件　レートでは直せない行 {uncompletable:N0}件（元通貨額なし）";
        if (completable == 0)
        {
            FetchButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            MessageText.Text = "元通貨額がないため、為替レートでは補完できる行がありません。";
        }
    }

    private async void FetchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        FetchButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        MessageText.Text = "APIへ問い合わせています…";
        try
        {
            int fetchedCount = 0;
            foreach (FxRateCompletionItem item in RatesList.Items.OfType<FxRateCompletionItem>())
            {
                if (item.CompletableCount == 0) continue;
                FxRate? rate = await _fxRates.FetchForDateAsync(item.CalledDate);
                if (rate is null) continue;
                item.SetApiRate(rate);
                fetchedCount++;
            }

            MessageText.Text =
                $"{fetchedCount:N0}日分を取得しました。空欄の日付は取得できないため、レートを入力してください。";
        }
        finally
        {
            _busy = false;
            FetchButton.IsEnabled = RatesList.Items.OfType<FxRateCompletionItem>()
                .Any(item => item.CompletableCount > 0);
            ApplyButton.IsEnabled = true;
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var rates = new Dictionary<DateOnly, FxRate>();
        foreach (FxRateCompletionItem item in RatesList.Items.OfType<FxRateCompletionItem>())
        {
            if (item.CompletableCount == 0 || string.IsNullOrWhiteSpace(item.RateText))
                continue;

            FxRate? rate = item.GetRate();
            if (rate is null)
            {
                MessageText.Text =
                    $"{item.CalledDateText} のレートは、1以上100000以下の数値で入力してください。";
                return;
            }

            rates[item.CalledDate] = rate;
        }

        if (rates.Count == 0)
        {
            MessageText.Text = "適用するレートを1件以上入力してください。";
            return;
        }

        FxRateCompletionResult result = _apiCalls.ApplyFxRates(rates);
        if (result.CompletedCount == 0)
        {
            MessageText.Text = result.UncompletableCount > 0
                ? $"補完できる行はありません。レートでは直せない行が{result.UncompletableCount:N0}件あります（元通貨額なし）。"
                : "補完できる未確認行はありません。内容を更新してから再度お試しください。";
            return;
        }

        MessageText.Text = $"{result.CompletedCount:N0}件を補完しました。";
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        DialogResult = false;
        e.Handled = true;
    }
}
