namespace JpScratch.Services;

/// <summary>
/// 課金集計・課金履歴画面で共有する、ローカル時刻ベースの期間境界計算。
/// <see cref="DateTime.Kind"/> を <see cref="DateTimeKind.Local"/> にしてから
/// <see cref="DateTimeOffset"/> へ変換することで、その日付時点の実オフセット
/// （DST の有無）を正しく反映する。
/// </summary>
internal static class UsagePeriod
{
    /// <summary>ローカル日の開始（00:00）。</summary>
    internal static DateTimeOffset StartOfDay(DateTimeOffset now)
    {
        DateTime local = now.LocalDateTime;
        return new DateTimeOffset(new DateTime(
            local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Local));
    }

    /// <summary>
    /// ローカル週の開始（00:00）。要件書（requirements.md §3.6）に週の開始曜日の定義がないため、
    /// ISO 8601 に合わせて月曜始まりを採用する。
    /// </summary>
    internal static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        DateTime localDate = now.LocalDateTime.Date;
        // DayOfWeek は日曜=0起点なので、月曜=0となるよう7を法にずらす。
        int daysSinceMonday = ((int)localDate.DayOfWeek + 6) % 7;
        DateTime mondayLocal = localDate.AddDays(-daysSinceMonday);
        return new DateTimeOffset(new DateTime(
            mondayLocal.Year, mondayLocal.Month, mondayLocal.Day, 0, 0, 0, DateTimeKind.Local));
    }

    /// <summary>ローカル月の開始（1日 00:00）。</summary>
    internal static DateTimeOffset StartOfMonth(DateTimeOffset now)
    {
        DateTime local = now.LocalDateTime;
        return new DateTimeOffset(new DateTime(
            local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Local));
    }
}
