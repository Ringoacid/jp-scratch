using System.Globalization;
using System.IO;

namespace JpScratch.Services;

internal enum ApiCallTrigger
{
    Auto,
    Manual,
    Realternative,
    StyleGuide,
}

internal enum ApiCallStatus
{
    Ok,
    Error,
    Timeout,
}

/// <summary>課金の唯一の情報源となる、1回の論理API呼び出しの記録。</summary>
internal sealed record ApiCallLogEntry(
    ApiCallTrigger Trigger,
    string Model,
    int PromptTokens,
    int OutputTokens,
    decimal UsdCost,
    long DurationMilliseconds,
    ApiCallStatus Status,
    string? ErrorMessage,
    int SuggestionCount,
    int DiscardedCount,
    FxRate? FxRate = null);

/// <summary>期間内のAPI呼び出しログを、料金精度を失わずに集計した値。</summary>
internal sealed record ApiCallUsageSummary(
    long TotalCalls,
    long OkCalls,
    long ErrorCalls,
    long TimeoutCalls,
    long PromptTokens,
    long OutputTokens,
    decimal UsdCost,
    long SuggestionCount,
    long DiscardedCount,
    decimal JpyCost,
    bool IsJpyComplete,
    decimal? SingleUsdJpyRate,
    DateOnly? SingleRateDate,
    DateOnly? FirstRateDate,
    DateOnly? LastRateDate,
    int DistinctRateCount)
{
    internal static ApiCallUsageSummary Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0m, 0, 0, 0m, true,
        null, null, null, null, 0);
}

/// <summary>表示用に取得する、直近1件の永続API呼び出しログ。</summary>
internal sealed record ApiCallLog(
    long Id,
    DateTimeOffset CalledAt,
    int PromptTokens,
    int OutputTokens,
    decimal UsdCost,
    decimal? UsdJpyRate,
    DateOnly? RateDate,
    decimal? JpyCost,
    ApiCallStatus Status,
    int SuggestionCount,
    int DiscardedCount);

/// <summary>課金履歴画面の明細1行。</summary>
internal sealed record ApiCallHistoryRow(
    long Id,
    DateTimeOffset CalledAt,
    ApiCallTrigger Trigger,
    string Model,
    int PromptTokens,
    int OutputTokens,
    decimal UsdCost,
    decimal? UsdJpyRate,
    DateOnly? RateDate,
    decimal? JpyCost,
    long DurationMilliseconds,
    ApiCallStatus Status,
    string? ErrorMessage,
    int SuggestionCount,
    int DiscardedCount);

/// <summary>
/// <see cref="ApiCallRepository.GetHistory"/> の結果。<paramref name="TotalCount"/> は
/// <paramref name="Rows"/> を <c>limit</c> で切り詰める前の、条件に合致した総件数。
/// </summary>
internal sealed record ApiCallHistoryPage(
    IReadOnlyList<ApiCallHistoryRow> Rows,
    long TotalCount,
    bool Truncated);

/// <summary>Gemini API呼び出しの課金・結果ログを永続化する。</summary>
internal sealed class ApiCallRepository
{
    private readonly Database _database;
    private readonly Func<DateTimeOffset> _now;

    internal ApiCallRepository(Database database, Func<DateTimeOffset>? now = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    internal long Add(ApiCallLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Model);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.PromptTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.OutputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.UsdCost);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.DurationMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.SuggestionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(entry.DiscardedCount);
        if (entry.FxRate is { UsdJpy: <= 0m })
            throw new ArgumentOutOfRangeException(nameof(entry), "為替レートは正数である必要があります。");

