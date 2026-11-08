using System.Globalization;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 保持期限後の明細圧縮（要件 3.6.2）の自己テスト。
///
/// 中心にあるのは**「圧縮しても期間合計が1円も変わらない」という不変条件**。
/// 圧縮は明細を消す破壊的な操作なので、合計が静かにずれたら気づけない。
/// 圧縮前後の <see cref="ApiCallUsageSummary"/> をレコード等価で丸ごと比較し、
/// 差が出てよいのは <see cref="ApiCallUsageSummary.CompactedCalls"/> だけであることを確認する。
/// </summary>
internal static class ApiLogCompactionValidation
{
    internal static bool RunSelfTests()
    {
        bool cutoffPassed = RunCutoffSelfTests();
        bool compactionPassed = RunCompactionSelfTests();

        bool passed = cutoffPassed && compactionPassed;
        Console.WriteLine(
            "課金明細の圧縮（保持期限の境界・合計不変・冪等・明細削除・リアクション保全）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunCutoffSelfTests()
    {
        DateTimeOffset now = At(2026, 7, 15, 12, 0);

        // 0以下は無期限。
        bool zeroIsUnlimited = ApiLogRetention.ComputeCutoff(now, 0) is null;
        bool negativeIsUnlimited = ApiLogRetention.ComputeCutoff(now, -1) is null;

        // 既定12か月。2026-07 の12か月前の月初 = 2025-07-01。
        bool twelveMonths =
            ApiLogRetention.ComputeCutoff(now, 12) == new DateTimeOffset(
                new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Local));

        // 月初へ丸めるので、月内のどの日に起動しても境界は動かない。
        bool sameWithinMonth =
            ApiLogRetention.ComputeCutoff(At(2026, 7, 1, 0, 0), 12) ==
            ApiLogRetention.ComputeCutoff(At(2026, 7, 31, 23, 59), 12);

        // 年をまたぐ引き算。2026-01 の 1か月前 = 2025-12-01。
        bool crossesYear =
            ApiLogRetention.ComputeCutoff(At(2026, 1, 20, 0, 0), 1) == new DateTimeOffset(
                new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Local));

        // 上限（SettingsService が 1200 か月へ丸める）でも例外を出さない。
        bool maxRetention = ApiLogRetention.ComputeCutoff(now, 1200) == new DateTimeOffset(
            new DateTime(1926, 7, 1, 0, 0, 0, DateTimeKind.Local));

        // 暦の開始を超える保持期間は「圧縮対象が存在しえない」= null。DateTime の範囲外例外を出さない。
        bool beyondCalendar = ApiLogRetention.ComputeCutoff(At(100, 1, 1, 0, 0), 1200) is null;

        bool passed = zeroIsUnlimited && negativeIsUnlimited && twelveMonths && sameWithinMonth &&
            crossesYear && maxRetention && beyondCalendar;
        Console.WriteLine("  保持期限の境界（0で無期限・月初へ丸め・年またぎ・暦外で例外なし）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunCompactionSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchApiLogCompactionValidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            DateTimeOffset clock = At(2026, 7, 15, 12, 0);
            var repository = new ApiCallRepository(database, () => clock);

            long AddAt(DateTimeOffset at, ApiCallLogEntry entry)
            {
                clock = at;
                return repository.Add(entry);
            }

            FxRate march = new(new DateOnly(2025, 3, 10), 150.1234m, At(2025, 3, 10, 0, 0));
            FxRate june = new(new DateOnly(2025, 6, 30), 160.5m, At(2025, 6, 30, 0, 0));

            // ---- 保持期限より古い明細（圧縮対象）----
            // 同じ日・種別・モデル・成否・レートの2件は、サマリ1行へ畳まれるはず。
            long oldA = AddAt(At(2025, 3, 10, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 100, 10,
                0.00005m, 900, ApiCallStatus.Ok, null, 2, 0, march));
            AddAt(At(2025, 3, 10, 10, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 200, 20,
                0.00011m, 800, ApiCallStatus.Ok, null, 3, 1, march));
            // 同じ日でも成否が違えば別のサマリ行になる。
            AddAt(At(2025, 3, 10, 11, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 5, 0,
                0.0000015m, 15000, ApiCallStatus.Timeout, "タイムアウト", 0, 0, march));
            // 為替未記録（jpy_cost が NULL）の行。IsJpyComplete=false が圧縮後も保たれること。
            AddAt(At(2024, 12, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 7, 3,
                0.00000123m, 500, ApiCallStatus.Ok, null, 1, 0));
            // 境界のすぐ手前。
            AddAt(At(2025, 6, 30, 23, 59), new ApiCallLogEntry(
                ApiCallTrigger.Realternative, "gemini-3.5-flash-lite", 50, 5,
                0.0000375m, 700, ApiCallStatus.Error, "HTTP 500", 0, 2, june));

            // ---- 保持期限以降の明細（そのまま残る）----
            // 境界ちょうど（cutoff は「これより前」が対象なので残る）。
            AddAt(At(2025, 7, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 11, 1,
                0.0000058m, 400, ApiCallStatus.Ok, null, 1, 0, june));
            AddAt(At(2026, 7, 10, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 13, 2,
                0.0000089m, 300, ApiCallStatus.Ok, null, 0, 0, june));
            // 保持期限を過ぎても、料金未確認行は後追い補完のため明細に残す。
            long unconfirmedOld = AddAt(At(2025, 2, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "plamo-3.0-prime", 8, 2,
                0m, 500, ApiCallStatus.Ok, null, 0, 0,
                FxRate: null, OriginalCurrency: "JPY", OriginalCost: 8m,
                IsUsdCostConfirmed: false));
            long unconfirmedWithoutAmountOld = AddAt(At(2025, 2, 2, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "plamo-3.0-prime", 4, 1,
                0m, 450, ApiCallStatus.Error, "料金算出に失敗しました。", 0, 0,
                FxRate: null, OriginalCurrency: null, OriginalCost: null,
                IsUsdCostConfirmed: false));

            // 圧縮対象の明細を参照するリアクション。学習データなので消してはならない。
            database.Execute(
                """
                INSERT INTO reactions (
                    reacted_at, api_call_id, tab_id, original, suggestion,
                    left_context, right_context, reaction, user_reason, used_in_prompt)
                VALUES ($at, $id, 'tab', 'コミニュケーション', 'コミュニケーション',
                        null, null, 'accept', null, 0);
                """,
                ("$at", "2025-03-10T09:00:00.0000000+09:00"),
                ("$id", oldA));

            // 解釈できない明細。集計に出てこない行を、圧縮のついでに黙って消さないこと。
            database.Execute(
                """
                INSERT INTO api_calls (
                    called_at, trigger_type, model, prompt_tokens, output_tokens,
                    usd_cost, usd_jpy_rate, rate_date, jpy_cost, duration_ms,
                    status, error_message, suggestion_cnt, discarded_cnt)
                VALUES ('壊れた日時', 'auto', 'gemini-3.5-flash-lite', 1, 1,
                        '0.000001', null, null, null, 1, 'ok', null, 0, 0);
                """);

            DateTimeOffset cutoff = ApiLogRetention.ComputeCutoff(At(2026, 7, 15, 12, 0), 12)!.Value;
            DateTimeOffset marchFrom = new(new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Local));
            DateTimeOffset marchTo = new(new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Local));

