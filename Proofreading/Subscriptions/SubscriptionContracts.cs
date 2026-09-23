using System.Text.Json;
using JpScratch.Models;

namespace JpScratch.Proofreading;

internal sealed class SubscriptionRequestStaleException : Exception;

internal sealed record SubscriptionModel(string Id, string Name, double? Multiplier = null,
    double? InputPrice = null, double? OutputPrice = null);
internal sealed record SubscriptionState(bool Authenticated, string? Account, string? Plan,
    double? UsedPercent, DateTimeOffset? ResetsAt, DateTimeOffset CheckedAt,
    IReadOnlyList<SubscriptionModel> Models)
{
    internal bool Exhausted => UsedPercent >= 100;
    internal string Description => !Authenticated ? "未ログイン" :
        $"{Account} {Plan}\n" + (UsedPercent is { } used ? $"利用率 {used:0.#}%" : "残量不明") +
        (ResetsAt is { } reset ? $" ／ リセット {reset.LocalDateTime:g}" : "") +
        $"\n取得時刻 {CheckedAt.LocalDateTime:g}" + (UsedPercent >= 80 ? "\n利用枠が少なくなっています。" : "");
}

internal interface ISubscriptionBackend : IDisposable, IAsyncDisposable
{
    // Codex process termination can wait too. Keep the default cleanup off the UI thread.
    ValueTask IAsyncDisposable.DisposeAsync() => new(Task.Run(Dispose));
    Task<SubscriptionState> ReadStateAsync(CancellationToken token);
    Task LoginAsync(bool deviceCode, Action<string> showCode, CancellationToken token,
        Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null);
    Task LogoutAsync(CancellationToken token);
    Task<GeminiRawTextResult> GenerateAsync(string model, string instructions, string prompt, CancellationToken token);
}

internal static class SubscriptionJson
{
    internal static JsonElement Get(this JsonElement root, string name)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) ? value : default;
    internal static string? Text(this JsonElement root, string name) => root.Get(name).ValueKind == JsonValueKind.String ? root.Get(name).GetString() : null;
    internal static double? Number(this JsonElement root, string name) => root.Get(name).TryNumber();
    internal static double? TryNumber(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double n) ? n : null;
    internal static IEnumerable<JsonElement> Items(this JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
}
