using System.Globalization;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class ApiCallRepositoryValidation
{
    private sealed record StoredApiCall(
        string CalledAt,
        string Trigger,
        string Model,
        int PromptTokens,
        int OutputTokens,
        string UsdCost,
        bool JpyColumnsAreNull,
        long DurationMilliseconds,
        string Status,
        string? ErrorMessage,
        int SuggestionCount,
        int DiscardedCount);

    internal static bool RunSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchApiCallValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            using var database = new Database(databaseFile);
            DateTimeOffset timestamp = At(2026, 7, 1, 0, 0);
            var repository = new ApiCallRepository(database, () => timestamp);

            long firstId = AddAt(At(2026, 7, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 12, 7,
                0.000021m, 15, ApiCallStatus.Ok, null, 3, 0));
            long errorId = AddAt(At(2026, 6, 30, 23, 59), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 5, 2,
                0.0000017m, 30, ApiCallStatus.Error,
                "Gemini APIがHTTP 400を返しました。", 0, 0));
            long staleId = AddAt(At(2026, 7, 30, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 9, 4,
                0.0000127m, 20, ApiCallStatus.Ok, null, 2, 0));
            long timeoutId = AddAt(At(2026, 7, 30, 8, 59), new ApiCallLogEntry(
                ApiCallTrigger.Realternative, "gemini-3.5-flash-lite", 1, 2,
                0.0000003m, 45, ApiCallStatus.Timeout,
                "Gemini APIへの接続が15秒以内に完了しませんでした。", 0, 1));
            long sessionErrorId = AddAt(At(2026, 7, 30, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 3, 6,
                0.0000015m, 25, ApiCallStatus.Error,
                "Gemini APIがHTTP 500を返しました。", 0, 0));
            long sessionOkId = AddAt(At(2026, 7, 30, 10, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 7, 8,
                0.0000025m, 18, ApiCallStatus.Ok, null, 1, 0));
            long tomorrowId = AddAt(At(2026, 7, 31, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 11, 12,
                0.0000033m, 40, ApiCallStatus.Timeout,
                "Gemini APIへの接続が15秒以内に完了しませんでした。", 0, 0));
            long fallbackEarlyId = AddAt(AtOffset(2026, 11, 1, 1, 30, -4), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 13, 14,
                0.0000040m, 32, ApiCallStatus.Error,
                "Gemini APIがHTTP 429を返しました。", 0, 0));
            long fallbackLateId = AddAt(AtOffset(2026, 11, 1, 1, 15, -5), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 17, 18,
                0.0000050m, 27, ApiCallStatus.Ok, null, 2, 0));
            // 実時刻が古くても、後から記録した有効ログは直近表示の対象になる。
            long lateInsertedOldId = AddAt(At(2026, 1, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 19, 20,
                0.0000060m, 22, ApiCallStatus.Timeout,
                "Gemini APIへの接続が15秒以内に完了しませんでした。", 3, 0));
            FxRate fxRate = new(new DateOnly(2026, 7, 29), 163.68m, At(2026, 8, 2, 8, 0));
            long fxId = AddAt(At(2026, 8, 2, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 1, 1,
                0.1m, 10, ApiCallStatus.Ok, null, 0, 0, fxRate));
            FxRate secondFxRate = new(new DateOnly(2026, 7, 30), 164.5m, At(2026, 8, 2, 8, 5));
            long secondFxId = AddAt(At(2026, 8, 2, 10, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 2, 2,
                0.2m, 10, ApiCallStatus.Ok, null, 0, 0, secondFxRate));
            // 壊れた日時を最後に置いても、直近取得は直前の有効ログへフォールバックする。
            database.Execute(
                """
                INSERT INTO api_calls (
                    called_at, trigger_type, model, prompt_tokens, output_tokens,
                    usd_cost, duration_ms, status, suggestion_cnt, discarded_cnt)
                VALUES ('not-an-iso-date', 'auto', 'invalid', 99, 99, '9.99', 1, 'ok', 0, 0);
                """);
            bool markedDiscarded = repository.MarkDiscarded(staleId);

            long AddAt(DateTimeOffset at, ApiCallLogEntry entry)
            {
                timestamp = at;
                return repository.Add(entry);
            }

            IReadOnlyList<StoredApiCall> rows = database.Read(
                """
                SELECT called_at, trigger_type, model, prompt_tokens, output_tokens,
                       usd_cost, usd_jpy_rate, rate_date, jpy_cost, duration_ms,
                       status, error_message, suggestion_cnt, discarded_cnt
                FROM api_calls ORDER BY id;
                """,
                reader =>
                {
                    List<StoredApiCall> values = [];
                    while (reader.Read())
                    {
                        values.Add(new StoredApiCall(
                            reader.GetString(0), reader.GetString(1), reader.GetString(2),
                            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5),
                            reader.IsDBNull(6) && reader.IsDBNull(7) && reader.IsDBNull(8),
                            reader.GetInt64(9), reader.GetString(10),
                            reader.IsDBNull(11) ? null : reader.GetString(11),
                            reader.GetInt32(12), reader.GetInt32(13)));
                    }
                    return (IReadOnlyList<StoredApiCall>)values;
                });

            ApiCallUsageSummary all = repository.GetUsageSummary();
            ApiCallUsageSummary session = repository.GetUsageSummary(
                At(2026, 7, 30, 9, 0), At(2026, 7, 30, 11, 0));
            ApiCallUsageSummary today = repository.GetUsageSummary(
                At(2026, 7, 30, 0, 0), At(2026, 7, 31, 0, 0));
            ApiCallUsageSummary month = repository.GetUsageSummary(
                At(2026, 7, 1, 0, 0), At(2026, 8, 1, 0, 0));
            ApiCallUsageSummary fallBackWindow = repository.GetUsageSummary(
                AtOffset(2026, 11, 1, 1, 45, -4),
                AtOffset(2026, 11, 1, 2, 0, -5));
            ApiCallUsageSummary empty = repository.GetUsageSummary(
                At(2026, 8, 3, 0, 0), At(2026, 8, 4, 0, 0));
            ApiCallUsageSummary fxOnly = repository.GetUsageSummary(
                At(2026, 8, 2, 8, 30), At(2026, 8, 2, 9, 30));
            ApiCallUsageSummary fxMultiple = repository.GetUsageSummary(
                At(2026, 8, 2, 8, 30), At(2026, 8, 2, 10, 30));
            database.Execute(
                "INSERT INTO fx_rates (rate_date, usd_jpy, fetched_at) VALUES ('2026-07-29', '163.68', $at);",
                ("$at", At(2026, 8, 2, 8, 0).ToString("O", CultureInfo.InvariantCulture)));
            database.Execute(
                "UPDATE fx_rates SET usd_jpy = '170' WHERE rate_date = '2026-07-29';");
            ApiCallLog? latest = repository.GetLatest();
            (string Rate, string Date, string Jpy)? fxSnapshot = database.Read<(string Rate, string Date, string Jpy)?>(
                "SELECT usd_jpy_rate, rate_date, jpy_cost FROM api_calls WHERE id = $id;",
                reader => reader.Read()
                    ? (Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                       reader.GetString(1), reader.GetString(2))
                    : null,
                ("$id", fxId));

            bool calledAtIsLocalIso8601 = DateTimeOffset.TryParse(
                rows[0].CalledAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out _);
            bool passed =
                firstId > 0 && errorId > firstId && staleId > errorId &&
                timeoutId > staleId && sessionErrorId > timeoutId &&
                sessionOkId > sessionErrorId && tomorrowId > sessionOkId &&
                fallbackEarlyId > tomorrowId && fallbackLateId > fallbackEarlyId &&
                lateInsertedOldId > fallbackLateId && fxId > lateInsertedOldId && secondFxId > fxId &&
                markedDiscarded && rows.Count == 13 && calledAtIsLocalIso8601 &&
                rows[0] == new StoredApiCall(
                    rows[0].CalledAt, "manual", "gemini-3.5-flash-lite", 12, 7,
                    "0.000021", true, 15, "ok", null, 3, 0) &&
                rows[2] == new StoredApiCall(
                    rows[2].CalledAt, "auto", "gemini-3.5-flash-lite", 9, 4,
                    "0.0000127", true, 20, "ok", null, 0, 1) &&
                all == new ApiCallUsageSummary(12, 6, 3, 3, 100, 96, 0.3000580m, 9, 2, 49.268m, false,
                    null, null, new DateOnly(2026, 7, 29), new DateOnly(2026, 7, 30), 2) &&
                session == new ApiCallUsageSummary(2, 1, 1, 0, 10, 14, 0.0000040m, 1, 0, 0m, false,
                    null, null, null, null, 0) &&
                today == new ApiCallUsageSummary(4, 2, 1, 1, 20, 20, 0.0000170m, 1, 2, 0m, false,
                    null, null, null, null, 0) &&
                month == new ApiCallUsageSummary(6, 3, 1, 2, 43, 39, 0.0000413m, 4, 2, 0m, false,
                    null, null, null, null, 0) &&
                fallBackWindow == new ApiCallUsageSummary(1, 1, 0, 0, 17, 18, 0.0000050m, 2, 0, 0m, false,
                    null, null, null, null, 0) &&
                empty == ApiCallUsageSummary.Empty &&
                fxOnly == new ApiCallUsageSummary(1, 1, 0, 0, 1, 1, 0.1m, 0, 0, 16.368m, true,
                    163.68m, new DateOnly(2026, 7, 29), new DateOnly(2026, 7, 29), new DateOnly(2026, 7, 29), 1) &&
                fxMultiple == new ApiCallUsageSummary(2, 2, 0, 0, 3, 3, 0.3m, 0, 0, 49.268m, true,
                    null, null, new DateOnly(2026, 7, 29), new DateOnly(2026, 7, 30), 2) &&
                fxSnapshot == ("163.68", "2026-07-29", "16.368") &&
                latest is not null && latest.Id == secondFxId &&
                latest.CalledAt == At(2026, 8, 2, 10, 0) &&
                latest.PromptTokens == 2 && latest.OutputTokens == 2 &&
                latest.UsdCost == 0.2m && latest.JpyCost == 32.9m &&
                latest.UsdJpyRate == 164.5m && latest.RateDate == new DateOnly(2026, 7, 30) &&
                latest.Status == ApiCallStatus.Ok;
            Console.WriteLine(
                "API呼び出しDB（集計・日時境界・不変decimal・直近取得）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>課金履歴画面向け <see cref="ApiCallRepository.GetHistory"/> の自己テスト。</summary>
    internal static bool RunHistorySelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchApiCallHistoryValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            using var database = new Database(databaseFile);
            DateTimeOffset timestamp = At(2026, 7, 1, 0, 0);
            var repository = new ApiCallRepository(database, () => timestamp);

            long AddAt(DateTimeOffset at, ApiCallLogEntry entry)
            {
                timestamp = at;
                return repository.Add(entry);
            }

            // from境界ちょうど（7/1 00:00）に置く行。
            long r1 = AddAt(At(2026, 7, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 10, 5,
                0.00001m, 12, ApiCallStatus.Ok, null, 1, 0));
            FxRate fxR2 = new(new DateOnly(2026, 7, 15), 150m, At(2026, 7, 15, 12, 0));
            long r2 = AddAt(At(2026, 7, 15, 12, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 20, 10,
                0.00002m, 20, ApiCallStatus.Ok, null, 2, 0, fxR2));
            // r3はr2とcalled_atが完全に同時刻。idの新しいr3が先に並ぶことを確認する対象。
            long r3 = AddAt(At(2026, 7, 15, 12, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 8, 3,
                0.000009m, 30, ApiCallStatus.Error,
                "Gemini APIがHTTP 500を返しました。", 0, 0));
            long r4 = AddAt(At(2026, 7, 20, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Realternative, "gemini-3.5-flash-lite", 5, 5,
                0.000005m, 45, ApiCallStatus.Timeout,
                "Gemini APIへの接続が15秒以内に完了しませんでした。", 0, 1));
            FxRate fxR5 = new(new DateOnly(2026, 7, 25), 152.3m, At(2026, 7, 25, 9, 0));
            long r5 = AddAt(At(2026, 7, 25, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.StyleGuide, "gemini-3.5-flash-lite", 30, 15,
                0.00004m, 60, ApiCallStatus.Ok, null, 0, 0, fxR5));
            // to境界ちょうど（8/1 00:00）に置く行。7月の問い合わせでは除外される側。
            long r6 = AddAt(At(2026, 8, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 1, 1,
                0.000001m, 5, ApiCallStatus.Ok, null, 1, 0));

            // 壊れた行を直接INSERTする。日時パース不可・usd_costパース不可・未知trigger。
            // いずれも7月の期間内に収まる日時を使い、除外されることを確認する。
            database.Execute(
                """
                INSERT INTO api_calls (
                    called_at, trigger_type, model, prompt_tokens, output_tokens,
                    usd_cost, duration_ms, status, suggestion_cnt, discarded_cnt)
                VALUES ('not-an-iso-date', 'manual', 'gemini-3.5-flash-lite', 1, 1, '0.001', 1, 'ok', 0, 0);
                """);
            database.Execute(
                """
                INSERT INTO api_calls (
                    called_at, trigger_type, model, prompt_tokens, output_tokens,
                    usd_cost, duration_ms, status, suggestion_cnt, discarded_cnt)
                VALUES ($called_at, 'manual', 'gemini-3.5-flash-lite', 1, 1, 'not-a-number', 1, 'ok', 0, 0);
                """,
                ("$called_at", At(2026, 7, 16, 0, 0).ToString("O", CultureInfo.InvariantCulture)));
            database.Execute(
                """
                INSERT INTO api_calls (
                    called_at, trigger_type, model, prompt_tokens, output_tokens,
                    usd_cost, duration_ms, status, suggestion_cnt, discarded_cnt)
                VALUES ($called_at, 'unknown', 'gemini-3.5-flash-lite', 1, 1, '0.001', 1, 'ok', 0, 0);
                """,
                ("$called_at", At(2026, 7, 17, 0, 0).ToString("O", CultureInfo.InvariantCulture)));

            ApiCallHistoryPage julyAll = repository.GetHistory(
                At(2026, 7, 1, 0, 0), At(2026, 8, 1, 0, 0));
            ApiCallHistoryPage autoOnly = repository.GetHistory(
                At(2026, 7, 1, 0, 0), At(2026, 8, 1, 0, 0),
                [ApiCallTrigger.Auto]);
            ApiCallHistoryPage multiTrigger = repository.GetHistory(
                At(2026, 7, 1, 0, 0), At(2026, 8, 1, 0, 0),
                [ApiCallTrigger.Realternative, ApiCallTrigger.StyleGuide]);
            ApiCallHistoryPage emptyTriggerSet = repository.GetHistory(
                At(2026, 7, 1, 0, 0), At(2026, 8, 1, 0, 0), []);
            ApiCallHistoryPage truncated = repository.GetHistory(
                At(2026, 7, 1, 0, 0), At(2026, 8, 1, 0, 0), null, 2);
            ApiCallHistoryPage augustBoundary = repository.GetHistory(
                At(2026, 8, 1, 0, 0), At(2026, 8, 2, 0, 0));

            bool limitThrows = Throws<ArgumentOutOfRangeException>(
                () => repository.GetHistory(limit: 0));
            bool rangeThrows = Throws<ArgumentException>(
                () => repository.GetHistory(At(2026, 8, 1, 0, 0), At(2026, 7, 1, 0, 0)));

            var expectedR1 = new ApiCallHistoryRow(
                r1, At(2026, 7, 1, 0, 0), ApiCallTrigger.Manual, "gemini-3.5-flash-lite",
                10, 5, 0.00001m, null, null, null, 12, ApiCallStatus.Ok, null, 1, 0);
            var expectedR2 = new ApiCallHistoryRow(
                r2, At(2026, 7, 15, 12, 0), ApiCallTrigger.Auto, "gemini-3.5-flash-lite",
                20, 10, 0.00002m, 150m, new DateOnly(2026, 7, 15), 0.003m, 20,
                ApiCallStatus.Ok, null, 2, 0);
            var expectedR3 = new ApiCallHistoryRow(
                r3, At(2026, 7, 15, 12, 0), ApiCallTrigger.Auto, "gemini-3.5-flash-lite",
                8, 3, 0.000009m, null, null, null, 30, ApiCallStatus.Error,
                "Gemini APIがHTTP 500を返しました。", 0, 0);
            var expectedR4 = new ApiCallHistoryRow(
                r4, At(2026, 7, 20, 9, 0), ApiCallTrigger.Realternative, "gemini-3.5-flash-lite",
                5, 5, 0.000005m, null, null, null, 45, ApiCallStatus.Timeout,
                "Gemini APIへの接続が15秒以内に完了しませんでした。", 0, 1);
            var expectedR5 = new ApiCallHistoryRow(
                r5, At(2026, 7, 25, 9, 0), ApiCallTrigger.StyleGuide, "gemini-3.5-flash-lite",
                30, 15, 0.00004m, 152.3m, new DateOnly(2026, 7, 25), 0.006092m, 60,
                ApiCallStatus.Ok, null, 0, 0);
            var expectedR6 = new ApiCallHistoryRow(
                r6, At(2026, 8, 1, 0, 0), ApiCallTrigger.Manual, "gemini-3.5-flash-lite",
                1, 1, 0.000001m, null, null, null, 5, ApiCallStatus.Ok, null, 1, 0);

            bool passed =
                julyAll.TotalCount == 5 && !julyAll.Truncated && julyAll.Rows.Count == 5 &&
                julyAll.Rows[0] == expectedR5 && julyAll.Rows[1] == expectedR4 &&
                julyAll.Rows[2] == expectedR3 && julyAll.Rows[3] == expectedR2 &&
                julyAll.Rows[4] == expectedR1 &&
                autoOnly.TotalCount == 2 && !autoOnly.Truncated && autoOnly.Rows.Count == 2 &&
                autoOnly.Rows[0] == expectedR3 && autoOnly.Rows[1] == expectedR2 &&
                multiTrigger.TotalCount == 2 && multiTrigger.Rows.Count == 2 &&
                multiTrigger.Rows[0] == expectedR5 && multiTrigger.Rows[1] == expectedR4 &&
                emptyTriggerSet.TotalCount == 5 && emptyTriggerSet.Rows.Count == 5 &&
                truncated.TotalCount == 5 && truncated.Truncated && truncated.Rows.Count == 2 &&
                truncated.Rows[0] == expectedR5 && truncated.Rows[1] == expectedR4 &&
                augustBoundary.TotalCount == 1 && augustBoundary.Rows.Count == 1 &&
                augustBoundary.Rows[0] == expectedR6 &&
                limitThrows && rangeThrows;

            Console.WriteLine(
                "API呼び出し履歴（期間・種別フィルタ、並び順、limit、壊れた行の除外）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>課金履歴画面のヘッダ合計向け、<see cref="ApiCallRepository.GetUsageSummary"/> の種別フィルタの自己テスト。</summary>
    internal static bool RunUsageSummaryTriggerFilterSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "JpScratchApiCallUsageSummaryTriggerValidation",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            using var database = new Database(databaseFile);
            DateTimeOffset timestamp = At(2026, 7, 1, 0, 0);
            var repository = new ApiCallRepository(database, () => timestamp);

            long AddAt(DateTimeOffset at, ApiCallLogEntry entry)
            {
                timestamp = at;
                return repository.Add(entry);
            }

            AddAt(At(2026, 7, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 10, 5,
                0.00001m, 12, ApiCallStatus.Ok, null, 1, 0));
            FxRate fxR2 = new(new DateOnly(2026, 7, 15), 150m, At(2026, 7, 15, 12, 0));
            AddAt(At(2026, 7, 15, 12, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 20, 10,
                0.00002m, 20, ApiCallStatus.Ok, null, 2, 0, fxR2));
            AddAt(At(2026, 7, 15, 12, 0), new ApiCallLogEntry(
                ApiCallTrigger.Auto, "gemini-3.5-flash-lite", 8, 3,
                0.000009m, 30, ApiCallStatus.Error,
                "Gemini APIがHTTP 500を返しました。", 0, 0));
            AddAt(At(2026, 7, 20, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.Realternative, "gemini-3.5-flash-lite", 5, 5,
                0.000005m, 45, ApiCallStatus.Timeout,
                "Gemini APIへの接続が15秒以内に完了しませんでした。", 0, 1));
            FxRate fxR5 = new(new DateOnly(2026, 7, 25), 152.3m, At(2026, 7, 25, 9, 0));
            AddAt(At(2026, 7, 25, 9, 0), new ApiCallLogEntry(
                ApiCallTrigger.StyleGuide, "gemini-3.5-flash-lite", 30, 15,
                0.00004m, 60, ApiCallStatus.Ok, null, 0, 0, fxR5));
            AddAt(At(2026, 8, 1, 0, 0), new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gemini-3.5-flash-lite", 1, 1,
                0.000001m, 5, ApiCallStatus.Ok, null, 1, 0));

            // 未知trigger（不正データ）は、フィルタ有無に関わらずGetHistoryと同じく除外される。
            database.Execute(
                """
                INSERT INTO api_calls (
                    called_at, trigger_type, model, prompt_tokens, output_tokens,
                    usd_cost, duration_ms, status, suggestion_cnt, discarded_cnt)
                VALUES ($called_at, 'unknown', 'gemini-3.5-flash-lite', 1, 1, '0.001', 1, 'ok', 0, 0);
                """,
                ("$called_at", At(2026, 7, 17, 0, 0).ToString("O", CultureInfo.InvariantCulture)));

            DateTimeOffset julyFrom = At(2026, 7, 1, 0, 0);
            DateTimeOffset julyTo = At(2026, 8, 1, 0, 0);

            ApiCallUsageSummary all = repository.GetUsageSummary(julyFrom, julyTo);
            ApiCallUsageSummary nullTriggers = repository.GetUsageSummary(julyFrom, julyTo, null);
            ApiCallUsageSummary emptyTriggers = repository.GetUsageSummary(julyFrom, julyTo, []);
            ApiCallUsageSummary autoOnly = repository.GetUsageSummary(
                julyFrom, julyTo, [ApiCallTrigger.Auto]);
            ApiCallUsageSummary multiTrigger = repository.GetUsageSummary(
                julyFrom, julyTo, [ApiCallTrigger.Realternative, ApiCallTrigger.StyleGuide]);
            ApiCallUsageSummary noMatch = repository.GetUsageSummary(
                At(2026, 8, 1, 0, 0), At(2026, 8, 2, 0, 0), [ApiCallTrigger.StyleGuide]);

            var expectedAll = new ApiCallUsageSummary(
                5, 3, 1, 1, 73, 38, 0.000084m, 3, 1, 0.009092m, false,
                null, null, new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 25), 2);
            var expectedAutoOnly = new ApiCallUsageSummary(
                2, 1, 1, 0, 28, 13, 0.000029m, 2, 0, 0.003m, false,
                150m, new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15), 1);
            var expectedMultiTrigger = new ApiCallUsageSummary(
                2, 1, 0, 1, 35, 20, 0.000045m, 0, 1, 0.006092m, false,
                152.3m, new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 25), 1);

            bool passed =
                all == expectedAll &&
                nullTriggers == expectedAll &&
                emptyTriggers == expectedAll &&
                autoOnly == expectedAutoOnly &&
                multiTrigger == expectedMultiTrigger &&
                noMatch == ApiCallUsageSummary.Empty;

            Console.WriteLine(
                "API利用集計（種別フィルタ、null/空=全種別、未知triggerの除外）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>料金未確認の記録・読み出し・集計が確定行と混ざらないことの自己テスト。</summary>
    internal static bool RunUnconfirmedCostSelfTests()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "JpScratchApiCallUnconfirmedValidation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databaseFile = Path.Combine(directory, "test.db");

        try
        {
            using var database = new Database(databaseFile);
            DateTimeOffset clock = At(2026, 8, 21, 12, 0);
            var repository = new ApiCallRepository(database, () => clock);
            using var httpClient = new HttpClient();
            using var fxRates = new FxRateService(database, httpClient, () => clock);
            var pricing = new PricingService(Path.Combine(directory, "pricing.json"));
            PricingQuote quote = pricing.Calculate("plamo-3.0-prime", 100, 20);
            FxRate? cachedRate = fxRates.GetCachedRate();
            decimal? convertedUsd = quote.ToUsd(cachedRate?.UsdJpy);
            bool emptyFxCacheReproduced = cachedRate is null && convertedUsd is null;
            FxRate knownRate = new(
                new DateOnly(2026, 8, 20), 150m, new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.FromHours(9)));
            decimal confirmedJpyUsd = quote.ToUsd(knownRate.UsdJpy) ??
                throw new InvalidOperationException("円建て料金をテスト用USDへ換算できませんでした。");

            long unconfirmedJpy = repository.Add(new ApiCallLogEntry(
                ApiCallTrigger.Manual, "plamo-3.0-prime", 100, 20,
                convertedUsd ?? 0m, 400, ApiCallStatus.Ok, null, 1, 0,
                FxRate: cachedRate, OriginalCurrency: quote.Currency, OriginalCost: quote.Cost,
                IsUsdCostConfirmed: false));
            long unconfirmedWithoutAmount = repository.Add(new ApiCallLogEntry(
                ApiCallTrigger.Auto, "plamo-3.0-prime", 50, 10,
                0m, 300, ApiCallStatus.Error, "料金算出に失敗しました。", 0, 0,
                FxRate: knownRate, OriginalCurrency: null, OriginalCost: null,
                IsUsdCostConfirmed: false));
            long confirmed = repository.Add(new ApiCallLogEntry(
                ApiCallTrigger.Manual, "gpt-5.6-luna", 10, 5,
                0.01m, 100, ApiCallStatus.Ok, null, 0, 0,
                FxRate: knownRate, OriginalCurrency: "USD", OriginalCost: 0.01m,
                IsUsdCostConfirmed: true));
            long confirmedJpy = repository.Add(new ApiCallLogEntry(
                ApiCallTrigger.Manual, "plamo-3.0-prime", 80, 15,
                confirmedJpyUsd, 120, ApiCallStatus.Ok, null, 0, 0,
                FxRate: knownRate, OriginalCurrency: "JPY", OriginalCost: quote.Cost,
                IsUsdCostConfirmed: true));

            (string? Currency, string? Cost, int Confirmed)? raw = database.Read<(string?, string?, int)?>(
                "SELECT original_currency, original_cost, usd_cost_confirmed FROM api_calls WHERE id = $id;",
                reader => reader.Read()
                    ? (reader.IsDBNull(0) ? null : reader.GetString(0),
                       reader.IsDBNull(1) ? null : reader.GetString(1),
                       reader.GetInt32(2))
                    : null,
                ("$id", unconfirmedJpy));
            decimal? missingJpyCost = database.Read<decimal?>(
                "SELECT jpy_cost FROM api_calls WHERE id = $id;",
                reader => reader.Read() && !reader.IsDBNull(0)
                    ? decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture)
                    : (decimal?)null,
                ("$id", unconfirmedWithoutAmount));
            decimal? confirmedJpyCost = database.Read<decimal?>(
                "SELECT jpy_cost FROM api_calls WHERE id = $id;",
                reader => reader.Read() && !reader.IsDBNull(0)
                    ? decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture)
                    : (decimal?)null,
                ("$id", confirmedJpy));

            ApiCallHistoryPage history = repository.GetHistory(limit: int.MaxValue);
            ApiCallHistoryRow? jpyRow = history.Rows.SingleOrDefault(row => row.Id == unconfirmedJpy);
            ApiCallHistoryRow? missingRow = history.Rows.SingleOrDefault(row => row.Id == unconfirmedWithoutAmount);
            ApiCallHistoryRow? confirmedRow = history.Rows.SingleOrDefault(row => row.Id == confirmed);
            ApiCallHistoryRow? confirmedJpyRow = history.Rows.SingleOrDefault(row => row.Id == confirmedJpy);
            ApiCallUsageSummary summary = repository.GetUsageSummary();

            bool passed =
                emptyFxCacheReproduced &&
                raw == (quote.Currency, quote.Cost.ToString(CultureInfo.InvariantCulture), 0) &&
                jpyRow is not null && !jpyRow.IsUsdCostConfirmed &&
                jpyRow.OriginalCurrency == quote.Currency && jpyRow.OriginalCost == quote.Cost &&
                missingRow is not null && !missingRow.IsUsdCostConfirmed &&
                missingRow.OriginalCurrency is null && missingRow.OriginalCost is null &&
                missingJpyCost is null &&
                confirmedRow is not null && confirmedRow.IsUsdCostConfirmed &&
                confirmedJpyRow is not null && confirmedJpyRow.IsUsdCostConfirmed &&
                confirmedJpyCost == quote.Cost &&
                summary.TotalCalls == 4 && summary.UsdCost == 0.01m + confirmedJpyUsd &&
                summary.JpyCost == 1.50m + quote.Cost && summary.IsJpyComplete &&
                summary.DistinctRateCount == 1 &&
                summary.UnconfirmedCostCalls == 2 &&
                summary.UnconfirmedJpyCost == quote.Cost &&
                summary.UnconfirmedJpyAmountCalls == 1;

            Console.WriteLine("料金未確認（元通貨額・金額なし・確定行の記録/読出し/集計）: " +
                (passed ? "PASS" : "FAIL"));
            return passed;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

    private static DateTimeOffset AtOffset(
        int year, int month, int day, int hour, int minute, int offsetHours)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));
}
