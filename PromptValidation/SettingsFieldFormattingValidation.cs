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
        bool unitPriceRoundTripPassed = RunUnitPriceRoundTripTests();
        bool unitPriceRejectPassed = RunUnitPriceRejectTests();
        bool updatedAtPassed = RunUpdatedAtTests();
        bool buildPricingUneditedPassed = RunBuildPricingUneditedTests();
        bool buildPricingEditedPassed = RunBuildPricingEditedTests();

        bool passed = monthlyLimitPassed && warningRatioPassed && parseFallbackPassed &&
                      unitPriceRoundTripPassed && unitPriceRejectPassed && updatedAtPassed &&
                      buildPricingUneditedPassed && buildPricingEditedPassed;
        Console.WriteLine(
            "設定画面の数値欄の往復不変性（月間上限額・警告閾値・パース失敗時のフォールバック・モデル単価編集）: " +
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

    /// <summary>モデル単価（USD / 1M tokens）の表示→パースの往復不変性。</summary>
    private static bool RunUnitPriceRoundTripTests()
    {
        decimal[] values = [0.30m, 2.50m, 0m, 0.00000001m, 12345.6789m];

        bool allPassed = true;
        foreach (decimal value in values)
        {
            string displayed = SettingsFieldFormatting.FormatUnitPrice(value);
            bool ok = SettingsFieldFormatting.TryParseUnitPrice(displayed, out decimal parsed) &&
                      parsed == value;
            allPassed &= ok;
            Console.WriteLine(
                $"  モデル単価 往復: {value} → \"{displayed}\" → {parsed} : " +
                (ok ? "PASS" : "FAIL"));
        }

        return allPassed;
    }

    /// <summary>モデル単価は負値・空・非数値を拒否すること。</summary>
    private static bool RunUnitPriceRejectTests()
    {
        bool negativeRejected = !SettingsFieldFormatting.TryParseUnitPrice("-1", out _);
        bool blankRejected = !SettingsFieldFormatting.TryParseUnitPrice("", out _);
        bool garbageRejected = !SettingsFieldFormatting.TryParseUnitPrice("abc", out _);
        bool passed = negativeRejected && blankRejected && garbageRejected;
        Console.WriteLine(
            "  モデル単価の不正入力拒否（負値・空欄・非数値）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>更新日は yyyy-MM-dd 厳密。区切り違い・空欄・非日付は拒否すること。</summary>
    private static bool RunUpdatedAtTests()
    {
        bool acceptsStrict =
            SettingsFieldFormatting.TryParseUpdatedAt("2026-07-31", out string normalized) &&
            normalized == "2026-07-31";
        bool rejectsShortYear = !SettingsFieldFormatting.TryParseUpdatedAt("2026-7-31", out _);
        bool rejectsSlash = !SettingsFieldFormatting.TryParseUpdatedAt("2026/07/31", out _);
        bool rejectsBlank = !SettingsFieldFormatting.TryParseUpdatedAt("", out _);
        bool rejectsGarbage = !SettingsFieldFormatting.TryParseUpdatedAt("abcd", out _);
        bool passed = acceptsStrict && rejectsShortYear && rejectsSlash && rejectsBlank && rejectsGarbage;
        Console.WriteLine(
            "  更新日の厳密パース（yyyy-MM-dd のみ受理）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 設計の肝: 3欄すべてが未編集（書式化した original と一致）なら、パースし直さず original を
    /// そのまま返す。小数9桁以上の単価（表示書式の最大8桁を超える精度）で確認する。ここが崩れると、
    /// pricing.json を手で編集して9桁以上の単価を入れていた場合に、ユーザーが触っていない欄まで
    /// 表示書式で丸めて書き戻してしまう（月間上限額の往復不変性バグと同種のデータ破壊）。
    /// </summary>
    private static bool RunBuildPricingUneditedTests()
    {
        var original = new ModelPricing
        {
            InputUsdPerMillion = 0.123456789m,
            OutputUsdPerMillion = 2.987654321m,
            UpdatedAt = "2026-07-29",
        };

        string inputText = SettingsFieldFormatting.FormatUnitPrice(original.InputUsdPerMillion);
        string outputText = SettingsFieldFormatting.FormatUnitPrice(original.OutputUsdPerMillion);

        // 表示書式（小数点以下最大8桁）が9桁目を丸めていることの前提確認。
        bool displayRounds =
            inputText != original.InputUsdPerMillion.ToString() &&
            outputText != original.OutputUsdPerMillion.ToString();

        bool built = SettingsFieldFormatting.TryBuildPricing(
            inputText, outputText, original.UpdatedAt, original, out ModelPricing result, out string error);

        bool passed = displayRounds && built && error == "" &&
                      result.InputUsdPerMillion == original.InputUsdPerMillion &&
                      result.OutputUsdPerMillion == original.OutputUsdPerMillion &&
                      result.UpdatedAt == original.UpdatedAt;
        Console.WriteLine(
            "  TryBuildPricing 未編集時は元の高精度値をそのまま保つ（9桁以上の単価）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>編集後の値を正しく反映し、不正入力ではエラーを返すこと。</summary>
    private static bool RunBuildPricingEditedTests()
    {
        var original = new ModelPricing
        {
            InputUsdPerMillion = 0.30m,
            OutputUsdPerMillion = 2.50m,
            UpdatedAt = "2026-07-29",
        };

        bool editedApplied =
            SettingsFieldFormatting.TryBuildPricing(
                "0.40", "2.60", "2026-08-01", original, out ModelPricing edited, out string editedError) &&
            editedError == "" &&
            edited.InputUsdPerMillion == 0.40m &&
            edited.OutputUsdPerMillion == 2.60m &&
            edited.UpdatedAt == "2026-08-01";

        bool invalidInputRejected =
            !SettingsFieldFormatting.TryBuildPricing(
                "-1", "2.60", "2026-08-01", original, out _, out string inputError) &&
            inputError.Length > 0;

        bool invalidOutputRejected =
            !SettingsFieldFormatting.TryBuildPricing(
                "0.40", "abc", "2026-08-01", original, out _, out string outputError) &&
            outputError.Length > 0;

        bool invalidDateRejected =
            !SettingsFieldFormatting.TryBuildPricing(
                "0.40", "2.60", "2026/08/01", original, out _, out string dateError) &&
            dateError.Length > 0;

        bool passed = editedApplied && invalidInputRejected && invalidOutputRejected && invalidDateRejected;
        Console.WriteLine(
            "  TryBuildPricing 編集反映と不正入力拒否（入力単価・出力単価・更新日）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
