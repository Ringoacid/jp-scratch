using System.Globalization;

namespace JpScratch.Services;

/// <summary>
/// 設定画面の数値欄における「表示 → パース」往復不変性を担保する純粋関数群。
/// <see cref="JpScratch.Views.SettingsWindow"/> のコードビハインドはWPFに依存し単体テストしにくいため、
/// 往復させたい計算だけをここへ切り出してある（WPF非依存）。
///
/// レビュー指摘の再現: 月間上限額の表示に <c>"0.##"</c>（小数2桁）を使うと、
/// <c>0.0032</c> を保存した直後に設定画面を開き直すと表示が <c>"0"</c> になり、
/// 保存を押すと上限が 0（無制限）へ黙って壊れる。表示は <see cref="UsageFormatting.FormatUsd"/>
/// と同じ書式（小数点以下最大8桁、CLAUDE.mdの既存規約）を再利用し、書式のずれを防ぐ。
/// 警告閾値（パーセント表示）も同じ性質のバグを持つため、同じ考え方で往復不変にする。
/// </summary>
internal static class SettingsFieldFormatting
{
    /// <summary>
    /// 月間上限額（USD）の表示。0 は「無制限」という正当な値であり、そのまま "0" と表示する
    /// （<see cref="UsageLimitService"/> や <see cref="Models.AppSettings.MonthlyLimitUsd"/> の規約どおり）。
    /// </summary>
    internal static string FormatMonthlyLimitUsd(decimal value) => UsageFormatting.FormatUsd(value);

    /// <summary>
    /// 警告閾値（0〜1の割合）をパーセント表示にする。USDと同じ小数点以下最大8桁を使うことで、
    /// 表示→パースの往復で値が変わらないようにする。
    /// </summary>
    internal static string FormatWarningPercent(decimal ratio) =>
        (ratio * 100m).ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>
    /// テキスト欄から decimal へ。パースに失敗した入力は <paramref name="fallback"/>（変更前の値）を返し、
    /// 欄を壊れた状態のまま保存させない（<see cref="JpScratch.Views.SettingsWindow"/> の他の数値欄と同じ規約）。
    /// </summary>
    internal static decimal ParseDecimalOrDefault(string text, decimal fallback) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : fallback;

    /// <summary>
    /// モデル単価（USD / 1M tokens）の表示。往復不変にするため <see cref="UsageFormatting.FormatUsd"/>
    /// と同じ最大8桁を使う。
    /// </summary>
    internal static string FormatUnitPrice(decimal value) => UsageFormatting.FormatUsd(value);

    /// <summary>
    /// 単価は非負・上限以下の decimal のみ受け付ける（InvariantCulture 固定）。
    /// 上限は <see cref="PricingService.MaxUnitPriceUsdPerMillion"/>。上限がないと巨大な値を
    /// 保存したとき <see cref="PricingService.Calculate"/> の decimal 演算がオーバーフローする。
    /// </summary>
    internal static bool TryParseUnitPrice(string text, out decimal value) =>
        decimal.TryParse(
            (text ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
        value >= 0m &&
        value <= PricingService.MaxUnitPriceUsdPerMillion;

    /// <summary>更新日は yyyy-MM-dd 厳密（InvariantCulture 固定）。前後の空白は許す。</summary>
    internal static bool TryParseUpdatedAt(string text, out string normalized)
    {
        if (DateOnly.TryParseExact(
                (text ?? "").Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        normalized = "";
        return false;
    }

    /// <summary>
    /// 設定画面の3欄（入力単価・出力単価・更新日）から <see cref="ModelPricing"/> を作る。
    ///
    /// 3欄すべてが <paramref name="original"/> を書式化した文字列と一致するなら、パースし直さず
    /// <paramref name="original"/> をそのまま返す。表示書式は小数点以下最大8桁なので、
    /// <c>pricing.json</c> を手で編集して9桁以上の単価を入れていた場合、ユーザーが触っていない欄まで
    /// 表示書式で丸めて書き戻してしまう（月間上限額の往復不変性バグと同種のデータ破壊）。
    /// 未編集の欄は元の値をそのまま通すことで防ぐ。
    /// </summary>
    internal static bool TryBuildPricing(
        string inputText,
        string outputText,
        string updatedAtText,
        ModelPricing original,
        out ModelPricing result,
        out string error)
    {
        string inputTrimmed = (inputText ?? "").Trim();
        string outputTrimmed = (outputText ?? "").Trim();
        string updatedAtTrimmed = (updatedAtText ?? "").Trim();

        bool unedited =
            inputTrimmed == FormatUnitPrice(original.InputUsdPerMillion) &&
            outputTrimmed == FormatUnitPrice(original.OutputUsdPerMillion) &&
            updatedAtTrimmed == original.UpdatedAt;
        if (unedited)
        {
            result = original;
            error = "";
            return true;
        }

        if (!TryParseUnitPrice(inputTrimmed, out decimal input))
        {
            result = original;
            error = "入力単価は 0 以上の数値で入力してください。";
            return false;
        }

        if (!TryParseUnitPrice(outputTrimmed, out decimal output))
        {
            result = original;
            error = "出力単価は 0 以上の数値で入力してください。";
            return false;
        }

        if (!TryParseUpdatedAt(updatedAtTrimmed, out string normalizedUpdatedAt))
        {
            result = original;
            error = "更新日は yyyy-MM-dd 形式で入力してください。";
            return false;
        }

        result = new ModelPricing
        {
            // 通貨は編集対象ではないので必ず引き継ぐ。既定値へ落とすと、円建てモデル（PLaMo）を
            // 設定画面で編集した瞬間に ¥60 が $60 として扱われ、料金表示が桁違いに狂う。
            Currency = original.Currency,
            InputUsdPerMillion = input,
            OutputUsdPerMillion = output,
            UpdatedAt = normalizedUpdatedAt,
        };
        error = "";
        return true;
    }
}
