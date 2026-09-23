using System.Globalization;
using JpScratch.Models;

namespace JpScratch.Services;

internal sealed partial class ApiCallRepository
{
    internal long GetSubscriptionCallCount(DateTimeOffset? from = null, DateTimeOffset? to = null, IReadOnlyCollection<ApiCallTrigger>? triggers = null)
    {
        if (from is not null && to is not null && from >= to)
            throw new ArgumentException("期間の開始は終了より前である必要があります。");
        long count = 0;
        _database.InTransaction(db =>
        {
            // Read only counting fields, with an index-friendly date prefilter. Exact comparisons
            // stay in DateTimeOffset to preserve offsets, DST and sub-millisecond boundaries.
            var conditions = new List<string> { "backend <> 0" };
            var parameters = new List<(string Name, object? Value)>();
            if (from is { } start && start > DateTimeOffset.MinValue.AddDays(2))
            {
                conditions.Add("called_at >= $lower");
                parameters.Add(("$lower", ToStorageValue(start.AddDays(-2))));
            }
            if (to is { } end && end < DateTimeOffset.MaxValue.AddDays(-2))
            {
                conditions.Add("called_at < $upper");
                parameters.Add(("$upper", ToStorageValue(end.AddDays(2))));
            }
            count = db.Read("SELECT called_at, trigger_type, status, usd_cost FROM api_calls WHERE " +
                string.Join(" AND ", conditions) + ";", reader =>
            {
                long matches = 0;
                while (reader.Read())
                {
                    if (!DateTimeOffset.TryParse(reader.GetString(0), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var calledAt) ||
                        (from is not null && calledAt < from) || (to is not null && calledAt >= to) ||
                        !TryFromStorageTrigger(reader.GetString(1), out var trigger) ||
                        (triggers is { Count: > 0 } && !triggers.Contains(trigger)) ||
                        !TryFromStorageStatus(reader.GetString(2), out _) ||
                        !decimal.TryParse(reader.GetString(3), NumberStyles.Number, CultureInfo.InvariantCulture, out _)) continue;
                    matches++;
                }
                return matches;
            }, parameters.ToArray());
            db.Read("SELECT day, call_cnt, trigger_type FROM subscription_daily;", reader =>
            {
                while (reader.Read())
                {
                    if (triggers is { Count: > 0 } && (!TryFromStorageTrigger(reader.GetString(2), out var trigger) || !triggers.Contains(trigger))) continue;
                    if (!DateTime.TryParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var day)) continue;
                    var date = new DateTimeOffset(day);
                    if ((from is null || date >= from) && (to is null || date < to)) count += reader.GetInt64(1);
                }
                return 0;
            });
        });
        return count;
    }

    private ApiCallCompactionResult CompactSubscriptions(DateTimeOffset cutoff, out HashSet<DateOnly> affectedDays)
    {
        int calls = 0, unlinked = 0;
        HashSet<DateOnly> days = [];
        _database.InTransaction(db =>
        {
            var rows = GetHistory(to: cutoff, limit: int.MaxValue).Rows.Where(row => row.Backend != BackendKind.Api).ToArray();
            foreach (var row in rows)
            {
                string day = row.CalledAt.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                db.Execute("""
                    INSERT INTO subscription_daily(day, backend, model, trigger_type, status, call_cnt,
                        prompt_tokens, output_tokens, unknown_usage_calls, suggestion_cnt, discarded_cnt, subscription_units, subscription_unit)
                    VALUES($day, $backend, $model, $trigger, $status, 1, $input, $output, $unknown, $suggestions, $discarded, $units, $unit)
                    ON CONFLICT(day, backend, model, trigger_type, status, subscription_unit) DO UPDATE SET
                        call_cnt = call_cnt + 1, prompt_tokens = prompt_tokens + excluded.prompt_tokens,
                        output_tokens = output_tokens + excluded.output_tokens,
                        unknown_usage_calls = unknown_usage_calls + excluded.unknown_usage_calls,
                        suggestion_cnt = suggestion_cnt + excluded.suggestion_cnt,
                        discarded_cnt = discarded_cnt + excluded.discarded_cnt,
                        subscription_units = CASE WHEN subscription_units IS NULL OR excluded.subscription_units IS NULL THEN NULL
                            ELSE subscription_units + excluded.subscription_units END;
                    """, ("$day", day), ("$backend", (int)row.Backend), ("$model", row.Model),
                    ("$trigger", ToStorageValue(row.Trigger)), ("$status", ToStorageValue(row.Status)),
                    ("$input", row.PromptTokens), ("$output", row.OutputTokens), ("$unknown", row.IsUsageKnown ? 0 : 1),
                    ("$suggestions", row.SuggestionCount), ("$discarded", row.DiscardedCount),
                    ("$units", row.SubscriptionUnits), ("$unit", row.SubscriptionUnit ?? ""));
                unlinked += db.Execute("UPDATE reactions SET api_call_id = NULL WHERE api_call_id = $id;", ("$id", row.Id));
                db.Execute("DELETE FROM api_calls WHERE id = $id;", ("$id", row.Id));
                calls++; days.Add(DateOnly.FromDateTime(row.CalledAt.LocalDateTime));
            }
        });
        affectedDays = days;
        return new(calls, days.Count, unlinked);
    }
}
