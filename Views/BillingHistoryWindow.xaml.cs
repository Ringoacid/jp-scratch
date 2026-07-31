using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JpScratch.Services;

namespace JpScratch.Views;

/// <summary>課金履歴画面の明細1行の表示用モデル（要件 3.6.2）。値はすべて表示済み文字列。</summary>
internal sealed record BillingHistoryDisplayRow(
    long Id,
    string CalledAt,
    string Trigger,
    string Model,
    string PromptTokens,
    string OutputTokens,
    string Usd,
    string Jpy,
    string RateDate,
    string Duration,
    string Status,
    string SuggestionCount,
    string DiscardedCount,
    string? ErrorMessage);

/// <summary>
/// 課金履歴画面（要件 3.1.1 / 3.6.2）。期間・種別フィルタで絞った <see cref="ApiCallRepository"/> の
/// 明細と集計を表示する。表示書式は <see cref="UsageFormatting"/> を経由して
/// ステータスバー下段（<c>MainWindow.RefreshUsageDisplay</c>）と完全に揃える。
/// </summary>
public partial class BillingHistoryWindow : Window
{
    private enum PeriodOption
    {
        Today,
        ThisWeek,
        ThisMonth,
        AllTime,
        Custom,
    }

    private readonly ApiCallRepository _apiCalls;

    // コンストラクタ中の初期値設定でフィルタ再読込を連鎖させないためのガード。
    private bool _initializing;

    internal BillingHistoryWindow(ApiCallRepository apiCalls)
    {
        // InitializeComponent() より前に立てる。種別チェックボックスは XAML で IsChecked="True" と
        // 書いてあるため、BAML読み込み中（InitializeComponent() の実行中）に Checked イベントが
        // 同期的に発火する。このとき ValidationErrorText などの名前付きフィールドはまだ
        // このインスタンスへ配線されておらず（XAML内でチェックボックスより後に出てくるため）、
        // ガードがここで false のままだと TriggerCheck_Changed → LoadHistory() が
        // null 参照で落ちる（実機で確認済みのクラッシュ）。
        _initializing = true;
        _apiCalls = apiCalls ?? throw new ArgumentNullException(nameof(apiCalls));
        InitializeComponent();

        PeriodCombo.ItemsSource = new[] { "当日", "当週", "当月", "全期間", "カスタム" };
        PeriodCombo.SelectedIndex = (int)PeriodOption.ThisMonth;

        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        // 暫定値。直後の UpdateCustomRangeDisplay() が既定プリセット（当月）の実際の範囲へ
        // 書き戻すので、ここでの30日固定範囲は一瞬も表示されない。
        CustomFromBox.Text = today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        CustomToBox.Text = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _initializing = false;

        UpdateCustomRangeEnabled();
        UpdateCustomRangeDisplay();
        LoadHistory();
    }

    /// <summary>既に開いている画面を再アクティブ化したときの再取得。呼び出し元は MainWindow。</summary>
    public void Refresh() => LoadHistory();

