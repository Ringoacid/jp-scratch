using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="SettingsFieldFormatting"/> の自己テスト。
/// レビュー指摘の再現防止: 設定画面の月間上限額欄が <c>"0.##"</c>（小数2桁）で表示していたため、
/// <c>0.0032</c> を保存した直後に設定画面を開き直すと表示が <c>"0"</c> になり、その状態で保存すると
/// 上限が 0（無制限）へ黙って壊れていた。「設定値 → 表示文字列 → パース → 同じ値」が
/// 崩れないことを、実際にユーザーの隔離環境の settings.json に残っていた 0.005 を含む複数の値で確認する。
/// </summary>
internal static class SettingsFieldFormattingValidation
{
    internal static bool RunSelfTests()
    {
        bool monthlyLimitPassed = RunMonthlyLimitRoundTripTests();
        bool warningRatioPassed = RunWarningRatioRoundTripTests();
        bool parseFallbackPassed = RunParseFallbackTests();

        bool passed = monthlyLimitPassed && warningRatioPassed && parseFallbackPassed;
        Console.WriteLine(
            "設定画面の数値欄の往復不変性（月間上限額・警告閾値・パース失敗時のフォールバック）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 月間上限額（USD）。0 は「無制限」という正当な値であることも含めて確認する。
    /// </summary>
    private static bool RunMonthlyLimitRoundTripTests()
    {
        decimal[] values =
        [
            2.00m,          // 既定値。"0.##" でも壊れなかった値だが、書式変更後も往復すること。
            0.0032m,        // レビューで見つかった実害の再現値。
            0.005m,         // ユーザーの隔離環境の settings.json に実際に残っていた値。
            0.00000001m,    // USD表示規約の下限（小数点以下8桁）。
            0m,             // 無制限。
            123456.5m,      // 大きい値でも壊れないこと。
        ];

        bool allPassed = true;
        foreach (decimal value in values)
        {
            string displayed = SettingsFieldFormatting.FormatMonthlyLimitUsd(value);
            decimal parsed = SettingsFieldFormatting.ParseDecimalOrDefault(displayed, fallback: -1m);
            bool roundTripped = parsed == value;
            allPassed &= roundTripped;
            Console.WriteLine(
                $"  月間上限額 往復: {value} → \"{displayed}\" → {parsed} : " +
                (roundTripped ? "PASS" : "FAIL"));
        }

        return allPassed;
    }

    /// <summary>
    /// 警告閾値（0〜1の割合）。パーセント表示との往復（割合→表示→パース→割合に戻す）で確認する。
    /// </summary>
    private static bool RunWarningRatioRoundTripTests()
    {
        decimal[] ratios = [0.80m, 0.0001m, 1m, 0.005m, 0.123456m];

        bool allPassed = true;
        foreach (decimal ratio in ratios)
        {
            string displayed = SettingsFieldFormatting.FormatWarningPercent(ratio);
            decimal parsedPercent = SettingsFieldFormatting.ParseDecimalOrDefault(displayed, fallback: -1m);
            decimal roundTripped = parsedPercent / 100m;
            bool passed = roundTripped == ratio;
            allPassed &= passed;
            Console.WriteLine(
                $"  警告閾値 往復: {ratio} ({ratio * 100m}%) → \"{displayed}\" → {roundTripped} : " +
                (passed ? "PASS" : "FAIL"));
        }

        return allPassed;
    }

    /// <summary>不正な入力ではフォールバック（変更前の値）を返し、既存の値を壊さないこと。</summary>
    private static bool RunParseFallbackTests()
    {
        bool blankFallsBack = SettingsFieldFormatting.ParseDecimalOrDefault("", 2.00m) == 2.00m;
        bool garbageFallsBack = SettingsFieldFormatting.ParseDecimalOrDefault("abc", 2.00m) == 2.00m;
        bool passed = blankFallsBack && garbageFallsBack;
        Console.WriteLine(
            "  パース失敗時のフォールバック（空欄・不正文字列で変更前の値を保つ）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
