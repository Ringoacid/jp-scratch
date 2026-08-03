namespace JpScratch.Proofreading;

/// <summary>
/// 自動校正のデバウンスと、全API送信に共通する最小間隔を時刻だけで判定する。
/// UIタイマーや通信を持たないため、境界時刻を決定的に検証できる。
/// </summary>
internal sealed class ProofreadingSchedule
{
    private readonly Dictionary<string, DateTimeOffset> _lastChanges = [];
    private DateTimeOffset? _lastSentAt;

    /// <summary>既定は 5 秒（proofreading-ux-fixes-plan.md §7.1）。設定から上書きされる。</summary>
    internal TimeSpan Debounce { get; set; } = TimeSpan.FromSeconds(5);
    internal TimeSpan MinimumSendInterval { get; set; } = TimeSpan.FromSeconds(10);

    internal void NotifyChanged(string tabId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        _lastChanges[tabId] = now;
    }

    internal DateTimeOffset? GetAutomaticDueAt(string tabId)
    {
        if (!_lastChanges.TryGetValue(tabId, out DateTimeOffset changedAt))
            return null;

        DateTimeOffset due = changedAt + Debounce;
        if (_lastSentAt is { } lastSent)
            due = Later(due, lastSent + MinimumSendInterval);
        return due;
    }

    internal bool IsAutomaticDue(string tabId, DateTimeOffset now)
        => GetAutomaticDueAt(tabId) is { } due && now >= due;

    internal TimeSpan GetDelayBeforeSend(DateTimeOffset now)
    {
        if (_lastSentAt is not { } lastSent)
            return TimeSpan.Zero;

        TimeSpan remaining = lastSent + MinimumSendInterval - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal void MarkSent(DateTimeOffset now) => _lastSentAt = now;

    internal void MarkAutomaticHandled(string tabId)
        => _lastChanges.Remove(tabId);

    private static DateTimeOffset Later(DateTimeOffset left, DateTimeOffset right)
        => left >= right ? left : right;
}