            ApiCallUsageSummary allBefore = repository.GetUsageSummary();
            ApiCallUsageSummary marchBefore = repository.GetUsageSummary(marchFrom, marchTo);
            ApiCallUsageSummary autoOnlyBefore = repository.GetUsageSummary(
                triggers: [ApiCallTrigger.Auto]);
            long detailCountBefore = repository.GetHistory(limit: int.MaxValue).TotalCount;

            ApiCallCompactionResult result = repository.Compact(cutoff);

            ApiCallUsageSummary allAfter = repository.GetUsageSummary();
            ApiCallUsageSummary marchAfter = repository.GetUsageSummary(marchFrom, marchTo);
            ApiCallUsageSummary autoOnlyAfter = repository.GetUsageSummary(
                triggers: [ApiCallTrigger.Auto]);
            ApiCallHistoryPage historyAfter = repository.GetHistory(limit: int.MaxValue);

            // 合計は1つも変わらない。差が出てよいのは CompactedCalls だけ。
            bool totalsUnchanged =
                (allAfter with { CompactedCalls = 0 }) == allBefore &&
                (marchAfter with { CompactedCalls = 0 }) == marchBefore &&
                (autoOnlyAfter with { CompactedCalls = 0 }) == autoOnlyBefore;

            bool compactedCountsReported =
                result.CompactedCalls == 5 &&
                // 圧縮された日は 2024-12-01 / 2025-03-10 / 2025-06-30 の3日。
                result.CompactedDays == 3 &&
                result.UnlinkedReactions == 1 &&
                allAfter.CompactedCalls == 5 &&
                marchAfter.CompactedCalls == 3;