        FxRate? fxRate = entry.FxRate;
        decimal? jpyCost = fxRate is null ? null : entry.UsdCost * fxRate.UsdJpy;
        return _database.Read(
            """
            INSERT INTO api_calls (
                called_at, trigger_type, model, prompt_tokens, output_tokens,
                usd_cost, usd_jpy_rate, rate_date, jpy_cost, duration_ms,
                status, error_message, suggestion_cnt, discarded_cnt)
            VALUES (
                $called_at, $trigger_type, $model, $prompt_tokens, $output_tokens,
                $usd_cost, $usd_jpy_rate, $rate_date, $jpy_cost, $duration_ms,
                $status, $error_message, $suggestion_cnt, $discarded_cnt)
            RETURNING id;
            """,
            reader => reader.Read()
                ? reader.GetInt64(0)
                : throw new InvalidOperationException("API呼び出しログのIDを取得できませんでした。"),
            ("$called_at", ToStorageValue(_now())),
            ("$trigger_type", ToStorageValue(entry.Trigger)),
            ("$model", entry.Model.Trim()),
            ("$prompt_tokens", entry.PromptTokens),
            ("$output_tokens", entry.OutputTokens),
            ("$usd_cost", entry.UsdCost.ToString(CultureInfo.InvariantCulture)),
            ("$usd_jpy_rate", fxRate?.UsdJpy.ToString(CultureInfo.InvariantCulture)),
            ("$rate_date", fxRate?.RateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$jpy_cost", jpyCost?.ToString(CultureInfo.InvariantCulture)),
            ("$duration_ms", entry.DurationMilliseconds),
            ("$status", ToStorageValue(entry.Status)),
            ("$error_message", entry.ErrorMessage),
            ("$suggestion_cnt", entry.SuggestionCount),
            ("$discarded_cnt", entry.DiscardedCount));
    }

    /// <summary>本文またはタブが変わり、応答全体を表示できなかったログを破棄済みに更新する。</summary>
    internal bool MarkDiscarded(long id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        return _database.Execute(
            """
            UPDATE api_calls
            SET suggestion_cnt = 0,
                discarded_cnt = 1
            WHERE id = $id;
            """,
            ("$id", id)) == 1;
    }

    /// <summary>
    /// 指定期間の料金・使用量を集計する。from は含み、to は含まない。
    /// USD は SQLite の REAL に変換せず、保存した不変カルチャ文字列を decimal として加算する。
    /// <paramref name="triggers"/> が null または空なら <see cref="GetHistory"/> と同じ規約で全種別を対象にする。
    /// 課金履歴画面のヘッダ合計を、種別フィルタと一致させつつ <see cref="GetHistory"/> の limit 切り詰めから
    /// 独立させるために存在する。
    /// </summary>
    internal ApiCallUsageSummary GetUsageSummary(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        IReadOnlyCollection<ApiCallTrigger>? triggers = null)
    {
        if (from is not null && to is not null && from >= to)
            throw new ArgumentException("期間の開始は終了より前である必要があります。");

        HashSet<ApiCallTrigger>? triggerFilter = triggers is { Count: > 0 }
            ? new HashSet<ApiCallTrigger>(triggers)
            : null;

        return _database.Read(
            """
            SELECT called_at, status, prompt_tokens, output_tokens, usd_cost, jpy_cost,
                   usd_jpy_rate, rate_date, suggestion_cnt, discarded_cnt, trigger_type
            FROM api_calls;
            """,
            reader =>
        {
            long totalCalls = 0;
            long okCalls = 0;
            long errorCalls = 0;
            long timeoutCalls = 0;
            long promptTokens = 0;
            long outputTokens = 0;
            decimal usdCost = 0m;
            decimal jpyCost = 0m;
            bool isJpyComplete = true;
            var distinctRates = new HashSet<(decimal Rate, DateOnly Date)>();
            long suggestionCount = 0;
            long discardedCount = 0;

            while (reader.Read())
            {
                // ISO文字列の辞書順はDSTの重複時刻で実時間順にならないため、
                // DateTimeOffsetとして比較する。壊れた旧ログは集計対象から除外する。
                if (!DateTimeOffset.TryParse(
                        reader.GetString(0),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset calledAt) ||
                    (from is not null && calledAt < from.Value) ||
                    (to is not null && calledAt >= to.Value))
                {
                    continue;
                }

                if (!TryFromStorageTrigger(reader.GetString(10), out ApiCallTrigger rowTrigger) ||
                    (triggerFilter is not null && !triggerFilter.Contains(rowTrigger)))
                {
                    continue;
                }

                if (!decimal.TryParse(
                        reader.GetString(4),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out decimal rowUsdCost))
                {
                    continue;
                }

                bool hasJpyCost = TryReadDecimal(reader, 5, out decimal rowJpyCost);

                totalCalls++;
                switch (reader.GetString(1))
                {
                    case "ok": okCalls++; break;
                    case "error": errorCalls++; break;
                    case "timeout": timeoutCalls++; break;
                    default:
                        // 旧版や手動編集で壊れた状態値も表示を壊さないよう除外する。
                        totalCalls--;
                        continue;
                }

                promptTokens += reader.GetInt32(2);
                outputTokens += reader.GetInt32(3);
                usdCost += rowUsdCost;
                if (hasJpyCost)
                {
                    jpyCost += rowJpyCost;
                    if (TryReadDecimal(reader, 6, out decimal rowRate) &&
                        TryReadDateOnly(reader, 7, out DateOnly rowRateDate))
                    {
                        distinctRates.Add((rowRate, rowRateDate));
                    }
                }
                else if (rowUsdCost != 0m)
                    isJpyComplete = false;
                suggestionCount += reader.GetInt32(8);
                discardedCount += reader.GetInt32(9);
            }

            (decimal Rate, DateOnly Date)? singleRate = distinctRates.Count == 1
                ? distinctRates.Single()
                : null;
            DateOnly? firstRateDate = distinctRates.Count == 0
                ? null
                : distinctRates.MinBy(value => value.Date).Date;
            DateOnly? lastRateDate = distinctRates.Count == 0
                ? null
                : distinctRates.MaxBy(value => value.Date).Date;

            return new ApiCallUsageSummary(
                totalCalls, okCalls, errorCalls, timeoutCalls,
                promptTokens, outputTokens, usdCost,
                suggestionCount, discardedCount, jpyCost, isJpyComplete,
                singleRate?.Rate, singleRate?.Date, firstRateDate, lastRateDate,
                distinctRates.Count);
        });
    }

