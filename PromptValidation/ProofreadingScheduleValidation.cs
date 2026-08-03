using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class ProofreadingScheduleValidation
{
    internal static bool RunSelfTests()
    {
        DateTimeOffset origin =
            new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(9));
        var schedule = new ProofreadingSchedule
        {
            Debounce = TimeSpan.FromSeconds(2),
            MinimumSendInterval = TimeSpan.FromSeconds(10),
        };

        schedule.NotifyChanged("tab-a", origin);
        bool debounce =
            schedule.GetAutomaticDueAt("tab-a") == origin.AddSeconds(2) &&
            !schedule.IsAutomaticDue("tab-a", origin.AddMilliseconds(1999)) &&
            schedule.IsAutomaticDue("tab-a", origin.AddSeconds(2));

        schedule.MarkSent(origin.AddSeconds(1));
        bool minimum =
            schedule.GetAutomaticDueAt("tab-a") == origin.AddSeconds(11) &&
            schedule.GetDelayBeforeSend(origin.AddSeconds(4)) ==
                TimeSpan.FromSeconds(7) &&
            schedule.GetDelayBeforeSend(origin.AddSeconds(11)) == TimeSpan.Zero;

        schedule.NotifyChanged("tab-b", origin.AddSeconds(20));
        bool perTab =
            schedule.GetAutomaticDueAt("tab-b") == origin.AddSeconds(22) &&
            schedule.GetAutomaticDueAt("tab-a") == origin.AddSeconds(11);

        schedule.MarkAutomaticHandled("tab-a");
        bool handled =
            schedule.GetAutomaticDueAt("tab-a") is null &&
            schedule.GetAutomaticDueAt("tab-b") is not null;

        // 既定デバウンス5秒（proofreading-ux-fixes-plan.md §7.1）: 4.9秒では送信対象にならず、
        // 5秒で送信対象になる。手動校正はスケジュールを通らない（呼び出し側が直接開始する）ため、
        // このテストは「自動送信の境界」だけを固定する。
        var scheduleFive = new ProofreadingSchedule(); // 既定 = 5秒
        scheduleFive.NotifyChanged("tab-c", origin);
        bool defaultDebounce =
            !scheduleFive.IsAutomaticDue("tab-c", origin.AddMilliseconds(4900)) &&
            scheduleFive.IsAutomaticDue("tab-c", origin.AddSeconds(5)) &&
            scheduleFive.GetAutomaticDueAt("tab-c") == origin.AddSeconds(5);

        bool passed = debounce && minimum && perTab && handled && defaultDebounce;
        Console.WriteLine(
            $"自動校正スケジュール（デバウンス・最小間隔・タブ分離）: " +
            $"{(passed ? "PASS" : "FAIL")}");
        return passed;
    }
}
