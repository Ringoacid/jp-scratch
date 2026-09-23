using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>為替の後追い取得・手動補完のDB境界を、実ネットワークなしで検証する。</summary>
internal static class FxRateCompletionValidation
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 10, 0, 0, TimeSpan.FromHours(9));

    internal static async Task<bool> RunSelfTestsAsync()
    {
        bool latest = await TestHistoricalRateDoesNotBecomeLatestAsync();
        bool ordering = TestCachedRateUsesRateDateOrdering();
        bool manual = await TestHistoricalFetchIgnoresDailySuppressionAsync();
        bool future = await TestFutureResponseIsRejectedAsync();
        bool missing = TestMissingOriginalCostIsReported();
        bool apply = TestApplyPreservesJpyCost();
        bool offsetDate = TestSavedOffsetDateIsUsedForCompletion();
        bool weekend = await TestWeekendRateUsesCalledDateAndReturnedRateDateAsync();
        bool passed = latest && ordering && manual && future && missing && apply && offsetDate && weekend;
        Console.WriteLine("為替レート（後追い・日次抑止分離・補完・元円額不変）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static async Task<bool> TestHistoricalRateDoesNotBecomeLatestAsync()
    {
        using var store = new TestStore();
        StoreRate(store.Database, new DateOnly(2026, 8, 20), 160m, Now.AddDays(-1));
        var handler = new StubHandler(count => count switch
        {
            1 => JsonResponse(ValidJson("2026-08-04", 157.41m)),
            2 => JsonResponse(ValidJson("2026-08-21", 161.2m)),
            _ => throw new InvalidOperationException("想定外のHTTP呼び出しです。"),
        });
        using var http = new HttpClient(handler);
        using var service = new FxRateService(store.Database, http, () => Now);

        FxRate? backfilled = await service.FetchForDateAsync(new DateOnly(2026, 8, 4));
        FxRate? afterBackfill = service.GetCachedRate();
        int cachedCount = store.Database.Read(
            "SELECT COUNT(*) FROM fx_rates;",
            reader => reader.Read()
                ? Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)
                : -1);
        FxRate? today = await service.EnsureTodayAsync();

        return cachedCount == 1 &&
               handler.Count == 2 &&
               handler.RequestUris.Count == 2 &&
               handler.RequestUris[0].Query.Contains("date=2026-08-04", StringComparison.Ordinal) &&
               !handler.RequestUris[1].Query.Contains("date=", StringComparison.Ordinal) &&
               backfilled is { RateDate: var backfillDate } &&
               backfillDate == new DateOnly(2026, 8, 4) &&
               afterBackfill is { RateDate: var cachedDate, UsdJpy: 160m } &&
               cachedDate == new DateOnly(2026, 8, 20) &&
               today is { RateDate: var todayDate, UsdJpy: 161.2m } &&
               todayDate == new DateOnly(2026, 8, 21);
    }

    private static async Task<bool> TestHistoricalFetchIgnoresDailySuppressionAsync()
    {
        using var store = new TestStore();
        store.Database.Execute(
            "INSERT INTO app_metadata (key, value) VALUES ('fx_last_attempt_local_date', $date);",
            ("$date", "2026-08-21"));
        var handler = new StubHandler(_ => JsonResponse(ValidJson("2026-08-04", 157.41m)));
        using var http = new HttpClient(handler);
        using var service = new FxRateService(store.Database, http, () => Now);

        FxRate? rate = await service.FetchForDateAsync(new DateOnly(2026, 8, 4));
        return handler.Count == 1 && rate is { UsdJpy: 157.41m };
    }

    private static bool TestCachedRateUsesRateDateOrdering()
    {
        using var store = new TestStore();
        // 古い基準日のほうが後から取得された状態でも、基準日が新しいレートを返す。
        StoreRate(store.Database, new DateOnly(2026, 8, 4), 157.41m, Now);
        StoreRate(store.Database, new DateOnly(2026, 8, 20), 160m, Now.AddDays(-1));
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not run"));
        using var http = new HttpClient(handler);
        using var service = new FxRateService(store.Database, http, () => Now);

        FxRate? cached = service.GetCachedRate();
        return handler.Count == 0 &&
               cached is { RateDate: var date, UsdJpy: 160m } &&
               date == new DateOnly(2026, 8, 20);
    }

    private static async Task<bool> TestFutureResponseIsRejectedAsync()
    {
        using var store = new TestStore();
        var handler = new StubHandler(_ => JsonResponse(ValidJson("2026-08-05", 158m)));
        using var http = new HttpClient(handler);
        using var service = new FxRateService(store.Database, http, () => Now);

        FxRate? rate = await service.FetchForDateAsync(new DateOnly(2026, 8, 4));
        int saved = store.Database.Read(
            "SELECT COUNT(*) FROM fx_rates;",
            reader => reader.Read()
                ? Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)
                : -1);
        return handler.Count == 1 && rate is null && saved == 0;
    }

    private static bool TestMissingOriginalCostIsReported()
    {
        using var store = new TestStore();
        var clock = new Clock();
        var repository = new ApiCallRepository(store.Database, () => clock.Value);
        AddUnconfirmed(repository, clock, At(2026, 8, 4, 9), "JPY", 1574.1m);
        AddUnconfirmed(repository, clock, At(2026, 8, 4, 10), null, null);

        IReadOnlyList<UnconfirmedFxDateSummary> summaries =
            repository.GetUnconfirmedFxDateSummaries();
        return summaries.Count == 1 &&
               summaries[0] == new UnconfirmedFxDateSummary(
                   new DateOnly(2026, 8, 4), 1, 1);
    }

    private static bool TestApplyPreservesJpyCost()
    {
        using var store = new TestStore();
        var clock = new Clock();
        var repository = new ApiCallRepository(store.Database, () => clock.Value);
        long id = AddUnconfirmed(repository, clock, At(2026, 8, 4, 9), "JPY", 1574.1m);
        string beforeJpy = ReadString(store.Database, "jpy_cost", id);

        FxRateCompletionResult result = repository.ApplyFxRates(
            new Dictionary<DateOnly, FxRate>
            {
                [new DateOnly(2026, 8, 4)] =
                    new FxRate(new DateOnly(2026, 8, 4), 157.41m, Now),
            });

        (string Jpy, string Usd, int Confirmed, string Rate, string RateDate) after =
            store.Database.Read(
                """
                SELECT jpy_cost, usd_cost, usd_cost_confirmed, usd_jpy_rate, rate_date
                FROM api_calls WHERE id = $id;
                """,
                // 補完が走らなかった場合 usd_jpy_rate / rate_date は NULL のまま残る。
                // GetString で落とすと FAIL ではなく異常終了になり、後続のテストが1件も走らない。
                reader => reader.Read()
                    ? (reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                       reader.IsDBNull(3)
                           ? ""
                           : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? "",
                       reader.IsDBNull(4) ? "" : reader.GetString(4))
                    : ("", "", -1, "", ""),
                ("$id", id));
        int cachedRateCount = store.Database.Read(
            "SELECT COUNT(*) FROM fx_rates;",
            reader => reader.Read()
                ? Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)
                : -1);

        return result == new FxRateCompletionResult(1, 0) &&
               beforeJpy == "1574.1" &&
               after.Jpy == beforeJpy &&
               after.Usd == "10" &&
               after.Confirmed == 1 &&
               after.Rate == "157.41" &&
               after.RateDate == "2026-08-04" &&
               cachedRateCount == 0;
    }

    private static bool TestSavedOffsetDateIsUsedForCompletion()
    {
        using var store = new TestStore();
        var clock = new Clock();
        var repository = new ApiCallRepository(store.Database, () => clock.Value);
        DateTimeOffset calledAt = new(2026, 8, 4, 23, 30, 0, TimeSpan.FromHours(-7));
        long id = AddUnconfirmed(repository, clock, calledAt, "JPY", 1600m);

        IReadOnlyList<UnconfirmedFxDateSummary> summaries =
            repository.GetUnconfirmedFxDateSummaries();
        FxRateCompletionResult result = repository.ApplyFxRates(
            new Dictionary<DateOnly, FxRate>
            {
                [new DateOnly(2026, 8, 4)] =
                    new FxRate(new DateOnly(2026, 8, 4), 160m, Now),
            });
        string rateDate = ReadString(store.Database, "rate_date", id);

        return summaries.Count == 1 &&
               summaries[0] == new UnconfirmedFxDateSummary(
                   new DateOnly(2026, 8, 4), 1, 0) &&
               result == new FxRateCompletionResult(1, 0) &&
               rateDate == "2026-08-04";
    }

    private static async Task<bool> TestWeekendRateUsesCalledDateAndReturnedRateDateAsync()
    {
        using var store = new TestStore();
        var clock = new Clock();
        var repository = new ApiCallRepository(store.Database, () => clock.Value);
        long id = AddUnconfirmed(
            repository,
            clock,
            At(2026, 8, 15, 9),
            "JPY",
            318.02m);
        var handler = new StubHandler(_ => JsonResponse(ValidJson("2026-08-14", 159.01m)));
        using var http = new HttpClient(handler);
        using var service = new FxRateService(store.Database, http, () => Now);

        FxRate? rate = await service.FetchForDateAsync(new DateOnly(2026, 8, 15));
        IReadOnlyList<UnconfirmedFxDateSummary> summaries =
            repository.GetUnconfirmedFxDateSummaries();
        FxRateCompletionResult result = rate is null || summaries.Count != 1
            ? new FxRateCompletionResult(0, 0)
            : repository.ApplyFxRates(
                new Dictionary<DateOnly, FxRate>
                {
                    [summaries[0].CalledDate] = rate,
                });
        (string Usd, string Rate, string RateDate) after = store.Database.Read(
            "SELECT usd_cost, usd_jpy_rate, rate_date FROM api_calls WHERE id = $id;",
            reader => reader.Read()
                ? (reader.IsDBNull(0) ? "" : reader.GetString(0),
                   reader.IsDBNull(1) ? "" : reader.GetString(1),
                   reader.IsDBNull(2) ? "" : reader.GetString(2))
                : ("", "", ""),
            ("$id", id));

        return handler.Count == 1 &&
               handler.RequestUris[0].Query.Contains("date=2026-08-15", StringComparison.Ordinal) &&
               rate is { RateDate: var returnedDate, UsdJpy: 159.01m } &&
               returnedDate == new DateOnly(2026, 8, 14) &&
               summaries.Count == 1 &&
               summaries[0] == new UnconfirmedFxDateSummary(
                   new DateOnly(2026, 8, 15), 1, 0) &&
               result == new FxRateCompletionResult(1, 0) &&
               after == ("2", "159.01", "2026-08-14");
    }

    /// <summary>
    /// <see cref="ApiCallRepository.Add"/> は <c>called_at</c> を自前の時計から取るため、
    /// 呼出日を指定するには時計そのものを動かす必要がある。
    /// </summary>
    private static long AddUnconfirmed(
        ApiCallRepository repository,
        Clock clock,
        DateTimeOffset calledAt,
        string? currency,
        decimal? originalCost)
    {
        clock.Value = calledAt;
        return repository.Add(new ApiCallLogEntry(
            ApiCallTrigger.Auto,
            "plamo-3.0-prime",
            10,
            5,
            0m,
            1,
            ApiCallStatus.Ok,
            null,
            0,
            0,
            OriginalCurrency: currency,
            OriginalCost: originalCost,
            IsUsdCostConfirmed: false));
    }

    /// <summary>テストから呼出日を動かすための可変時計。</summary>
    private sealed class Clock
    {
        internal DateTimeOffset Value { get; set; } = Now;
    }

    private static DateTimeOffset At(int year, int month, int day, int hour)
        => new(year, month, day, hour, 0, 0, TimeSpan.FromHours(9));

    private static void StoreRate(
        Database database,
        DateOnly date,
        decimal rate,
        DateTimeOffset fetchedAt)
        => database.Execute(
            "INSERT INTO fx_rates (rate_date, usd_jpy, fetched_at) VALUES ($date, $rate, $fetched);",
            ("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$rate", rate.ToString(CultureInfo.InvariantCulture)),
            ("$fetched", fetchedAt.ToString("O", CultureInfo.InvariantCulture)));

    private static string ReadString(Database database, string column, long id)
        => database.Read(
            $"SELECT {column} FROM api_calls WHERE id = $id;",
            // 補完が走らなかった場合この列は NULL のまま。GetString で例外を投げると
            // スイート全体が止まり、FAIL として報告されなくなる。
            reader => reader.Read() && !reader.IsDBNull(0) ? reader.GetString(0) : "",
            ("$id", id));

    private static Task<HttpResponseMessage> JsonResponse(string json)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private static string ValidJson(string date, decimal rate)
        => $$"""{"date":"{{date}}","base":"USD","quote":"JPY","rate":{{rate.ToString(CultureInfo.InvariantCulture)}}}""";

    private sealed class TestStore : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "JpScratchFxCompletionValidation", Guid.NewGuid().ToString("N"));
        internal Database Database { get; }

        internal TestStore()
        {
            Directory.CreateDirectory(_directory);
            Database = new Database(Path.Combine(_directory, "test.db"));
        }

        public void Dispose()
        {
            Database.Dispose();
            try { Directory.Delete(_directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class StubHandler(Func<int, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        internal int Count { get; private set; }
        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            RequestUris.Add(request.RequestUri!);
            return response(Count);
        }
    }
}