    /// <summary>
    /// 課金履歴画面向けの明細一覧を返す。from は含み、to は含まない（<see cref="GetUsageSummary"/> と同じ半開区間）。
    /// USD/JPY は SQLite の REAL に変換せず、保存した不変カルチャ文字列を decimal として読む。
    /// 新しい順（<see cref="ApiCallHistoryRow.CalledAt"/> 降順）で並べ、同時刻は id 降順で安定させる。
    /// <paramref name="limit"/> 件を超えた分は切り捨て、<see cref="ApiCallHistoryPage.TotalCount"/> と
    /// <see cref="ApiCallHistoryPage.Truncated"/> で呼び出し側に伝える。
    /// </summary>
    internal ApiCallHistoryPage GetHistory(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        IReadOnlyCollection<ApiCallTrigger>? triggers = null,
        int limit = 2000)
    {
        if (from is not null && to is not null && from >= to)
            throw new ArgumentException("期間の開始は終了より前である必要があります。");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        HashSet<ApiCallTrigger>? triggerFilter = triggers is { Count: > 0 }
            ? new HashSet<ApiCallTrigger>(triggers)
            : null;

        return _database.Read(
            """
            SELECT id, called_at, trigger_type, model, prompt_tokens, output_tokens,
                   usd_cost, usd_jpy_rate, rate_date, jpy_cost, duration_ms,
                   status, error_message, suggestion_cnt, discarded_cnt
            FROM api_calls;
            """,
            reader =>
        {
            List<ApiCallHistoryRow> matched = [];

            while (reader.Read())
            {
                // ISO文字列の辞書順はDSTの重複時刻で実時間順にならないため、DateTimeOffsetとして比較する。
                if (!DateTimeOffset.TryParse(
                        reader.GetString(1),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset calledAt) ||
                    (from is not null && calledAt < from.Value) ||
                    (to is not null && calledAt >= to.Value))
                {
                    continue;
                }

                if (!TryFromStorageTrigger(reader.GetString(2), out ApiCallTrigger trigger) ||
                    (triggerFilter is not null && !triggerFilter.Contains(trigger)))
                {
                    continue;
                }

                if (!decimal.TryParse(
                        reader.GetString(6),
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out decimal usdCost))
                {
                    continue;
                }

                if (!TryFromStorageStatus(reader.GetString(11), out ApiCallStatus status))
                    continue;

                matched.Add(new ApiCallHistoryRow(
                    reader.GetInt64(0),
                    calledAt,
                    trigger,
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    usdCost,
                    TryReadDecimal(reader, 7, out decimal rate) ? rate : null,
                    TryReadDateOnly(reader, 8, out DateOnly rateDate) ? rateDate : null,
                    TryReadDecimal(reader, 9, out decimal jpyCost) ? jpyCost : null,
                    reader.GetInt64(10),
                    status,
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.GetInt32(13),
                    reader.GetInt32(14)));
            }

            matched.Sort((left, right) =>
            {
                int byDate = right.CalledAt.CompareTo(left.CalledAt);
                return byDate != 0 ? byDate : right.Id.CompareTo(left.Id);
            });

            long totalCount = matched.Count;
            bool truncated = totalCount > limit;
            IReadOnlyList<ApiCallHistoryRow> page = truncated
                ? matched.Take(limit).ToArray()
                : matched;

            return new ApiCallHistoryPage(page, totalCount, truncated);
        });
    }

