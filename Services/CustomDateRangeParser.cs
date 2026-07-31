using System.Globalization;

namespace JpScratch.Services;

/// <summary>
/// 課金履歴画面のカスタム期間入力（<c>yyyy-MM-dd</c>）を解析する、UI に依存しない純粋関数。
/// 終了日は含む扱いとし、呼び出し側（<c>Views.BillingHistoryWindow</c>）が渡す
/// <see cref="ApiCallRepository.GetHistory"/> / <see cref="ApiCallRepository.GetUsageSummary"/> の
/// 半開区間に合わせて、内部では翌日 0 時を排他的な上限として返す。
/// </summary>
internal static class CustomDateRangeParser
{
    internal readonly record struct Result(DateTimeOffset? From, DateTimeOffset? To, string? Error)
    {
        internal bool IsError => Error is not null;
    }

    internal static Result Parse(string fromText, string toText)
    {
        if (!DateOnly.TryParseExact(
                (fromText ?? "").Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start) ||
            !DateOnly.TryParseExact(
                (toText ?? "").Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly end))
        {
            return new Result(null, null, "日付は yyyy-MM-dd 形式で入力してください");
        }

        if (start > end)
            return new Result(null, null, "開始日は終了日より前の日付にしてください");

        // 開始日・終了日それぞれが DateOnly の表現範囲の端（例: 0001-01-01 や 9999-12-31）だと、
        // ローカルタイムゾーンへのUTC変換（開始日側で特に負のオフセットの環境が影響）や
        // 翌日への繰り上げ＋変換（終了日側）で DateTime/DateTimeOffset の表現範囲を超えうる。
        // 例外で落とさず、他の入力エラーと同じくメッセージとして返す。from/to を同じ try に
        // 入れると、開始日側の失敗でも終了日向けの文言を返してしまうため、個別に判定する。
        DateTimeOffset from;
        try
        {
            from = new DateTimeOffset(new DateTime(
                start.Year, start.Month, start.Day, 0, 0, 0, DateTimeKind.Local));
        }
        catch (ArgumentOutOfRangeException)
        {
            return new Result(null, null, "開始日が扱える範囲を超えています。もう少し後の日付を指定してください");
        }

        DateTimeOffset toExclusive;
        try
        {
            toExclusive = new DateTimeOffset(new DateTime(
                end.Year, end.Month, end.Day, 0, 0, 0, DateTimeKind.Local).AddDays(1));
        }
        catch (ArgumentOutOfRangeException)
        {
            return new Result(null, null, "終了日が扱える範囲を超えています。もう少し前の日付を指定してください");
        }

        return new Result(from, toExclusive, null);
    }

    /// <summary>
    /// プリセット期間（当日/当週/当月など）が実際にクエリへ渡す半開区間を、カスタム入力欄の
    /// 「終了日は含む」規約に合わせて <c>yyyy-MM-dd</c> の文字列へ書き戻す。
    /// ここで作った文字列を <see cref="Parse"/> に戻すと同じ区間になることを、プリセット切替時に
    /// 表示がずれないための往復一致として <c>PromptValidation</c> で確認している。
    /// </summary>
    internal static (string From, string To) FormatInclusive(DateTimeOffset from, DateTimeOffset toExclusive)
    {
        DateOnly fromDate = DateOnly.FromDateTime(from.LocalDateTime);
        // 終了日は「翌日0時（排他的）」から1日引いて「含む最終日」に変換する。
        DateOnly toDateInclusive = DateOnly.FromDateTime(toExclusive.LocalDateTime.AddDays(-1));
        return (
            fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            toDateInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
