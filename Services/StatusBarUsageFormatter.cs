using System.Globalization;
using JpScratch.Models;

namespace JpScratch.Services;

/// <summary>
/// ステータスバー下段の課金表示オプション（docs/proofreading-ux-fixes-plan.md §8）。
/// <see cref="Models.AppSettings"/> の各項目をそのまま写した不変値。WPF 非依存で自己テストできる。
/// </summary>
internal readonly record struct StatusBarDisplayOptions(
    bool ShowLatest,
    bool ShowSession,
    bool ShowToday,
    bool ShowMonth,
    bool ShowFx,
    StatusBarCurrencyFormat Currency);

/// <summary>
/// ステータスバー下段の課金表示を組み立てる純粋関数。
/// <c>Views/MainWindow.xaml.cs</c> の <c>RefreshUsageDisplay</c> が唯一の呼び出し元で、
/// 表示項目のON/OFF・通貨形式の切り替えをWPF非依存のまま検証できる。
///
/// 既定（当月＋為替、円表示）の表示例:
/// <c>今月 ¥4.80  為替 USD/JPY 155.00 (7/31)</c>
///
/// 円換算できない期間は既存の安全な欠損表示（¥—）を維持する。為替は独立した項目として扱い、
/// 取得済みキャッシュが無い場合は「—」を表示する（項目自体は消さない＝設定の反映を分かりやすくする）。
/// </summary>
internal static class StatusBarUsageFormatter
{
    internal static string Format(
        StatusBarDisplayOptions options,
        ApiCallLog? latest,
        ApiCallUsageSummary session,
        ApiCallUsageSummary today,
        ApiCallUsageSummary month,
        FxRate? cachedRate)
    {
        var parts = new List<string>();
        if (options.ShowLatest)
            parts.Add(FormatLatest(latest, options.Currency));
        if (options.ShowSession)
            parts.Add($"起動後 {FormatSummaryCost(session, options.Currency)}");
        if (options.ShowToday)
            parts.Add($"今日 {FormatSummaryCost(today, options.Currency)}");
        if (options.ShowMonth)
            parts.Add($"今月 {FormatSummaryCost(month, options.Currency)}");
        if (options.ShowFx)
            parts.Add(FormatFx(cachedRate));
        return string.Join("  ", parts);
    }

    /// <summary>直近1件。入出力トークンと料金をまとめて出す（この項目だけトークンを含む）。</summary>
    private static string FormatLatest(ApiCallLog? latest, StatusBarCurrencyFormat currency)
    {
        if (latest is null)
            return "直近 —";

        string tokens = $"↑{latest.PromptTokens:N0}↓{latest.OutputTokens:N0}";
        string cost = latest.IsUsdCostConfirmed
            ? FormatCost(latest.UsdCost, latest.JpyCost, currency)
            : latest.OriginalCurrency == PricingCurrency.Jpy && latest.OriginalCost is decimal
                ? $"料金未確認 ({UsageFormatting.FormatJpy(latest)})"
                : "料金未確認";
        return $"直近 {tokens} {cost}";
    }

    /// <summary>期間集計。USD/JPYは <see cref="ApiCallUsageSummary"/> の完全フラグに従う。</summary>
    private static string FormatSummaryCost(
        ApiCallUsageSummary summary,
        StatusBarCurrencyFormat currency)
    {
        decimal? jpy = summary.IsJpyComplete ? summary.JpyCost : null;
        string cost = FormatCost(summary.UsdCost, jpy, currency);
        string unconfirmed = UsageFormatting.FormatUnconfirmedCost(summary);
        return unconfirmed.Length > 0 ? $"{cost}（{unconfirmed}）" : cost;
    }

    /// <summary>
    /// 通貨形式に応じた共通の料金表示。円が欠損している場合はドルだけ・または（¥—）を付ける。
    /// 為替レート（基準日つき）はこの関数には含めない。為替は独立項目として扱う。
    /// </summary>
    private static string FormatCost(decimal usd, decimal? jpy, StatusBarCurrencyFormat currency)
    {
        string usdText = $"${UsageFormatting.FormatUsd(usd)}";
        switch (currency)
        {
            case StatusBarCurrencyFormat.Usd:
                return usdText;
            case StatusBarCurrencyFormat.Both:
                return jpy is decimal j
                    ? $"{usdText} ({UsageFormatting.FormatJpy(j)})"
                    : $"{usdText} (¥—)";
            case StatusBarCurrencyFormat.Jpy:
            default:
                // 円が欠損している期間は「¥—」だけにせず、ドル額も併記する。
                // 要件 3.5.3:「キャッシュがまったくない状態で失敗した場合は ¥— と表示し、$ のみ表示する」。
                // ¥— 単独だと、為替を取得できていない間は自分がいくら使ったのかを確認する手段が
                // 画面から消えてしまう（課金履歴を開かないと分からない）。
                return jpy is decimal j2
                    ? UsageFormatting.FormatJpy(j2)
                    : $"{usdText} (¥—)";
        }
    }

    /// <summary>取得済みキャッシュのレートを「USD/JPY 155.00 (7/31)」の形で表示する。</summary>
    private static string FormatFx(FxRate? cachedRate)
        => cachedRate is null
            ? "為替 —"
            : $"為替 USD/JPY {cachedRate.UsdJpy.ToString("0.00##", CultureInfo.InvariantCulture)} " +
              $"({cachedRate.RateDate.ToString("M/d", CultureInfo.InvariantCulture)})";
}
