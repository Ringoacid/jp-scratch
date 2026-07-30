using System.Net;
using System.Net.Http;
using System.Text;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class FxRateServiceValidation
{
    private static readonly DateTimeOffset Today = new(2026, 7, 30, 10, 0, 0, TimeSpan.FromHours(9));

    internal static async Task<bool> RunSelfTestsAsync()
    {
        bool todayCache = await TestTodayCacheAsync();
        bool staleSuccess = await TestStaleCacheRefreshAsync();
        bool dayRollover = await TestDayRolloverAsync();
        bool weekendCache = await TestWeekendCacheAsync();
        bool fallback = await TestFailuresFallbackAsync();
        bool failedAttemptSuppression = await TestFailedAttemptSuppressionAsync();
        bool noCache = await TestNoCacheFailureAsync();
        bool concurrent = await TestConcurrentFetchAsync();
        bool passed = todayCache && staleSuccess && dayRollover && weekendCache && fallback &&
                      failedAttemptSuppression && noCache && concurrent;
        Console.WriteLine("為替レート（日次キャッシュ・fallback・応答検証・同時取得）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static async Task<bool> TestTodayCacheAsync()
    {
        using TestStore store = new();
        Store(store.Database, new DateOnly(2026, 7, 29), 163.68m, Today);
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not run"));
        using HttpClient http = new(handler);
        using var service = new FxRateService(store.Database, http, () => Today);
        FxRate? rate = await service.EnsureTodayAsync();
        return handler.Count == 0 && rate is { RateDate: var date, UsdJpy: 163.68m } &&
               date == new DateOnly(2026, 7, 29);
    }

    private static async Task<bool> TestStaleCacheRefreshAsync()
    {
        using TestStore store = new();
        Store(store.Database, new DateOnly(2026, 7, 28), 160m, Today.AddDays(-1));
        var handler = new StubHandler(_ => JsonResponse(ValidJson("2026-07-29", 163.68m)));
        using HttpClient http = new(handler);
        using var service = new FxRateService(store.Database, http, () => Today);
        FxRate? rate = await service.EnsureTodayAsync();
        FxRate? cached = service.GetCachedRate();
        return handler.Count == 1 && rate is { UsdJpy: 163.68m } &&
               cached is { RateDate: var date, UsdJpy: 163.68m } && date == new DateOnly(2026, 7, 29);
    }

    private static async Task<bool> TestWeekendCacheAsync()
    {
        using TestStore store = new();
        // レート基準日が金曜でも、土曜に取得済みなら追加HTTPしない。
        Store(store.Database, new DateOnly(2026, 7, 24), 163m, Today);
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP must not run"));
        using HttpClient http = new(handler);
        using var service = new FxRateService(store.Database, http, () => Today);
        FxRate? rate = await service.EnsureTodayAsync();
        return handler.Count == 0 && rate?.RateDate == new DateOnly(2026, 7, 24);
    }

    private static async Task<bool> TestDayRolloverAsync()
    {
        using TestStore store = new();
        DateTimeOffset now = Today;
        var handler = new StubHandler(count => JsonResponse(count == 1
            ? ValidJson("2026-07-29", 163.68m)
            : ValidJson("2026-07-30", 164.5m)));
        using HttpClient http = new(handler);
        using var service = new FxRateService(store.Database, http, () => now);
        FxRate? first = await service.EnsureTodayAsync();
        now = Today.AddDays(1);
        FxRate? second = await service.EnsureTodayAsync();
        return handler.Count == 2 && first is { UsdJpy: 163.68m } &&
               second is { RateDate: var date, UsdJpy: 164.5m } &&
               date == new DateOnly(2026, 7, 30);
    }

    private static async Task<bool> TestFailuresFallbackAsync()
    {
        IEnumerable<Func<int, Task<HttpResponseMessage>>> failures =
        [
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            _ => Task.FromException<HttpResponseMessage>(new OperationCanceledException()),
            _ => JsonResponse("{"),
            _ => JsonResponse("{}"),
            _ => JsonResponse(ValidJson("2026-07-29", 163m, @base: "EUR")),
            _ => JsonResponse(ValidJson("2026-07-29", 163m, quote: "USD")),
            _ => JsonResponse(ValidJson("not-a-date", 163m)),
            _ => JsonResponse(ValidJson("2026-07-29", 0m)),
            _ => JsonResponse(ValidJson("2026-07-29", -1m)),
        ];

        foreach (var failure in failures)
        {
            using TestStore store = new();
            Store(store.Database, new DateOnly(2026, 7, 28), 160m, Today.AddDays(-1));
            var handler = new StubHandler(failure);
            using HttpClient http = new(handler);
            using var service = new FxRateService(store.Database, http, () => Today,
                requestTimeout: TimeSpan.FromSeconds(1));
            FxRate? rate = await service.EnsureTodayAsync();
            if (handler.Count != 1 || rate is not { RateDate: var date, UsdJpy: 160m } ||
                date != new DateOnly(2026, 7, 28))
                return false;
        }

        return true;
    }

    private static async Task<bool> TestNoCacheFailureAsync()
    {
        using TestStore store = new();
        var handler = new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using HttpClient http = new(handler);
        using var service = new FxRateService(store.Database, http, () => Today);
        FxRate? first = await service.EnsureTodayAsync();
        FxRate? second = await service.EnsureTodayAsync();
        var restartedHandler = new StubHandler(_ => JsonResponse(ValidJson("2026-07-29", 163m)));
        using HttpClient restartedHttp = new(restartedHandler);
        using var restarted = new FxRateService(store.Database, restartedHttp, () => Today);
        FxRate? afterRestart = await restarted.EnsureTodayAsync();
        return first is null && second is null && afterRestart is null &&
               handler.Count == 1 && restartedHandler.Count == 0;
    }

    private static async Task<bool> TestFailedAttemptSuppressionAsync()
    {
        using TestStore store = new();
        DateTimeOffset now = Today;
        Store(store.Database, new DateOnly(2026, 7, 28), 160m, now.AddDays(-1));
        var failingHandler = new StubHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using HttpClient failingHttp = new(failingHandler);
        using var firstService = new FxRateService(store.Database, failingHttp, () => now);
        FxRate? first = await firstService.EnsureTodayAsync();
        FxRate? second = await firstService.EnsureTodayAsync();

        var restartedHandler = new StubHandler(_ => JsonResponse(ValidJson("2026-07-29", 163.68m)));
        using HttpClient restartedHttp = new(restartedHandler);
        using var restartedService = new FxRateService(store.Database, restartedHttp, () => now);
        FxRate? sameDayRestart = await restartedService.EnsureTodayAsync();
        now = Today.AddDays(1);
        FxRate? nextDay = await restartedService.EnsureTodayAsync();

        return first is { UsdJpy: 160m } && second is { UsdJpy: 160m } &&
               sameDayRestart is { UsdJpy: 160m } && nextDay is { UsdJpy: 163.68m } &&
               failingHandler.Count == 1 && restartedHandler.Count == 1;
    }

    private static async Task<bool> TestConcurrentFetchAsync()
    {
        using TestStore store = new();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async _ =>
        {
            await gate.Task;
            return await JsonResponse(ValidJson("2026-07-29", 163.68m));
        });
        using HttpClient http = new(handler);
        using var service = new FxRateService(store.Database, http, () => Today);
        Task<FxRate?> first = service.EnsureTodayAsync();
        Task<FxRate?> second = service.EnsureTodayAsync();
        gate.SetResult();
        FxRate?[] values = await Task.WhenAll(first, second);
        return handler.Count == 1 && values.All(rate => rate is { UsdJpy: 163.68m });
    }

    private static void Store(Database database, DateOnly date, decimal rate, DateTimeOffset fetchedAt)
        => database.Execute(
            "INSERT INTO fx_rates (rate_date, usd_jpy, fetched_at) VALUES ($d, $r, $f);",
            ("$d", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            ("$r", rate.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("$f", fetchedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));

    private static Task<HttpResponseMessage> JsonResponse(string json)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    private static string ValidJson(string date, decimal rate, string @base = "USD", string quote = "JPY")
        => $$"""{"date":"{{date}}","base":"{{@base}}","quote":"{{quote}}","rate":{{rate.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""";

    private sealed class TestStore : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "JpScratchFxValidation", Guid.NewGuid().ToString("N"));
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            return response(Count);
        }
    }
}
