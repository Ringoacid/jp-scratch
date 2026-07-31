namespace JpScratch.Services;

/// <summary>
/// 課金ログの保持期限（要件 3.6.2「保持期間は既定 12 か月。それ以前は日次サマリに圧縮して明細を削除する」）。
/// WPF・SQLiteに依存しない純粋関数だけを置く。
/// </summary>
internal static class ApiLogRetention
{
    /// <summary>
    /// 明細を圧縮する境界時刻を返す。これより前（<c>&lt;</c>）の明細が圧縮対象。
    /// <see langword="null"/> なら圧縮しない。
    ///
    /// 境界は**月初のローカル0時**に揃える。日単位で刻むと、保持期間ちょうどの
    /// 「昨日の明細」が今日になって消えるような、ユーザーから見て説明しづらい挙動になる。
    /// 月初で刻めば「当月を含む直近 N か月ぶんの明細が残り、それより古い月が圧縮される」と
    /// 一言で言える（<paramref name="retentionMonths"/> = 12・現在 2026-07 なら境界は 2025-07-01）。
    /// この定義では、削除されるのは必ず「今より <paramref name="retentionMonths"/> か月以上前」の
    /// 明細に限られる。
    ///
    /// <paramref name="retentionMonths"/> が 0 以下なら無期限（<see langword="null"/>）。
    /// 保持期間が暦の開始を超える場合も、圧縮対象が存在しえないので <see langword="null"/> を返す
    /// （<see cref="DateTime.AddMonths"/> の範囲外例外を起こさないため、月数の算術で判定する）。
    /// </summary>
    internal static DateTimeOffset? ComputeCutoff(DateTimeOffset now, int retentionMonths)
    {
        if (retentionMonths <= 0) return null;

        DateTime local = now.LocalDateTime;
        int monthsSinceEpoch = (local.Year * 12) + (local.Month - 1) - retentionMonths;
        int year = monthsSinceEpoch / 12;
        int month = (monthsSinceEpoch % 12) + 1;
        if (monthsSinceEpoch < 0 || year < 1 || year > 9999) return null;

        return new DateTimeOffset(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Local));
    }
}
