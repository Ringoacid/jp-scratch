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
}
