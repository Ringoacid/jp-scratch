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

        bool passed = debounce && minimum && perTab && handled;
        Console.WriteLine(
            $"自動校正スケジュール（デバウンス・最小間隔・タブ分離）: " +
            $"{(passed ? "PASS" : "FAIL")}");
        return passed;
    }
}