    private void PeriodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        UpdateCustomRangeEnabled();
        UpdateCustomRangeDisplay();
        LoadHistory();
    }

    private void UpdateCustomRangeEnabled()
    {
        bool isCustom = (PeriodOption)PeriodCombo.SelectedIndex == PeriodOption.Custom;
        CustomFromBox.IsEnabled = isCustom;
        CustomToBox.IsEnabled = isCustom;
    }

    /// <summary>
    /// プリセット（当日/当週/当月）選択時に、実際にクエリへ渡す範囲をカスタム欄へ書き戻す。
    /// これをしないと「当月」を選んでいるのにカスタム欄が初期値の固定30日レンジのままになり、
    /// 実際の集計期間と表示が食い違って見える（レビュー指摘の再現バグ）。
    /// 「カスタム」選択時は、ユーザー入力（または他プリセットから書き戻された値）が既にあれば
    /// 上書きしない。「全期間」は単一の日付範囲で表現できないため欄を空にする
    /// （直前の表示を残すと「全期間を選んでいるのに特定の日付が出ている」という、
    /// 当月のときと同種の誤読を招く）。
    /// クエリが使う排他的な終了日時（翌日/翌月1日 00:00）は、カスタム欄の「終了日は含む」規約に
    /// 合わせて <see cref="CustomDateRangeParser.FormatInclusive"/> で変換してから書き戻す。
    /// </summary>
    private void UpdateCustomRangeDisplay()
    {
        var option = (PeriodOption)PeriodCombo.SelectedIndex;

        if (option == PeriodOption.AllTime)
        {
            CustomFromBox.Text = "";
            CustomToBox.Text = "";
            return;
        }

        if (option == PeriodOption.Custom)
        {
            FillCustomRangeDefaultIfBlank();
            return;
        }

        (DateTimeOffset? From, DateTimeOffset? To)? range = ComputeRange(out _, out _);
        if (range is null || range.Value.From is null || range.Value.To is null) return;

        SetCustomRangeBoxes(range.Value.From.Value, range.Value.To.Value);
    }

    /// <summary>
    /// 「全期間」から空欄のまま「カスタム」へ切り替えた直後にそのまま検索すると、
    /// 「日付はyyyy-MM-dd形式で入力してください」という入力エラーがいきなり出て面食らう
    /// （レビュー指摘）。未入力は「まだ選んでいない」中立状態として扱い、当月を初期値にする。
    /// 既に値がある（他プリセットから書き戻された、またはユーザーが入力済み）場合は触らない。
    /// </summary>
    private void FillCustomRangeDefaultIfBlank()
    {
        if (!string.IsNullOrWhiteSpace(CustomFromBox.Text) || !string.IsNullOrWhiteSpace(CustomToBox.Text))
            return;

        DateTimeOffset now = DateTimeOffset.Now;
        // TextChangedのたびのLoadHistory再実行を防ぎ、呼び出し元の1回のLoadHistoryへ任せる。
        bool wasInitializing = _initializing;
        _initializing = true;
        SetCustomRangeBoxes(UsagePeriod.StartOfMonth(now), LocalStartOfNextMonth(now));
        _initializing = wasInitializing;
    }

    private void SetCustomRangeBoxes(DateTimeOffset from, DateTimeOffset toExclusive)
    {
        (string fromText, string toText) = CustomDateRangeParser.FormatInclusive(from, toExclusive);
        CustomFromBox.Text = fromText;
        CustomToBox.Text = toText;
    }

    private void CustomRange_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        if ((PeriodOption)PeriodCombo.SelectedIndex != PeriodOption.Custom) return;
        LoadHistory();
    }

    private void TriggerCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        LoadHistory();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadHistory();

    private void LoadHistory()
    {
        ValidationErrorText.Visibility = Visibility.Collapsed;

        (DateTimeOffset? From, DateTimeOffset? To)? range = ComputeRange(out string? error, out string periodLabel);
        if (range is null)
        {
            // 不正な入力では一覧・ヘッダを直前の状態のまま残す（ユーザーの意図と逆の「全件表示」を避ける）。
            ValidationErrorText.Text = error;
            ValidationErrorText.Visibility = Visibility.Visible;
            return;
        }

        List<ApiCallTrigger> selectedTriggers = CollectSelectedTriggers();
        if (selectedTriggers.Count == 0)
        {
            // GetHistory/GetUsageSummary は空コレクションを「フィルタなし＝全種別」として扱うため、
            // ここでリポジトリを呼ばず、明示的に空の結果を描画する。
            ResultsList.ItemsSource = Array.Empty<BillingHistoryDisplayRow>();
            RenderSummary(ApiCallUsageSummary.Empty, page: null, periodLabel, noTriggerSelected: true);
            return;
        }

        ApiCallHistoryPage page = _apiCalls.GetHistory(range.Value.From, range.Value.To, selectedTriggers);
        ApiCallUsageSummary summary = _apiCalls.GetUsageSummary(range.Value.From, range.Value.To, selectedTriggers);

        ResultsList.ItemsSource = page.Rows.Select(ToDisplayRow).ToArray();
        RenderSummary(summary, page, periodLabel, noTriggerSelected: false);
    }

    private List<ApiCallTrigger> CollectSelectedTriggers()
    {
        List<ApiCallTrigger> selected = [];
        if (TriggerAutoCheck.IsChecked == true) selected.Add(ApiCallTrigger.Auto);
        if (TriggerManualCheck.IsChecked == true) selected.Add(ApiCallTrigger.Manual);
        if (TriggerRealternativeCheck.IsChecked == true) selected.Add(ApiCallTrigger.Realternative);
        if (TriggerStyleGuideCheck.IsChecked == true) selected.Add(ApiCallTrigger.StyleGuide);
        return selected;
    }

    private (DateTimeOffset? From, DateTimeOffset? To)? ComputeRange(out string? error, out string periodLabel)
    {
        error = null;
        DateTimeOffset now = DateTimeOffset.Now;

        switch ((PeriodOption)PeriodCombo.SelectedIndex)
        {
            case PeriodOption.Today:
                periodLabel = "当日";
                return (UsagePeriod.StartOfDay(now), LocalStartOfNextDay(now));

            case PeriodOption.ThisWeek:
                periodLabel = "当週";
                DateTimeOffset weekStart = UsagePeriod.StartOfWeek(now);
                return (weekStart, LocalAddDays(weekStart, 7));

            case PeriodOption.ThisMonth:
                periodLabel = "当月";
                return (UsagePeriod.StartOfMonth(now), LocalStartOfNextMonth(now));

            case PeriodOption.AllTime:
                periodLabel = "全期間";
                return (null, null);

            case PeriodOption.Custom:
                return ComputeCustomRange(out error, out periodLabel);

            default:
                periodLabel = "";
                return (null, null);
        }
    }

    private (DateTimeOffset?, DateTimeOffset?)? ComputeCustomRange(out string? error, out string periodLabel)
    {
        periodLabel = "カスタム";

        // 解析・境界計算そのものは UI に依存しない純粋関数へ切り出してあり、
        // 極端な入力（例: 終了日 9999-12-31）による例外を含めて PromptValidation から検証できる。
        CustomDateRangeParser.Result result = CustomDateRangeParser.Parse(CustomFromBox.Text, CustomToBox.Text);
        error = result.Error;
        return result.IsError ? null : (result.From, result.To);
    }

    private static DateTimeOffset LocalStartOfNextDay(DateTimeOffset value)
    {
        DateTime localStart = value.LocalDateTime.Date;
        return new DateTimeOffset(localStart.AddDays(1));
    }

    private static DateTimeOffset LocalAddDays(DateTimeOffset value, int days)
    {
        DateTime localStart = value.LocalDateTime.Date.AddDays(days);
        return new DateTimeOffset(localStart);
    }

    private static DateTimeOffset LocalStartOfNextMonth(DateTimeOffset value)
    {
        DateTime localStart = new(
            value.LocalDateTime.Year, value.LocalDateTime.Month, 1, 0, 0, 0, DateTimeKind.Local);
        return new DateTimeOffset(localStart.AddMonths(1));
    }

    private void RenderSummary(
        ApiCallUsageSummary summary, ApiCallHistoryPage? page, string periodLabel, bool noTriggerSelected)
    {
        if (noTriggerSelected)
        {
            SummaryHeaderText.Text = $"{periodLabel}: 種別を1つ以上選択してください（該当なし）";
            SummaryHeaderText.ToolTip = null;
            return;
        }

        string usd = UsageFormatting.FormatUsd(summary.UsdCost);
        string jpy = UsageFormatting.FormatJpy(summary);
        string statusCounts = UsageFormatting.FormatStatusCounts(summary);

        string text =
            $"{periodLabel}: {summary.TotalCalls:N0}件（{statusCounts}）　" +
            $"入力 {summary.PromptTokens:N0} / 出力 {summary.OutputTokens:N0} tokens　" +
            $"${usd} ({jpy})　提案 {summary.SuggestionCount:N0} / 破棄 {summary.DiscardedCount:N0}";

        if (page is { Truncated: true })
            text += $"　※ {page.TotalCount:N0}件中 {page.Rows.Count:N0}件を表示";

        SummaryHeaderText.Text = text;
        SummaryHeaderText.ToolTip = UsageFormatting.FormatSummaryRateReference(summary);
    }

    private static BillingHistoryDisplayRow ToDisplayRow(ApiCallHistoryRow row) => new(
        row.Id,
        row.CalledAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        UsageFormatting.FormatTrigger(row.Trigger),
        row.Model,
        row.PromptTokens.ToString("N0", CultureInfo.InvariantCulture),
        row.OutputTokens.ToString("N0", CultureInfo.InvariantCulture),
        "$" + UsageFormatting.FormatUsd(row.UsdCost),
        row.JpyCost is decimal jpy ? UsageFormatting.FormatJpy(jpy) : "—",
        UsageFormatting.FormatRateDate(row.RateDate),
        row.DurationMilliseconds.ToString("N0", CultureInfo.InvariantCulture) + " ms",
        UsageFormatting.FormatStatus(row.Status),
        row.SuggestionCount.ToString("N0", CultureInfo.InvariantCulture),
        row.DiscardedCount.ToString("N0", CultureInfo.InvariantCulture),
        row.ErrorMessage);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
