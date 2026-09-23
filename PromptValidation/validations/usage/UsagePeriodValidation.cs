using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary><see cref="UsagePeriod"/> の日/週/月境界計算の自己テスト。APIは呼ばない。</summary>
internal static class UsagePeriodValidation
{
    internal static bool RunSelfTests()
    {
        // 2026-07-27（月）を含む週。同じ週の木曜・日曜からも月曜0時に戻ることを確認する。
        DateTimeOffset mondayMorning = At(2026, 7, 27, 0, 0);
        DateTimeOffset thursdayLate = At(2026, 7, 30, 23, 59);
        DateTimeOffset sundayEnd = At(2026, 8, 2, 23, 59);
        // 2026-08-03は月曜。週をまたいでも正しく次の月曜へ切り替わることを確認する。
        DateTimeOffset nextMondayNoon = At(2026, 8, 3, 12, 0);

        bool weekFromMonday = UsagePeriod.StartOfWeek(mondayMorning) == At(2026, 7, 27, 0, 0);
        bool weekFromThursday = UsagePeriod.StartOfWeek(thursdayLate) == At(2026, 7, 27, 0, 0);
        bool weekFromSunday = UsagePeriod.StartOfWeek(sundayEnd) == At(2026, 7, 27, 0, 0);
        bool weekFromNextMonday = UsagePeriod.StartOfWeek(nextMondayNoon) == At(2026, 8, 3, 0, 0);

        bool dayBoundary = UsagePeriod.StartOfDay(thursdayLate) == At(2026, 7, 30, 0, 0);
        bool dayAtMidnightIsNoOp = UsagePeriod.StartOfDay(At(2026, 7, 30, 0, 0)) == At(2026, 7, 30, 0, 0);

        bool monthBoundary = UsagePeriod.StartOfMonth(thursdayLate) == At(2026, 7, 1, 0, 0);
        bool monthCrossYear = UsagePeriod.StartOfMonth(At(2026, 1, 15, 5, 0)) == At(2026, 1, 1, 0, 0);
        bool monthAtFirstIsNoOp = UsagePeriod.StartOfMonth(At(2026, 8, 1, 0, 0)) == At(2026, 8, 1, 0, 0);

        bool passed = weekFromMonday && weekFromThursday && weekFromSunday && weekFromNextMonday &&
            dayBoundary && dayAtMidnightIsNoOp &&
            monthBoundary && monthCrossYear && monthAtFirstIsNoOp;

        Console.WriteLine(
            "UsagePeriod（日/週(月曜始まり)/月のローカル境界）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));
}
