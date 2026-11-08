namespace JpScratch.Proofreading;

internal static class SubscriptionPolicy
{
    internal static TimeSpan Debounce(int milliseconds) => TimeSpan.FromMilliseconds(Math.Max(10000, milliseconds));
    internal static TimeSpan Interval(int seconds) => TimeSpan.FromSeconds(Math.Max(30, seconds));
    internal static bool IsFresh(DateTimeOffset checkedAt, DateTimeOffset now) => now >= checkedAt && now - checkedAt < TimeSpan.FromSeconds(60);
    internal static void RequireCompleted(string? status, string? finalText, GeminiUsage usage)
    {
        if (status != "completed" || string.IsNullOrWhiteSpace(finalText))
            throw new GeminiClientException(GeminiClientError.InvalidResponse, "完了した校正結果を取得できませんでした。", usage: usage);
    }
}
