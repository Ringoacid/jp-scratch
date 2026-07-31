using System.Globalization;

namespace JpScratch.Services;

/// <summary>月間上限に対する当月累計の状態（要件 3.6.3）。</summary>
internal enum UsageLimitState
{
    /// <summary>警告閾値未満、または上限が無制限（0以下）。</summary>
    Normal,

    /// <summary>警告閾値以上・上限未満。</summary>
    Warning,

    /// <summary>上限以上。</summary>
    Reached,
}

/// <summary>
/// 月間上限額に対する当月累計USDの判定（要件 3.6.3 / 発火条件5）。
/// WPF・DBに依存しない純粋関数として実装し、<c>PromptValidation</c> から単体で検証できるようにする。
/// 当月累計の取得は既存の <c>ApiCallRepository.GetUsageSummary</c> 経路（<c>MainWindow.RefreshUsageDisplay</c>）
/// を使い回し、ここでは二重にDBを読まない。
/// </summary>
internal static class UsageLimitService
{
    /// <summary>
    /// 上限額から見た当月累計の状態。
    /// 上限が0以下（無制限）なら常に <see cref="UsageLimitState.Normal"/>。
    /// 判定は「送信前の当月累計が上限以上かどうか」のみ。今回の送信で超える見込みかの事前見積りはしない
    /// （出力トークン数が送信前には分からないため）。
    /// </summary>
    internal static UsageLimitState Evaluate(decimal monthUsd, decimal limitUsd, decimal warningRatio)
    {
        if (limitUsd <= 0m) return UsageLimitState.Normal;
        if (monthUsd >= limitUsd) return UsageLimitState.Reached;
        if (warningRatio > 0m && monthUsd >= limitUsd * warningRatio) return UsageLimitState.Warning;
        return UsageLimitState.Normal;
    }

    /// <summary>上限に達しているか（0以下＝無制限は常にfalse）。</summary>
    internal static bool IsReached(decimal monthUsd, decimal limitUsd)
        => limitUsd > 0m && monthUsd >= limitUsd;

    /// <summary>
    /// 進捗バー用の0〜100の割合。上限が無制限（0以下）なら <c>null</c>
    /// （呼び出し側はこれをバー非表示の合図として扱う）。100を超えても100に丸める。
    /// </summary>
    internal static double? ProgressPercent(decimal monthUsd, decimal limitUsd)
    {
        if (limitUsd <= 0m) return null;
        decimal ratio = monthUsd / limitUsd;
        if (ratio < 0m) ratio = 0m;
        if (ratio > 1m) ratio = 1m;
        return (double)(ratio * 100m);
    }
}

/// <summary>
/// 月間上限到達のトレイ通知を「年月＋上限額」単位で一度だけに抑える。
/// 入力停止のたびに自動チェックのガードへ引っかかっても通知が繰り返されないようにするための状態。
/// 月が変わるか、設定画面で上限額そのものが変更されたら、鍵（年月＋上限額）が変わるので再度通知できる。
/// WPF・DBに依存しないため、単体テストではDateTimeOffsetを差し替えて月替りを再現できる。
/// </summary>
internal sealed class UsageLimitNotificationTracker
{
    private string? _notifiedKey;

    /// <summary>今この状態で通知してよいか（＝直近に同じ鍵で通知済みでないか）。</summary>
    internal bool ShouldNotify(DateTimeOffset now, decimal limitUsd) => _notifiedKey != BuildKey(now, limitUsd);

    /// <summary>通知したことを記録する。</summary>
    internal void MarkNotified(DateTimeOffset now, decimal limitUsd) => _notifiedKey = BuildKey(now, limitUsd);

    /// <summary>テスト・設定リセット用に通知済み状態を消す。</summary>
    internal void Reset() => _notifiedKey = null;

    private static string BuildKey(DateTimeOffset now, decimal limitUsd)
    {
        DateTime local = now.LocalDateTime;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{local.Year:D4}-{local.Month:D2}|{limitUsd.ToString(CultureInfo.InvariantCulture)}");
    }
}
