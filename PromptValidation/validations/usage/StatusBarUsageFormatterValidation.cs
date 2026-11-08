using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// ステータスバー下段の課金表示（docs/proofreading-ux-fixes-plan.md §8）の純粋関数テスト。
/// 各表示項目の単独・複数・全非表示、通貨形式の切替、既定値（当月＋為替、円表示）、
/// 円欠損時の安全な表示を確認する。
/// </summary>
internal static class StatusBarUsageFormatterValidation
{
    private static readonly DateOnly RateDate = new(2026, 7, 31);
    private static readonly FxRate CachedRate =
        new(RateDate, 155.00m, new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.FromHours(9)));

    private static readonly ApiCallUsageSummary MonthSummary = new(
        TotalCalls: 8,
        OkCalls: 7,
        ErrorCalls: 1,
        TimeoutCalls: 0,
        PromptTokens: 1000,
        OutputTokens: 500,
        UsdCost: 0.0310m,
        SuggestionCount: 4,
        DiscardedCount: 1,
        JpyCost: 4.80m,
        IsJpyComplete: true,
        SingleUsdJpyRate: 155.00m,
        SingleRateDate: RateDate,
        FirstRateDate: RateDate,
        LastRateDate: RateDate,
        DistinctRateCount: 1);

    private static readonly ApiCallUsageSummary TodaySummary = new(
        TotalCalls: 2, OkCalls: 2, ErrorCalls: 0, TimeoutCalls: 0,
        PromptTokens: 200, OutputTokens: 100, UsdCost: 0.0050m,
        SuggestionCount: 1, DiscardedCount: 0,
        JpyCost: 0.78m, IsJpyComplete: true,
        SingleUsdJpyRate: 155.00m, SingleRateDate: RateDate,
        FirstRateDate: RateDate, LastRateDate: RateDate, DistinctRateCount: 1);

    private static readonly ApiCallUsageSummary SessionSummary = new(
        TotalCalls: 3, OkCalls: 3, ErrorCalls: 0, TimeoutCalls: 0,
        PromptTokens: 300, OutputTokens: 150, UsdCost: 0.0090m,
        SuggestionCount: 2, DiscardedCount: 0,
        JpyCost: 1.40m, IsJpyComplete: true,
        SingleUsdJpyRate: 155.00m, SingleRateDate: RateDate,
        FirstRateDate: RateDate, LastRateDate: RateDate, DistinctRateCount: 1);

    /// <summary>JPY が欠損している期間（非ゼロUSD）の集計。</summary>
    private static readonly ApiCallUsageSummary IncompleteJpySummary = new(
        TotalCalls: 1, OkCalls: 1, ErrorCalls: 0, TimeoutCalls: 0,
        PromptTokens: 100, OutputTokens: 50, UsdCost: 0.0020m,
        SuggestionCount: 1, DiscardedCount: 0,
        JpyCost: 0m, IsJpyComplete: false,
        SingleUsdJpyRate: null, SingleRateDate: null,
        FirstRateDate: null, LastRateDate: null, DistinctRateCount: 0);

    private static readonly ApiCallUsageSummary UnconfirmedSummary = new(
        TotalCalls: 3, OkCalls: 3, ErrorCalls: 0, TimeoutCalls: 0,
        PromptTokens: 300, OutputTokens: 150, UsdCost: 0.0090m,
        SuggestionCount: 2, DiscardedCount: 0,
        JpyCost: 0.80m, IsJpyComplete: true,
        SingleUsdJpyRate: 155.00m, SingleRateDate: RateDate,
        FirstRateDate: RateDate, LastRateDate: RateDate, DistinctRateCount: 1,
        UnconfirmedCostCalls: 2, UnconfirmedJpyCost: 0.60m, UnconfirmedJpyAmountCalls: 1);

    private static readonly ApiCallUsageSummary MultipleRateUnconfirmedSummary = new(
        TotalCalls: 4, OkCalls: 4, ErrorCalls: 0, TimeoutCalls: 0,
        PromptTokens: 400, OutputTokens: 200, UsdCost: 0.0120m,
        SuggestionCount: 2, DiscardedCount: 0,
        JpyCost: 2.00m, IsJpyComplete: true,
        SingleUsdJpyRate: null, SingleRateDate: null,
        FirstRateDate: new DateOnly(2026, 7, 30), LastRateDate: RateDate, DistinctRateCount: 2,
        UnconfirmedCostCalls: 1, UnconfirmedJpyCost: 0.50m, UnconfirmedJpyAmountCalls: 1);

    private static readonly ApiCallLog Latest = new(
        Id: 1,
        CalledAt: new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.FromHours(9)),
        PromptTokens: 307,
        OutputTokens: 6,
        UsdCost: 0.0001071m,
        UsdJpyRate: 155.00m,
        RateDate: RateDate,
        JpyCost: 0.02m,
        Status: ApiCallStatus.Ok,
        SuggestionCount: 1,
        DiscardedCount: 0);

    internal static bool RunSelfTests()
    {
        (string Name, Func<bool> Test)[] tests =
        [
            ("既定は当月＋為替・円表示", TestDefault),
            ("全項目・両方表示", TestAllBoth),
            ("全非表示は空文字", TestAllHidden),
            ("単独表示（直近）", TestLatestOnly),
            ("通貨切替（ドル）", TestUsdOnly),
            ("通貨切替（両方）", TestBoth),
            ("円欠損は安全な欠損表示", TestJpyMissing),
            ("未確認件数を合計へ併記", TestUnconfirmedCount),
            ("単一レートの未確認注記を連結", TestSingleRateWithUnconfirmed),
            ("複数レートの未確認注記を連結", TestMultipleRatesWithUnconfirmed),
            ("為替なしは「為替 —」", TestNoFxRate),
        ];

        bool passed = true;
        foreach ((string name, Func<bool> test) in tests)
        {
            bool result = test();
            Console.WriteLine($"ステータスバー表示（{name}）: {(result ? "PASS" : "FAIL")}");
            passed &= result;
        }
        return passed;
    }

    private static StatusBarDisplayOptions Options(
        bool latest = false, bool session = false, bool today = false,
        bool month = true, bool fx = true, StatusBarCurrencyFormat currency = StatusBarCurrencyFormat.Jpy)
        => new(latest, session, today, month, fx, currency);

    private static string Format(
        StatusBarDisplayOptions options,
        ApiCallUsageSummary month,
        ApiCallUsageSummary today,
        ApiCallUsageSummary session,
        ApiCallLog? latest,
        FxRate? rate)
        => StatusBarUsageFormatter.Format(options, latest, session, today, month, rate);

    private static bool TestDefault()
    {
        string text = Format(
            Options(), MonthSummary, TodaySummary, SessionSummary, null, CachedRate);
        return text == "今月 ¥4.80  為替 USD/JPY 155.00 (7/31)";
    }

    private static bool TestAllBoth()
    {
        string text = Format(
            Options(latest: true, session: true, today: true, month: true, fx: true, StatusBarCurrencyFormat.Both),
            MonthSummary, TodaySummary, SessionSummary, Latest, CachedRate);
        return text ==
            "直近 ↑307↓6 $0.0001071 (¥0.02)  " +
            "起動後 $0.009 (¥1.40)  " +
            "今日 $0.005 (¥0.78)  " +
            "今月 $0.031 (¥4.80)  " +
            "為替 USD/JPY 155.00 (7/31)";
    }

    private static bool TestAllHidden()
    {
        string text = Format(
            Options(latest: false, session: false, today: false, month: false, fx: false),
            MonthSummary, TodaySummary, SessionSummary, Latest, CachedRate);
        return text == "";
    }

    private static bool TestLatestOnly()
    {
        string text = Format(
            Options(latest: true, session: false, today: false, month: false, fx: false),
            MonthSummary, TodaySummary, SessionSummary, Latest, CachedRate);
        return text == "直近 ↑307↓6 ¥0.02";
    }

    private static bool TestUsdOnly()
    {
        string text = Format(
            Options(month: true, fx: false, currency: StatusBarCurrencyFormat.Usd),
            MonthSummary, TodaySummary, SessionSummary, null, CachedRate);
        return text == "今月 $0.031";
    }

    private static bool TestBoth()
    {
        string text = Format(
            Options(month: true, fx: false, currency: StatusBarCurrencyFormat.Both),
            MonthSummary, TodaySummary, SessionSummary, null, CachedRate);
        return text == "今月 $0.031 (¥4.80)";
    }

    private static bool TestJpyMissing()
    {
        // 要件3.5.3:「キャッシュがまったくない状態で失敗した場合は ¥— と表示し、$ のみ表示する」。
        // 円表示でも、円が欠損している期間はドル額を併記する（¥— 単独だと利用額を確認できない）。
        string text = Format(
            Options(month: true, fx: false, currency: StatusBarCurrencyFormat.Jpy),
            IncompleteJpySummary, TodaySummary, SessionSummary, null, CachedRate);
        if (text != "今月 $0.002 (¥—)")
            return false;

        string both = Format(
            Options(month: true, fx: false, currency: StatusBarCurrencyFormat.Both),
            IncompleteJpySummary, TodaySummary, SessionSummary, null, CachedRate);
        return both == "今月 $0.002 (¥—)";
    }

    private static bool TestNoFxRate()
    {
        string text = Format(
            Options(month: true, fx: true), MonthSummary, TodaySummary, SessionSummary, null, null);
        return text == "今月 ¥4.80  為替 —";
    }

    private static bool TestUnconfirmedCount()
    {
        string text = Format(
            Options(month: true, fx: false), UnconfirmedSummary, TodaySummary, SessionSummary, null, CachedRate);
        return text == "今月 ¥0.80（未確認 2件 / 判明分 1件 / 元通貨計 ¥0.60 / 元通貨額不明 1件）";
    }

    private static bool TestSingleRateWithUnconfirmed()
    {
        string text = UsageFormatting.FormatSummaryRateReference(UnconfirmedSummary);
        return text.Contains("1USD=¥155 / 07-31時点（ログ固定）", StringComparison.Ordinal) &&
               text.Contains("未確認 2件 / 判明分 1件 / 元通貨計 ¥0.60 / 元通貨額不明 1件",
                   StringComparison.Ordinal);
    }

    private static bool TestMultipleRatesWithUnconfirmed()
    {
        string text = UsageFormatting.FormatSummaryRateReference(MultipleRateUnconfirmedSummary);
        return text.Contains("ログ固定レート合計 / 07-30〜07-31・2レート", StringComparison.Ordinal) &&
               text.Contains("未確認 1件 / 判明分 1件 / 元通貨計 ¥0.50", StringComparison.Ordinal);
    }
}