    /// <summary>最後に記録した有効な1件を、挿入順で返す。</summary>
    internal ApiCallLog? GetLatest()
        => _database.Read(
            """
            SELECT id, called_at, prompt_tokens, output_tokens, usd_cost,
                   usd_jpy_rate, rate_date, jpy_cost, status, suggestion_cnt, discarded_cnt
            FROM api_calls
            ORDER BY id DESC;
            """,
            reader =>
            {
                while (reader.Read())
                {
                    if (!DateTimeOffset.TryParse(
                            reader.GetString(1),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out DateTimeOffset calledAt) ||
                        !decimal.TryParse(
                            reader.GetString(4),
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                        out decimal usdCost) ||
                        !TryFromStorageStatus(reader.GetString(8), out ApiCallStatus status))
                    {
                        continue;
                    }

                    return new ApiCallLog(
                        reader.GetInt64(0), calledAt, reader.GetInt32(2),
                        reader.GetInt32(3), usdCost,
                        TryReadDecimal(reader, 5, out decimal rate) ? rate : null,
                        TryReadDateOnly(reader, 6, out DateOnly rateDate) ? rateDate : null,
                        TryReadDecimal(reader, 7, out decimal jpyCost) ? jpyCost : null,
                        status, reader.GetInt32(9), reader.GetInt32(10));
                }

                return null;
            });

    private static string ToStorageValue(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    private static bool TryFromStorageTrigger(string value, out ApiCallTrigger trigger)
    {
        trigger = value switch
        {
            "auto" => ApiCallTrigger.Auto,
            "manual" => ApiCallTrigger.Manual,
            "realternative" => ApiCallTrigger.Realternative,
            "styleguide" => ApiCallTrigger.StyleGuide,
            _ => default,
        };
        return value is "auto" or "manual" or "realternative" or "styleguide";
    }

    private static bool TryFromStorageStatus(string value, out ApiCallStatus status)
    {
        status = value switch
        {
            "ok" => ApiCallStatus.Ok,
            "error" => ApiCallStatus.Error,
            "timeout" => ApiCallStatus.Timeout,
            _ => default,
        };
        return value is "ok" or "error" or "timeout";
    }

    private static bool TryReadDecimal(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal,
        out decimal value)
    {
        value = 0m;
        if (reader.IsDBNull(ordinal))
            return false;

        return decimal.TryParse(
            Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadDateOnly(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal,
        out DateOnly value)
    {
        value = default;
        return !reader.IsDBNull(ordinal) &&
               DateOnly.TryParseExact(reader.GetString(ordinal), "yyyy-MM-dd",
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static string ToStorageValue(ApiCallTrigger trigger)
        => trigger switch
        {
            ApiCallTrigger.Auto => "auto",
            ApiCallTrigger.Manual => "manual",
            ApiCallTrigger.Realternative => "realternative",
            ApiCallTrigger.StyleGuide => "styleguide",
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    private static string ToStorageValue(ApiCallStatus status)
        => status switch
        {
            ApiCallStatus.Ok => "ok",
            ApiCallStatus.Error => "error",
            ApiCallStatus.Timeout => "timeout",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
