using System.Text.Json;

namespace JpScratch.Proofreading;

internal static class CopilotQuota
{
    internal static (double? UsedPercent, DateTimeOffset? ResetsAt) Read(JsonElement quota, DateTimeOffset checkedAt)
    {
        double? used = null;
        bool hasMonthlyAllowance = false;
        JsonElement snapshots = quota.Get("quotaSnapshots");
        if (snapshots.ValueKind == JsonValueKind.Object)
            foreach (var entry in snapshots.EnumerateObject())
            {
                var value = entry.Value;
                if (value.Number("entitlementRequests") == -1 || value.Get("isUnlimitedEntitlement").ValueKind == JsonValueKind.True)
                {
                    used ??= 0;
                    continue;
                }
                if (value.Number("remainingPercentage") is { } remaining)
                {
                    used = Math.Max(used ?? 0, Math.Clamp(100 - remaining, 0, 100));
                    hasMonthlyAllowance = true;
                }
            }

        // Monthly AI Credits reset on the first day at 00:00 UTC, independently
        // of billing dates. CLI snapshot resetDate can instead be its fetch time.
        // https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing#how-do-ai-credits-work
        var utc = checkedAt.ToUniversalTime();
        DateTimeOffset? reset = hasMonthlyAllowance
            ? new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)
            : null;
        return (used, reset);
    }
}
