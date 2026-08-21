using System.Globalization;

namespace JpScratch.Services;

/// <summary>
/// USD / JPY / 為替レートの表示書式（要件 3.5.2〜3.5.3）を一箇所に集約する。
/// ステータスバー下段（<c>Views/MainWindow.xaml.cs</c> の <c>RefreshUsageDisplay</c>）と
/// 課金履歴画面（<c>Views/BillingHistoryWindow.xaml.cs</c>）が同じ書式を必ず共有するために存在する。
/// 丸めは表示時のみ行い、内部計算・永続化は常に <see cref="decimal"/> のまま扱う。
/// </summary>
internal static class UsageFormatting
{
    /// <summary>$は小数点以下最大8桁（例 $0.000021）。</summary>
    internal static string FormatUsd(decimal cost)
        => cost.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>
    /// ¥は通常小数第2位まで、0 &lt; |¥| &lt; 0.01 のときだけ微小額を隠さないよう小数第3位まで。
    /// </summary>
    internal static string FormatJpy(decimal value)
    {
        decimal rounded = value != 0m && Math.Abs(value) < 0.01m
            ? Math.Round(value, 3, MidpointRounding.AwayFromZero)
            : Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return "¥" + rounded.ToString(
            Math.Abs(rounded) < 0.01m && rounded != 0m ? "0.###" : "0.00",
            CultureInfo.InvariantCulture);
    }

    internal static string FormatJpy(ApiCallLog entry)
        => !entry.IsUsdCostConfirmed
            ? entry.OriginalCurrency == PricingCurrency.Jpy && entry.OriginalCost is decimal original
                ? FormatJpy(original)
                : "¥—"
            : entry.JpyCost is decimal cost
            ? FormatJpy(cost)
            : entry.UsdCost == 0m ? FormatJpy(0m) : "¥—";

    internal static string FormatJpy(ApiCallHistoryRow entry)
        => !entry.IsUsdCostConfirmed
            ? entry.OriginalCurrency == PricingCurrency.Jpy && entry.OriginalCost is decimal original
                ? FormatJpy(original)
                : "—"
            : entry.JpyCost is decimal cost
                ? FormatJpy(cost)
                : entry.UsdCost == 0m ? FormatJpy(0m) : "—";

    internal static string FormatUsd(ApiCallLog entry)
        => entry.IsUsdCostConfirmed ? "$" + FormatUsd(entry.UsdCost) : "未確認";

    internal static string FormatUsd(ApiCallHistoryRow entry)
        => entry.IsUsdCostConfirmed ? "$" + FormatUsd(entry.UsdCost) : "未確認";

    internal static string FormatJpy(ApiCallUsageSummary summary)
        => summary.IsJpyComplete ? FormatJpy(summary.JpyCost) : "¥—";

    /// <summary>
    /// 期間合計に含めていない料金未確認分を、確定分と母集団を混ぜずに表示する。
    /// 元円額が無い料金算出エラーも件数として隠さず示す。
    /// </summary>
    internal static string FormatUnconfirmedCost(ApiCallUsageSummary summary)
    {
        if (summary.UnconfirmedCostCalls == 0)
            return "";

        long amountCalls = Math.Clamp(
            summary.UnconfirmedJpyAmountCalls, 0, summary.UnconfirmedCostCalls);
        long unknownCalls = summary.UnconfirmedCostCalls - amountCalls;
        List<string> details = [];
        if (amountCalls > 0)
            details.Add($"判明分 {amountCalls:N0}件 / 元通貨計 {FormatJpy(summary.UnconfirmedJpyCost)}");
        if (unknownCalls > 0)
            details.Add($"元通貨額不明 {unknownCalls:N0}件");
        return $"未確認 {summary.UnconfirmedCostCalls:N0}件 / {string.Join(" / ", details)}";
    }

    /// <summary>単一ログの直近表示に付ける「@07-25」のようなレート基準日サフィックス。</summary>
    internal static string FormatRateDateSuffix(ApiCallLog entry)
        => entry.JpyCost is not null && entry.RateDate is DateOnly date
            ? "@" + date.ToString("MM-dd", CultureInfo.InvariantCulture)
            : "";

    /// <summary>レート基準日が null の行の書式（課金履歴の明細1行にも使う）。</summary>
    internal static string FormatRateDate(DateOnly? rateDate)
        => rateDate is DateOnly date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—";

    internal static string FormatRateReference(FxRate? rate)
        => rate is null
            ? ""
            : $" (1USD=¥{rate.UsdJpy.ToString("0.####", CultureInfo.InvariantCulture)} / " +
              $"{rate.RateDate:MM-dd}時点)";

    /// <summary>期間集計向け。単一レートなら基準日つき、複数レートなら範囲と件数を示す。</summary>
    internal static string FormatSummaryRateReference(ApiCallUsageSummary summary)
    {
        string rateText;
        if (!summary.IsJpyComplete)
            rateText = "JPY換算に未記録ログあり";
        else if (summary.DistinctRateCount == 1 &&
            summary.SingleUsdJpyRate is decimal rate && summary.SingleRateDate is DateOnly date)
        {
            rateText = $"1USD=¥{rate.ToString("0.####", CultureInfo.InvariantCulture)} / " +
                   $"{date:MM-dd}時点（ログ固定）";
        }
        else if (summary.DistinctRateCount > 1 &&
            summary.FirstRateDate is DateOnly first && summary.LastRateDate is DateOnly last)
        {
            rateText = $"ログ固定レート合計 / {first:MM-dd}〜{last:MM-dd}・" +
                   $"{summary.DistinctRateCount}レート";
        }
        else
        {
            rateText = summary.UnconfirmedCostCalls > 0 && summary.UsdCost == 0m
                ? ""
                : summary.UsdCost == 0m ? "JPY 0円（レート不要）" : "ログ固定レート情報なし";
        }

        string unconfirmedText = FormatUnconfirmedCost(summary);
        return unconfirmedText.Length == 0
            ? rateText
            : rateText.Length == 0 ? unconfirmedText : $"{rateText} / {unconfirmedText}";
    }

    internal static string FormatStatusCounts(ApiCallStatus status)
        => status switch
        {
            ApiCallStatus.Ok => "成功 1",
            ApiCallStatus.Error => "エラー 1",
            ApiCallStatus.Timeout => "タイムアウト 1",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    internal static string FormatStatusCounts(ApiCallUsageSummary summary)
        => $"成功 {summary.OkCalls:N0} / エラー {summary.ErrorCalls:N0} / タイムアウト {summary.TimeoutCalls:N0}";

    /// <summary>課金履歴画面の明細一覧・トレイ等で使う、種別の日本語表記。</summary>
    internal static string FormatTrigger(ApiCallTrigger trigger)
        => trigger switch
        {
            ApiCallTrigger.Auto => "自動",
            ApiCallTrigger.Manual => "手動",
            ApiCallTrigger.Realternative => "別案生成",
            ApiCallTrigger.StyleGuide => "スタイルガイド生成",
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    /// <summary>課金履歴画面の明細一覧で使う、成否の日本語表記。</summary>
    internal static string FormatStatus(ApiCallStatus status)
        => status switch
        {
            ApiCallStatus.Ok => "成功",
            ApiCallStatus.Error => "エラー",
            ApiCallStatus.Timeout => "タイムアウト",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
