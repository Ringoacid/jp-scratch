namespace JpScratch.Services;

/// <summary>応答直後の使用量・料金表示へ渡す、WPF非依存の値。</summary>
internal sealed record ApiUsageDisplayCost(
    int PromptTokens,
    int OutputTokens,
    decimal UsdCost,
    decimal? JpyCost,
    FxRate? FxRate,
    string? OriginalCurrency,
    decimal? OriginalCost,
    bool IsCostConfirmed,
    bool IsUsageKnown);

/// <summary>
/// 応答直後の使用量表示を組み立てる純粋関数。
/// 使用量不明と料金未確認を別々の文言で示し、保存される課金ログの確定状態と矛盾させない。
/// </summary>
internal static class ApiUsageDisplayFormatter
{
    internal static string FormatAccountingErrorNotice(string? accountingError)
        => string.IsNullOrWhiteSpace(accountingError)
            ? ""
            : "\n\n課金ログまたは料金算出の処理に失敗しました。表示額は算出できた範囲のみで、" +
              "当月累計と課金履歴が実際と一致しない可能性があります。\n詳細: " +
              accountingError;

    internal static string BuildFailureUsageText(ApiUsageDisplayCost? cost)
        => cost is null
            ? "応答前のため、使用量と料金は確認できませんでした。"
            : BuildUsageText([cost]);

    internal static string BuildFailureStatusText(
        string prefix,
        ApiUsageDisplayCost? recordedCost,
        string? accountingError)
    {
        string status = recordedCost is { } cost
            ? $"{prefix} {FormatCostText(cost)}"
            : $"{prefix}（応答前のため、使用量と料金は確認できませんでした）";
        return status + FormatAccountingErrorNotice(accountingError);
    }

    internal static string FormatCostText(ApiUsageDisplayCost cost)
        => FormatCostText([cost]);

    /// <summary>失敗時ステータスへ表示する料金部分を、リスト全体から非再帰に組み立てる。</summary>
    internal static string FormatCostText(IReadOnlyList<ApiUsageDisplayCost> costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        if (costs.Count == 0)
            return "料金未確認";

        IReadOnlyList<ApiUsageDisplayCost> known =
            costs.Where(cost => cost.IsUsageKnown).ToArray();
        if (known.Count == 0)
        {
            if (costs.All(cost => cost.IsCostConfirmed))
            {
                decimal usd = costs.Sum(cost => cost.UsdCost);
                return $"使用量未確認（料金は${UsageFormatting.FormatUsd(usd)}の確定扱い）";
            }

            return "使用量未確認（料金未確認）";
        }

        IReadOnlyList<ApiUsageDisplayCost> confirmed =
            known.Where(cost => cost.IsCostConfirmed).ToArray();
        string costText = confirmed.Count == 0
            ? "料金未確認"
            : FormatConfirmedCosts(confirmed);
        int unconfirmedCount = known.Count - confirmed.Count;
        if (unconfirmedCount > 0)
        {
            costText += $"（{FormatUnconfirmedCosts(known, unconfirmedCount)}）";
        }

        return costText + (known.Count == costs.Count ? "" : "（一部使用量未確認）");
    }

    internal static string BuildUsageText(IReadOnlyList<ApiUsageDisplayCost> costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        if (costs.Count == 0)
            return "使用量・料金は未確認";

        IReadOnlyList<ApiUsageDisplayCost> known =
            costs.Where(cost => cost.IsUsageKnown).ToArray();
        if (known.Count == 0)
        {
            if (costs.All(cost => cost.IsCostConfirmed))
            {
                decimal usd = costs.Sum(cost => cost.UsdCost);
                return $"使用量未確認（料金は${UsageFormatting.FormatUsd(usd)}の確定扱い）";
            }

            return "使用量未確認（料金未確認）";
        }

        int promptTokens = known.Sum(cost => cost.PromptTokens);
        int outputTokens = known.Sum(cost => cost.OutputTokens);
        IReadOnlyList<ApiUsageDisplayCost> confirmed =
            known.Where(cost => cost.IsCostConfirmed).ToArray();
        int unconfirmedCount = known.Count - confirmed.Count;
        string label = known.Count == costs.Count ? "" : "（既知分）";
        string unknownUsage = known.Count == costs.Count ? "" : "（一部使用量未確認）";
        string costText = confirmed.Count == 0
            ? "料金未確認"
            : FormatConfirmedCosts(confirmed);
        if (unconfirmedCount > 0)
            costText += $"（{FormatUnconfirmedCosts(known, unconfirmedCount)}）";

        return $"入力 {promptTokens:N0}、出力・推論 {outputTokens:N0} tokens{label} / " +
               $"料金 {costText}{unknownUsage}";
    }

    private static string FormatConfirmedCosts(IReadOnlyList<ApiUsageDisplayCost> costs)
    {
        decimal usdCost = costs.Sum(cost => cost.UsdCost);
        if (costs.Any(cost => cost.UsdCost != 0m && cost.JpyCost is null))
            return $"${UsageFormatting.FormatUsd(usdCost)} (¥—)";

        decimal jpyCost = costs.Sum(cost => cost.JpyCost ?? 0m);
        return $"${UsageFormatting.FormatUsd(usdCost)} ({UsageFormatting.FormatJpy(jpyCost)})" +
               FormatRateReferences(costs);
    }

    internal static string FormatUnconfirmedCosts(
        IReadOnlyList<ApiUsageDisplayCost> known,
        int count)
    {
        ApiUsageDisplayCost[] withJpy = known
            .Where(cost => !cost.IsCostConfirmed &&
                cost.OriginalCurrency == PricingCurrency.Jpy &&
                cost.OriginalCost is decimal)
            .ToArray();
        int amountCount = Math.Min(withJpy.Length, count);
        int unknownCount = count - amountCount;
        List<string> details = [];
        if (amountCount > 0)
            details.Add($"判明分 {amountCount}件 / 元通貨計 " +
                UsageFormatting.FormatJpy(withJpy.Take(amountCount)
                    .Sum(cost => cost.OriginalCost!.Value)));
        if (unknownCount > 0)
            details.Add($"元通貨額不明 {unknownCount}件");
        return $"未確認 {count}件 / {string.Join(" / ", details)}";
    }

    private static string FormatRateReferences(IReadOnlyList<ApiUsageDisplayCost> costs)
    {
        (decimal Rate, DateOnly Date)[] rates = costs
            .Where(cost => cost.JpyCost is not null && cost.FxRate is not null)
            .Select(cost => (cost.FxRate!.UsdJpy, cost.FxRate.RateDate))
            .Distinct()
            .OrderBy(value => value.RateDate)
            .ToArray();
        if (rates.Length == 1)
        {
            return UsageFormatting.FormatRateReference(
                new FxRate(rates[0].Date, rates[0].Rate, default));
        }

        return rates.Length > 1
            ? $" (ログ固定レート合計 / {rates[0].Date:MM-dd}〜{rates[^1].Date:MM-dd}・{rates.Length}レート)"
            : "";
    }
}
