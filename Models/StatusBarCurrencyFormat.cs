namespace JpScratch.Models;

/// <summary>
/// ステータスバー下段の料金表示形式（docs/proofreading-ux-fixes-plan.md §8.2）。
/// 有効にした「直近・起動後・当日・当月」の全項目へ共通適用する。
/// 未知の値は <see cref="JpScratch.Services.SettingsService"/> の正規化で円表示へ戻す。
/// </summary>
public enum StatusBarCurrencyFormat
{
    /// <summary>円表示（既定）。円換算できない場合は安全な欠損表示（¥—）を出す。</summary>
    Jpy,

    /// <summary>ドル表示。</summary>
    Usd,

    /// <summary>ドルと円の両方を表示する。</summary>
    Both,
}
