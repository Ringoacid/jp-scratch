namespace JpScratch.Services;

/// <summary>円建て料金をUSDへ換算する共通処理。</summary>
internal static class UsdCostConversion
{
    /// <summary>decimalで表現できる最小の正の料金。</summary>
    internal const decimal MinimumPositiveUsd = 0.0000000000000000000000000001m;

    internal static decimal ConvertJpyToUsd(decimal jpyCost, decimal usdJpyRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(usdJpyRate);

        decimal usdCost = jpyCost / usdJpyRate;
        if (jpyCost > 0m && usdCost == 0m)
        {
            // 正の料金を0へ丸めると本物の無料料金と区別できず、料金を低く見せるため切り上げる。
            return MinimumPositiveUsd;
        }

        return usdCost;
    }
}
