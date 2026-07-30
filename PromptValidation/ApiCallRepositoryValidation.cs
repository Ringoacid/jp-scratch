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

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

    private static DateTimeOffset AtOffset(
        int year, int month, int day, int hour, int minute, int offsetHours)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));
}
