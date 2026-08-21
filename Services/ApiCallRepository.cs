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
    FxRate? FxRate = null,
    string? OriginalCurrency = null,
    decimal? OriginalCost = null,
    bool IsUsdCostConfirmed = true);

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
    int DistinctRateCount,
    long CompactedCalls = 0,
    long UnconfirmedCostCalls = 0,
    decimal UnconfirmedJpyCost = 0m,
    long UnconfirmedJpyAmountCalls = 0)
{
    internal static ApiCallUsageSummary Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0m, 0, 0, 0m, true,
        null, null, null, null, 0, 0);
}

/// <summary>
/// 保持期限を過ぎた明細を日次サマリへ圧縮した結果（要件 3.6.2）。
/// </summary>
internal sealed record ApiCallCompactionResult(
    int CompactedCalls,
    int CompactedDays,
    int UnlinkedReactions)
{
    internal static ApiCallCompactionResult None { get; } = new(0, 0, 0);

    internal bool DidCompact => CompactedCalls > 0;
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
    int DiscardedCount,
    string? OriginalCurrency = null,
    decimal? OriginalCost = null,
    bool IsUsdCostConfirmed = true);

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
    int DiscardedCount,
    string? OriginalCurrency = null,
    decimal? OriginalCost = null,
    bool IsUsdCostConfirmed = true);

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
        if (entry.OriginalCost is < 0m)
            throw new ArgumentOutOfRangeException(nameof(entry), "元通貨の料金は負数にできません。");
        if (entry.OriginalCost is not null && string.IsNullOrWhiteSpace(entry.OriginalCurrency))
            throw new ArgumentException("元通貨額には元通貨が必要です。", nameof(entry));
        if (entry.OriginalCurrency is not null && string.IsNullOrWhiteSpace(entry.OriginalCurrency))
            throw new ArgumentException("元通貨が空です。", nameof(entry));
        if (entry.FxRate is { UsdJpy: <= 0m })
            throw new ArgumentOutOfRangeException(nameof(entry), "為替レートは正数である必要があります。");

        FxRate? fxRate = entry.FxRate;
        decimal? jpyCost = !entry.IsUsdCostConfirmed
            ? string.Equals(entry.OriginalCurrency, "JPY", StringComparison.OrdinalIgnoreCase) &&
              entry.OriginalCost is decimal unconfirmedJpy
                ? unconfirmedJpy
                : null
            : string.Equals(entry.OriginalCurrency, "JPY", StringComparison.OrdinalIgnoreCase) &&
              entry.OriginalCost is decimal confirmedJpy
                ? confirmedJpy
                : fxRate is null ? null : entry.UsdCost * fxRate.UsdJpy;
        return _database.Read(
            """
            INSERT INTO api_calls (
                called_at, trigger_type, model, prompt_tokens, output_tokens,
                usd_cost, usd_jpy_rate, rate_date, jpy_cost, duration_ms,
                status, error_message, suggestion_cnt, discarded_cnt,
                original_currency, original_cost, usd_cost_confirmed)
            VALUES (
                $called_at, $trigger_type, $model, $prompt_tokens, $output_tokens,
                $usd_cost, $usd_jpy_rate, $rate_date, $jpy_cost, $duration_ms,
                $status, $error_message, $suggestion_cnt, $discarded_cnt,
                $original_currency, $original_cost, $usd_cost_confirmed)
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
            ("$discarded_cnt", entry.DiscardedCount),
            ("$original_currency", string.IsNullOrWhiteSpace(entry.OriginalCurrency)
                ? null : entry.OriginalCurrency.Trim().ToUpperInvariant()),
            ("$original_cost", entry.OriginalCost?.ToString(CultureInfo.InvariantCulture)),
            ("$usd_cost_confirmed", entry.IsUsdCostConfirmed ? 1 : 0));
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

        var accumulator = new UsageAccumulator();

        // 2回の読み取り（api_calls と api_call_daily）は1つのロック区間（InTransaction）にまとめる。
        // 別々の _database.Read だと、その間にバックグラウンドの明細圧縮（Compact）が挟まると、
        // 圧縮対象の行が「api_calls で読んだ直後に api_call_daily へ移動」して二重に集計される。
        // Database の lock は同一スレッドで再入できるため、InTransaction の中で db.Read を呼んでも
        // デッドロックしない。
        _database.InTransaction(db =>
        {
            db.Read(
                """
                SELECT called_at, status, prompt_tokens, output_tokens, usd_cost, jpy_cost,
                       usd_jpy_rate, rate_date, suggestion_cnt, discarded_cnt, trigger_type,
                       usd_cost_confirmed
                FROM api_calls
                WHERE ($from IS NULL OR called_at >= $from)
                  AND ($to   IS NULL OR called_at <  $to);
                """,
                reader =>
                {
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

                        accumulator.Add(
                            reader.GetString(1),
                            calls: 1,
                            promptTokens: reader.GetInt32(2),
                            outputTokens: reader.GetInt32(3),
                            usdCost: rowUsdCost,
                            jpyCost: TryReadDecimal(reader, 5, out decimal rowJpyCost) ? rowJpyCost : null,
                            rate: TryReadDecimal(reader, 6, out decimal rowRate) ? rowRate : null,
                            rateDate: TryReadDateOnly(reader, 7, out DateOnly rowRateDate) ? rowRateDate : null,
                            suggestionCount: reader.GetInt32(8),
                            discardedCount: reader.GetInt32(9),
                            usdCostConfirmed: reader.GetInt32(11) != 0,
                            compacted: false);
                    }

                    return 0;
                },
                ("$from", WidenedBound(from, -2)),
                ("$to", WidenedBound(to, +2)));

            // 保持期限を過ぎて圧縮済みの日次サマリを合算する。ここを読まないと、
            // 圧縮した瞬間に「全期間」や過去月の合計が黙って減る。
            db.Read(
                """
                SELECT day, status, prompt_tokens, output_tokens, usd_cost, jpy_cost,
                       usd_jpy_rate, rate_date, suggestion_cnt, discarded_cnt, trigger_type, call_cnt,
                       1 AS usd_cost_confirmed
                FROM api_call_daily
                WHERE ($from IS NULL OR day >= $from)
                  AND ($to   IS NULL OR day <= $to);
                """,
                reader =>
                {
                    while (reader.Read())
                    {
                        // サマリ1行はローカル1日ぶんなので、期間の判定はその日の0時で行う。
                        // 画面が渡す範囲（当日/当週/当月/全期間/カスタム）はすべて日境界に揃っているため、
                        // 明細行を1件ずつ判定した場合と同じ結果になる。
                        if (!DateOnly.TryParseExact(
                                reader.GetString(0), "yyyy-MM-dd",
                                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day))
                        {
                            continue;
                        }

                        var dayStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
                        if ((from is not null && dayStart < from.Value) ||
                            (to is not null && dayStart >= to.Value))
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

                        accumulator.Add(
                            reader.GetString(1),
                            calls: reader.GetInt64(11),
                            promptTokens: reader.GetInt64(2),
                            outputTokens: reader.GetInt64(3),
                            usdCost: rowUsdCost,
                            jpyCost: TryReadDecimal(reader, 5, out decimal rowJpyCost) ? rowJpyCost : null,
                            rate: TryReadDecimal(reader, 6, out decimal rowRate) ? rowRate : null,
                            rateDate: TryReadDateOnly(reader, 7, out DateOnly rowRateDate) ? rowRateDate : null,
                            suggestionCount: reader.GetInt64(8),
                            discardedCount: reader.GetInt64(9),
                            usdCostConfirmed: true,
                            compacted: true);
                    }

                    return 0;
                },
                // day はローカル日 yyyy-MM-dd。判定はその日の 0 時で行うので、
                // 前後 1 日ぶん広げておけば境界の行を落とさない。
                ("$from", from?.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$to", to?.AddDays(+1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        });

        return accumulator.Build();
    }

    /// <summary>
    /// <c>api_calls</c> の明細と <c>api_call_daily</c> の日次サマリを、同じ規約で1つの集計へ畳み込む。
    /// サマリ行は「同じ日・同じ種別・同じモデル・同じ成否・同じレートの <c>call_cnt</c> 件」なので、
    /// 件数を伴う点を除けば明細1行とまったく同じ扱いができる。
    /// </summary>
    private sealed class UsageAccumulator
    {
        private readonly HashSet<(decimal Rate, DateOnly Date)> _distinctRates = [];
        private long _totalCalls;
        private long _okCalls;
        private long _errorCalls;
        private long _timeoutCalls;
        private long _promptTokens;
        private long _outputTokens;
        private decimal _usdCost;
        private decimal _jpyCost;
        private bool _isJpyComplete = true;
        private long _suggestionCount;
        private long _discardedCount;
        private long _compactedCalls;
        private decimal _unconfirmedJpyCost;
        private long _unconfirmedJpyAmountCalls;

        internal void Add(
            string statusText,
            long calls,
            long promptTokens,
            long outputTokens,
            decimal usdCost,
            decimal? jpyCost,
            decimal? rate,
            DateOnly? rateDate,
            long suggestionCount,
            long discardedCount,
            bool usdCostConfirmed,
            bool compacted)
        {
            switch (statusText)
            {
                case "ok": _okCalls += calls; break;
                case "error": _errorCalls += calls; break;
                case "timeout": _timeoutCalls += calls; break;
                // 旧版や手動編集で壊れた状態値も表示を壊さないよう除外する。
                default: return;
            }

            _totalCalls += calls;
            _promptTokens += promptTokens;
            _outputTokens += outputTokens;
            _usdCost += usdCost;
            if (!usdCostConfirmed)
            {
                _unconfirmedCostCalls += calls;
                if (jpyCost is decimal unconfirmedJpy)
                {
                    _unconfirmedJpyCost += unconfirmedJpy;
                    _unconfirmedJpyAmountCalls += calls;
                }
            }
            else if (jpyCost is decimal jpy)
            {
                _jpyCost += jpy;
                if (rate is decimal rateValue && rateDate is DateOnly date)
                    _distinctRates.Add((rateValue, date));
            }
            else if (usdCost != 0m)
                _isJpyComplete = false;

            _suggestionCount += suggestionCount;
            _discardedCount += discardedCount;
            if (compacted) _compactedCalls += calls;
        }

        internal ApiCallUsageSummary Build()
        {
            (decimal Rate, DateOnly Date)? singleRate = _distinctRates.Count == 1
                ? _distinctRates.Single()
                : null;
            DateOnly? firstRateDate = _distinctRates.Count == 0
                ? null
                : _distinctRates.MinBy(value => value.Date).Date;
            DateOnly? lastRateDate = _distinctRates.Count == 0
                ? null
                : _distinctRates.MaxBy(value => value.Date).Date;

            return new ApiCallUsageSummary(
                _totalCalls, _okCalls, _errorCalls, _timeoutCalls,
                _promptTokens, _outputTokens, _usdCost,
                _suggestionCount, _discardedCount, _jpyCost, _isJpyComplete,
                singleRate?.Rate, singleRate?.Date, firstRateDate, lastRateDate,
                _distinctRates.Count, _compactedCalls, _unconfirmedCostCalls,
                _unconfirmedJpyCost, _unconfirmedJpyAmountCalls);
        }

        private long _unconfirmedCostCalls;
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
                   status, error_message, suggestion_cnt, discarded_cnt,
                   original_currency, original_cost, usd_cost_confirmed
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
                    reader.GetInt32(14),
                    reader.IsDBNull(15) ? null : reader.GetString(15),
                    TryReadDecimal(reader, 16, out decimal originalCost) ? originalCost : null,
                    reader.GetInt32(17) != 0));
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
                   usd_jpy_rate, rate_date, jpy_cost, status, suggestion_cnt, discarded_cnt,
                   original_currency, original_cost, usd_cost_confirmed
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
                        status, reader.GetInt32(9), reader.GetInt32(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        TryReadDecimal(reader, 12, out decimal originalCost) ? originalCost : null,
                        reader.GetInt32(13) != 0);
                }

                return null;
            });

    /// <summary>
    /// <paramref name="cutoff"/> より前の明細を日次サマリ（<c>api_call_daily</c>）へ畳み込み、
    /// 元の明細行を削除する（要件 3.6.2）。境界の決め方は <see cref="ApiLogRetention.ComputeCutoff"/>。
    ///
    /// 集計値は失わない。<see cref="GetUsageSummary"/> は両テーブルを合算するため、圧縮の前後で
    /// 期間合計・件数・トークン数・提案/破棄数はすべて一致する。失われるのは
    /// <see cref="GetHistory"/> が返す1件ごとの明細（時刻・所要時間・エラー文）だけ。
    ///
    /// <c>reactions.api_call_id</c> は <c>api_calls(id)</c> への外部キーで、<c>PRAGMA foreign_keys=ON</c>
    /// のため参照が残ったままでは削除できない。学習データそのもの（原文・修正案・拒否理由）は
    /// 消してはならないので、**リアクション行は残したまま <c>api_call_id</c> だけを NULL にする**。
    ///
    /// 解釈できない行（<c>called_at</c> / <c>usd_cost</c> / 種別 / 成否が壊れている）は対象にしない。
    /// 集計にも出てこない行を、圧縮のついでに黙って消さないため。
    /// </summary>
    internal ApiCallCompactionResult Compact(DateTimeOffset cutoff)
    {
        var totals = new Dictionary<DailyKey, DailyTotals>();
        List<long> idsToRemove = [];
        HashSet<DateOnly> affectedDays = [];
        int unlinkedReactions = 0;

        // 圧縮対象の抽出から明細の削除までを 1 つのトランザクション（＝1 つのロック区間）に収める。
        // 抽出だけをトランザクションの外で行うと、Compact が重なったとき（起動時・保持期間変更時・
        // 日付ロールオーバーの 3 経路がある）に、後発が先発の書き込み済みサマリへ同じ明細を
        // 再加算しうる。明細は既に消えているので、日次サマリの二重計上は復旧不能になる。
        // Database の lock は同一スレッドで再入できるため、InTransaction の中で db.Read を呼んでも
        // デッドロックしない（GetUsageSummary と同じ構造）。
        _database.InTransaction(db =>
        {
            db.Read(
                """
                SELECT id, called_at, trigger_type, model, status, prompt_tokens, output_tokens,
                       usd_cost, usd_jpy_rate, rate_date, jpy_cost, suggestion_cnt, discarded_cnt,
                       usd_cost_confirmed
                FROM api_calls
                WHERE called_at < $cutoff_upper;
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
                            calledAt >= cutoff)
                        {
                            continue;
                        }

                        // 未確認行は、元通貨額の有無にかかわらず**すべて**明細のまま保持する。
                        // 元通貨額を持つ行は後からレートで補完できるようにするため、元通貨額を持たない行
                        // （料金算出そのものが失敗した行）は未確認であった事実を失わないため。
                        // api_call_daily には未確認を表す列がないので、圧縮するとどちらも「$0 の確定行」として
                        // 合流し、未確認だったことが消える。それはこの機能がなくそうとしている状態そのもの。
                        if (reader.GetInt32(13) == 0)
                            continue;

                        if (!TryFromStorageTrigger(reader.GetString(2), out ApiCallTrigger trigger) ||
                            !TryFromStorageStatus(reader.GetString(4), out ApiCallStatus status) ||
                            !decimal.TryParse(
                                reader.GetString(7),
                                NumberStyles.Number,
                                CultureInfo.InvariantCulture,
                                out decimal usdCost))
                        {
                            continue;
                        }

                        DailyKey key = new(
                            DateOnly.FromDateTime(calledAt.LocalDateTime),
                            trigger,
                            reader.GetString(3),
                            status,
                            TryReadDecimal(reader, 8, out decimal rate) ? rate : null,
                            TryReadDateOnly(reader, 9, out DateOnly rateDate) ? rateDate : null);

                        Accumulate(
                            totals, key,
                            calls: 1,
                            promptTokens: reader.GetInt32(5),
                            outputTokens: reader.GetInt32(6),
                            usdCost: usdCost,
                            jpyCost: TryReadDecimal(reader, 10, out decimal jpyCost) ? jpyCost : null,
                            suggestionCount: reader.GetInt32(11),
                            discardedCount: reader.GetInt32(12));

                        idsToRemove.Add(reader.GetInt64(0));
                    }

                    return 0;
                },
                // 索引 idx_api_calls_at を効かせるための安全側の絞り込み。
                // 実際の cutoff 判定は上のループで DateTimeOffset として行う。
                ("$cutoff_upper", ToStorageValue(cutoff.AddDays(2))));

            if (idsToRemove.Count == 0) return;

            affectedDays.UnionWith(totals.Keys.Select(key => key.Day));

            // 既存のサマリを取り込んでから書き直す。同じ日を二度圧縮しても（保持期間を
            // 短くして再実行した場合など）合算が壊れず、何度実行しても結果が同じになる。
            // トランザクションの内側で読むことで、読み込みと書き戻しの間に割り込まれない。
            db.Read(
                """
                SELECT day, trigger_type, model, status, usd_jpy_rate, rate_date,
                       call_cnt, prompt_tokens, output_tokens, usd_cost, jpy_cost,
                       suggestion_cnt, discarded_cnt
                FROM api_call_daily;
                """,
                reader =>
            {
                while (reader.Read())
                {
                    if (!DateOnly.TryParseExact(
                            reader.GetString(0), "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ||
                        !affectedDays.Contains(day) ||
                        !TryFromStorageTrigger(reader.GetString(1), out ApiCallTrigger trigger) ||
                        !TryFromStorageStatus(reader.GetString(3), out ApiCallStatus status) ||
                        !decimal.TryParse(
                            reader.GetString(9),
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out decimal usdCost))
                    {
                        continue;
                    }

                    DailyKey key = new(
                        day, trigger, reader.GetString(2), status,
                        TryReadDecimal(reader, 4, out decimal rate) ? rate : null,
                        TryReadDateOnly(reader, 5, out DateOnly rateDate) ? rateDate : null);

                    Accumulate(
                        totals, key,
                        calls: reader.GetInt64(6),
                        promptTokens: reader.GetInt64(7),
                        outputTokens: reader.GetInt64(8),
                        usdCost: usdCost,
                        jpyCost: TryReadDecimal(reader, 10, out decimal jpyCost) ? jpyCost : null,
                        suggestionCount: reader.GetInt64(11),
                        discardedCount: reader.GetInt64(12));
                }

                return 0;
            });

            foreach (DateOnly day in affectedDays)
            {
                db.Execute(
                    "DELETE FROM api_call_daily WHERE day = $day;",
                    ("$day", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            }

            foreach ((DailyKey key, DailyTotals value) in totals)
            {
                db.Execute(
                    """
                    INSERT INTO api_call_daily (
                        day, trigger_type, model, status, usd_jpy_rate, rate_date,
                        call_cnt, prompt_tokens, output_tokens, usd_cost, jpy_cost,
                        suggestion_cnt, discarded_cnt)
                    VALUES (
                        $day, $trigger_type, $model, $status, $usd_jpy_rate, $rate_date,
                        $call_cnt, $prompt_tokens, $output_tokens, $usd_cost, $jpy_cost,
                        $suggestion_cnt, $discarded_cnt);
                    """,
                    ("$day", key.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("$trigger_type", ToStorageValue(key.Trigger)),
                    ("$model", key.Model),
                    ("$status", ToStorageValue(key.Status)),
                    ("$usd_jpy_rate", key.Rate is decimal rate ? (double)rate : null),
                    ("$rate_date", key.RateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("$call_cnt", value.Calls),
                    ("$prompt_tokens", value.PromptTokens),
                    ("$output_tokens", value.OutputTokens),
                    ("$usd_cost", value.UsdCost.ToString(CultureInfo.InvariantCulture)),
                    ("$jpy_cost", value.JpyMissing
                        ? null
                        : value.JpyCost.ToString(CultureInfo.InvariantCulture)),
                    ("$suggestion_cnt", value.SuggestionCount),
                    ("$discarded_cnt", value.DiscardedCount));
            }

            // SQLite のパラメータ上限（既定999）に収まるよう分割する。
            foreach (long[] chunk in idsToRemove.Chunk(400))
            {
                (string inClause, (string Name, object? Value)[] parameters) = BuildIdInClause(chunk);
                unlinkedReactions += db.Execute(
                    $"UPDATE reactions SET api_call_id = NULL WHERE api_call_id IN {inClause};",
                    parameters);
                db.Execute($"DELETE FROM api_calls WHERE id IN {inClause};", parameters);
            }
        });

        return idsToRemove.Count == 0
            ? ApiCallCompactionResult.None
            : new ApiCallCompactionResult(idsToRemove.Count, affectedDays.Count, unlinkedReactions);
    }

    private static void Accumulate(
        Dictionary<DailyKey, DailyTotals> totals,
        DailyKey key,
        long calls,
        long promptTokens,
        long outputTokens,
        decimal usdCost,
        decimal? jpyCost,
        long suggestionCount,
        long discardedCount)
    {
        if (!totals.TryGetValue(key, out DailyTotals? value))
        {
            value = new DailyTotals();
            totals[key] = value;
        }

        value.Calls += calls;
        value.PromptTokens += promptTokens;
        value.OutputTokens += outputTokens;
        value.UsdCost += usdCost;
        value.SuggestionCount += suggestionCount;
        value.DiscardedCount += discardedCount;

        // 粒度にレートを含めているので、レート有りのグループは全行が円を持ち、
        // レート無しのグループは全行が円を持たない。理屈の上で混在しうるのは手で壊した
        // 旧ログだけで、その場合は「円合計が欠けている」として NULL にする。
        // 誤った円合計を出すより、集計側で ¥— と表示させるほうが安全（既存の IsJpyComplete と同じ判断）。
        if (jpyCost is decimal jpy) value.JpyCost += jpy;
        else value.JpyMissing = true;
    }

    private static (string InClause, (string Name, object? Value)[] Parameters) BuildIdInClause(
        IReadOnlyList<long> ids)
    {
        var names = new string[ids.Count];
        var parameters = new (string Name, object? Value)[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            names[i] = "$id" + i.ToString(CultureInfo.InvariantCulture);
            parameters[i] = (names[i], ids[i]);
        }

        return ("(" + string.Join(",", names) + ")", parameters);
    }

    /// <summary>日次サマリの粒度。レートまで含める理由は DB v4 の移行コメントを参照。</summary>
    private readonly record struct DailyKey(
        DateOnly Day,
        ApiCallTrigger Trigger,
        string Model,
        ApiCallStatus Status,
        decimal? Rate,
        DateOnly? RateDate);

    private sealed class DailyTotals
    {
        internal long Calls;
        internal long PromptTokens;
        internal long OutputTokens;
        internal decimal UsdCost;
        internal decimal JpyCost;
        internal bool JpyMissing;
        internal long SuggestionCount;
        internal long DiscardedCount;
    }

    private static string ToStorageValue(DateTimeOffset value)
        => value.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>called_at</c>（ラウンドトリップ書式のローカル日時＋オフセット）に対する、
    /// 索引 <c>idx_api_calls_at</c> を効かせるための**安全側に広げた**文字列境界。
    ///
    /// 保存文字列は固定幅なので辞書順＝表記上の日時順だが、行ごとにオフセットが違いうるため、
    /// 実時刻の順序とは最大で ±14 時間ずれる。前後 2 日ぶん広げれば取りこぼしは起きない。
    /// 実際の期間判定は従来どおり <see cref="DateTimeOffset"/> として行うので、
    /// この境界は「明らかに範囲外の行を SQLite 側で落とすだけ」の絞り込みでしかない。
    /// 12 か月ぶん貯まった状態でのステータスバー更新・コールドスタートを守るために要る。
    /// </summary>
    private static string? WidenedBound(DateTimeOffset? value, int days)
        => value is { } bound ? ToStorageValue(bound.AddDays(days)) : null;

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
