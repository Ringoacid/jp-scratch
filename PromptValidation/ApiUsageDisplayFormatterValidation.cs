using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>応答直後の使用量不明と料金未確認を別表示する純粋関数の自己テスト。</summary>
internal static class ApiUsageDisplayFormatterValidation
{
    internal static bool RunSelfTests()
    {
        ApiUsageDisplayCost usageUnknownButConfirmed = new(
            PromptTokens: 0, OutputTokens: 0, UsdCost: 0m, JpyCost: 0m,
            FxRate: null, OriginalCurrency: PricingCurrency.Usd, OriginalCost: 0m,
            IsCostConfirmed: true, IsUsageKnown: false);
        ApiUsageDisplayCost usageKnownButUnconfirmed = new(
            PromptTokens: 100, OutputTokens: 20, UsdCost: 0m, JpyCost: 0.01m,
            FxRate: null, OriginalCurrency: PricingCurrency.Jpy, OriginalCost: 0.01m,
            IsCostConfirmed: false, IsUsageKnown: true);
        ApiUsageDisplayCost usageKnownButAmountMissing = new(
            PromptTokens: 50, OutputTokens: 10, UsdCost: 0m, JpyCost: null,
            FxRate: new FxRate(
                new DateOnly(2026, 8, 20), 150m,
                new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(9))),
            OriginalCurrency: null, OriginalCost: null,
            IsCostConfirmed: false, IsUsageKnown: true);

        string usageUnknownText =
            ApiUsageDisplayFormatter.BuildFailureUsageText(usageUnknownButConfirmed);
        string noCostText = ApiUsageDisplayFormatter.BuildFailureUsageText(null);
        // 課金ログの記録処理そのものが落ちたとき、別案生成が渡す値。応答前の失敗（$0 確定）とも
        // 「応答前のため確認できない」とも別の文言になることを確かめる。
        ApiUsageDisplayCost recordingFailed = new(
            PromptTokens: 0, OutputTokens: 0, UsdCost: 0m, JpyCost: null,
            FxRate: null, OriginalCurrency: null, OriginalCost: null,
            IsCostConfirmed: false, IsUsageKnown: false);
        string recordingFailedText =
            ApiUsageDisplayFormatter.BuildFailureUsageText(recordingFailed);
        string recordingFailedStatus = ApiUsageDisplayFormatter.BuildFailureStatusText(
            "別案生成に失敗しました", recordingFailed, "テスト用DBエラー");
        string costUnconfirmedText = ApiUsageDisplayFormatter.BuildUsageText(
            [usageKnownButUnconfirmed, usageKnownButAmountMissing]);
        ApiUsageDisplayCost confirmedCost = new(
            PromptTokens: 10, OutputTokens: 5, UsdCost: 0.01m, JpyCost: 1.50m,
            FxRate: usageKnownButAmountMissing.FxRate,
            OriginalCurrency: PricingCurrency.Usd, OriginalCost: 0.01m,
            IsCostConfirmed: true, IsUsageKnown: true);
        string confirmedStatusText = ApiUsageDisplayFormatter.FormatCostText([confirmedCost]);
        string mixedStatusText = ApiUsageDisplayFormatter.FormatCostText(
            [confirmedCost, usageKnownButUnconfirmed, usageKnownButAmountMissing]);

        bool usageUnknownPassed = usageUnknownText == "使用量未確認（料金は$0の確定扱い）";
        bool noCostPassed = noCostText == "応答前のため、使用量と料金は確認できませんでした。";
        bool recordingFailedPassed =
            recordingFailedText == "使用量未確認（料金未確認）" &&
            recordingFailedStatus.Contains("使用量未確認（料金未確認）", StringComparison.Ordinal) &&
            recordingFailedStatus.Contains("テスト用DBエラー", StringComparison.Ordinal) &&
            !recordingFailedStatus.Contains("応答前のため", StringComparison.Ordinal) &&
            !recordingFailedStatus.Contains("確定扱い", StringComparison.Ordinal);
        bool costUnconfirmedPassed = costUnconfirmedText ==
            "入力 150、出力・推論 30 tokens / 料金 料金未確認（未確認 2件 / " +
            "判明分 1件 / 元通貨計 ¥0.01 / 元通貨額不明 1件）";
        bool statusPathPassed =
            confirmedStatusText == "$0.01 (¥1.50) (1USD=¥150 / 08-20時点)" &&
            mixedStatusText.Contains(
                "未確認 2件 / 判明分 1件 / 元通貨計 ¥0.01 / 元通貨額不明 1件",
                StringComparison.Ordinal);
        string accountingNotice =
            ApiUsageDisplayFormatter.FormatAccountingErrorNotice("テスト用DBエラー");
        bool accountingNoticePassed =
            accountingNotice.Contains("課金ログまたは料金算出の処理に失敗しました", StringComparison.Ordinal) &&
            accountingNotice.Contains("テスト用DBエラー", StringComparison.Ordinal);
        string savedFailureStatus = ApiUsageDisplayFormatter.BuildFailureStatusText(
            "別案生成に失敗しました",
            usageUnknownButConfirmed,
            "テスト用DBエラー");
        string unsavedFailureStatus = ApiUsageDisplayFormatter.BuildFailureStatusText(
            "別案生成に失敗しました", null, null);
        bool failureStatusPassed =
            savedFailureStatus.Contains("料金は$0の確定扱い", StringComparison.Ordinal) &&
            savedFailureStatus.Contains("テスト用DBエラー", StringComparison.Ordinal) &&
            unsavedFailureStatus.Contains("応答前のため", StringComparison.Ordinal);

        bool passed = usageUnknownPassed && noCostPassed && recordingFailedPassed &&
            costUnconfirmedPassed && statusPathPassed && accountingNoticePassed &&
            failureStatusPassed;
        Console.WriteLine("応答直後の使用量不明・料金未確認の表示分離: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