            // 明細は消え、保持期限以降の2件だけが残る。
            bool detailsRemoved =
                detailCountBefore == 9 &&
                historyAfter.TotalCount == 4 &&
                historyAfter.Rows.Any(row => row.Id == unconfirmedOld && !row.IsUsdCostConfirmed) &&
                historyAfter.Rows.Any(row => row.Id == unconfirmedWithoutAmountOld && !row.IsUsdCostConfirmed) &&
                historyAfter.Rows
                    .Where(row => row.Id != unconfirmedOld && row.Id != unconfirmedWithoutAmountOld)
                    .All(row => row.CalledAt >= cutoff);

            bool unconfirmedKept = allAfter.UnconfirmedCostCalls == 2 &&
                CountUnconfirmedRows(database) == 2;

            // 圧縮されているか（同日・同条件の2件が1行に畳まれている）。
            long dailyRows = CountDailyRows(database);
            bool actuallyCompressed = dailyRows == 4;

            // 為替未記録の行があるので、圧縮後も円合計は不完全のまま。
            bool jpyIncompletenessKept = !allAfter.IsJpyComplete && !allBefore.IsJpyComplete;

            // レートの粒度が保たれ、2種類のレートが2種類のまま数えられている。
            bool ratesKept = allAfter.DistinctRateCount == allBefore.DistinctRateCount &&
                allBefore.DistinctRateCount == 2;

            // 学習データは残り、リンクだけが外れている。
            bool reactionKept =
                CountReactions(database) == 1 && CountReactionsWithApiCall(database) == 0;

            // 解釈できない行はそのまま残っている。
            bool brokenRowKept = CountBrokenRows(database) == 1;

            // 冪等。もう一度同じ境界で走らせても何も起きず、合計も動かない。
            ApiCallCompactionResult second = repository.Compact(cutoff);
            bool idempotent =
                !second.DidCompact &&
                repository.GetUsageSummary() == allAfter;

            // 既にサマリのある日へ、後から古い明細が入った場合の合流。
            AddAt(At(2025, 3, 10, 12, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 1, 1,
                0.0000011m, 100, ApiCallStatus.Ok, null, 1, 0, march));
            ApiCallUsageSummary mergedBefore = repository.GetUsageSummary();
            repository.Compact(cutoff);
            ApiCallUsageSummary mergedAfter = repository.GetUsageSummary();
            bool mergesIntoExistingDay =
                (mergedAfter with { CompactedCalls = 0 }) == (mergedBefore with { CompactedCalls = 0 }) &&
                mergedAfter.CompactedCalls == 6 &&
                CountDailyRows(database) == 4;

            bool passed = totalsUnchanged && compactedCountsReported && detailsRemoved &&
                unconfirmedKept &&
                actuallyCompressed && jpyIncompletenessKept && ratesKept && reactionKept &&
                brokenRowKept && idempotent && mergesIntoExistingDay;

            Console.WriteLine("  圧縮（未確認明細を保持・合計不変・件数報告・レート保持・冪等・既存日への合流）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static long CountDailyRows(Database database)
        => database.Read(
            "SELECT COUNT(*) FROM api_call_daily;",
            reader => reader.Read() ? reader.GetInt64(0) : -1);

    private static long CountReactions(Database database)
        => database.Read(
            "SELECT COUNT(*) FROM reactions;",
            reader => reader.Read() ? reader.GetInt64(0) : -1);

    private static long CountReactionsWithApiCall(Database database)
        => database.Read(
            "SELECT COUNT(*) FROM reactions WHERE api_call_id IS NOT NULL;",
            reader => reader.Read() ? reader.GetInt64(0) : -1);

    private static long CountBrokenRows(Database database)
        => database.Read(
            "SELECT COUNT(*) FROM api_calls WHERE called_at = '壊れた日時';",
            reader => reader.Read() ? reader.GetInt64(0) : -1);

    private static long CountUnconfirmedRows(Database database)
        => database.Read(
            "SELECT COUNT(*) FROM api_calls WHERE usd_cost_confirmed = 0;",
            reader => reader.Read() ? reader.GetInt64(0) : -1);

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));
}
