using System.Globalization;
using System.Text.Json;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class CopilotQuotaValidation
{
    internal static bool Run()
    {
        bool passed = true;
        void Check(string name, bool value)
        {
            Console.WriteLine($"Copilot利用枠（{name}）: {(value ? "PASS" : "FAIL")}");
            passed &= value;
        }
        static DateTimeOffset Date(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        static JsonElement Quota(string? resetDate) => JsonSerializer.SerializeToElement(new
        {
            quotaSnapshots = new { premium_interactions = new { remainingPercentage = 94.9, resetDate } },
        });

        foreach (var (name, fetchedAt, expectedReset) in new[]
        {
            ("取得日時をリセット日時にしない", "2026-09-12T13:17:00+09:00", "2026-10-01T00:00:00Z"),
            ("UTC月末の直前", "2026-09-30T23:59:59.9999999Z", "2026-10-01T00:00:00Z"),
            ("UTC月初の瞬間から翌月", "2026-10-01T00:00:00Z", "2026-11-01T00:00:00Z"),
            ("日本時間は翌月でもUTC月を使う", "2026-10-01T08:59:59+09:00", "2026-10-01T00:00:00Z"),
            ("負の時差で現地は前月でもUTC月を使う", "2026-09-30T20:00:00-04:00", "2026-11-01T00:00:00Z"),
            ("年越し", "2026-12-31T23:59:59Z", "2027-01-01T00:00:00Z"),
            ("うるう年の2月", "2028-02-29T12:00:00Z", "2028-03-01T00:00:00Z"),
        })
        {
            var actual = CopilotQuota.Read(Quota(fetchedAt), Date(fetchedAt));
            Check(name, actual.ResetsAt == Date(expectedReset) && actual.ResetsAt?.Offset == TimeSpan.Zero &&
                actual.UsedPercent is { } used && Math.Abs(used - 5.1) < 0.00001);
        }

        var checkedAt = Date("2026-09-12T04:17:00Z");
        foreach (string? invalidReset in new[] { null, "invalid", "2026-09-12T04:16:00Z", "2026-10-17T08:30:00Z" })
        {
            var actual = CopilotQuota.Read(Quota(invalidReset), checkedAt);
            Check("CLIリセット日時に依存しない: " + (invalidReset ?? "欠落"), actual.ResetsAt == Date("2026-10-01T00:00:00Z"));
        }

        var mixed = CopilotQuota.Read(JsonSerializer.SerializeToElement(new
        {
            quotaSnapshots = new
            {
                unlimited = new { entitlementRequests = -1, remainingPercentage = 0 },
                monthly = new { remainingPercentage = 100 },
            },
        }), checkedAt);
        Check("無制限枠と併存する未使用の月間枠もリセット日を表示",
            mixed.UsedPercent == 0 && mixed.ResetsAt == Date("2026-10-01T00:00:00Z"));
        var unlimited = CopilotQuota.Read(JsonSerializer.SerializeToElement(new
        {
            quotaSnapshots = new { unlimited = new { isUnlimitedEntitlement = true, remainingPercentage = 0 } },
        }), checkedAt);
        Check("無制限枠だけなら月間リセット日を付加しない", unlimited.UsedPercent == 0 && unlimited.ResetsAt is null);
        var unknown = CopilotQuota.Read(JsonSerializer.SerializeToElement(new { }), checkedAt);
        Check("残量不明を残量ありに変えない", unknown.UsedPercent is null && unknown.ResetsAt is null);
        return passed;
    }
}
