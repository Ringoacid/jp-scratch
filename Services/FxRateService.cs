using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace JpScratch.Services;

/// <summary>Frankfurter が返した USD/JPY の基準日と、取得した時刻を含む不変のレート。</summary>
internal sealed record FxRate(DateOnly RateDate, decimal UsdJpy, DateTimeOffset FetchedAt);

/// <summary>
/// Frankfurter v2 の ECB USD/JPY レートを日次で取得し、SQLite に保存する。
/// 通信・応答・キャッシュ書込みの失敗は料金表示の補助情報に限り、呼出元へ例外を出さない。
/// </summary>
internal sealed class FxRateService : IDisposable
{
    internal static readonly Uri Endpoint = new(
        "https://api.frankfurter.dev/v2/rate/USD/JPY?providers=ECB");
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private const string LastAttemptDateKey = "fx_last_attempt_local_date";

    private readonly Database _database;
    private readonly HttpClient _httpClient;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);
    private DateOnly? _lastAttemptLocalDate;

    internal FxRateService(Database database)
        : this(database, CreateHttpClient(), ownsHttpClient: true)
    {
    }

    internal FxRateService(
        Database database,
        HttpClient httpClient,
        Func<DateTimeOffset>? now = null,
        TimeSpan? requestTimeout = null,
        bool ownsHttpClient = false)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _now = now ?? (() => DateTimeOffset.Now);
        _requestTimeout = requestTimeout ?? RequestTimeout;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <summary>通信せず、最後に有効に保存されたレートだけを返す。</summary>
    internal FxRate? GetCachedRate()
    {
        try
        {
            return _database.Read(
                "SELECT rate_date, usd_jpy, fetched_at FROM fx_rates;",
                reader =>
                {
                    FxRate? latest = null;
                    while (reader.Read())
                    {
                        if (TryReadRate(reader, out FxRate? candidate) && candidate is { } value &&
                            (latest is null ||
                             value.RateDate > latest.RateDate ||
                             (value.RateDate == latest.RateDate && value.FetchedAt > latest.FetchedAt)))
                        {
                            latest = value;
                        }
                    }
                    return latest;
                });
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// ローカル日付で今日未取得の場合だけネットワークへ出る。失敗時は古いキャッシュ、
    /// それも無ければ null を返す。
    /// </summary>
    internal async Task<FxRate?> EnsureTodayAsync(CancellationToken cancellationToken = default)
    {
        DateOnly today = LocalDate(_now());
        FxRate? cached = GetCachedRate();
        // 当日成功キャッシュは即時返す。失敗済みの同日呼出はgateの内側で判定し、
        // 進行中の取得なら同じ結果（または古いcache）を受け取れるようにする。
        if (WasFetchedToday(cached, today))
            return cached;

        try
        {
            await _fetchGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return cached;
        }

        try
        {
            cached = GetCachedRate();
            today = LocalDate(_now());
            if (WasFetchedToday(cached, today) || WasAttemptedToday(today))
                return cached;

            // 成否にかかわらず、HTTP開始前に当日の試行を永続化して日中の再試行を抑止する。
            MarkAttempted(today);
            FxRate? fetched = await FetchAsync(null, cancellationToken);
            if (fetched is null || !TrySave(fetched))
                return cached;

            return fetched;
        }
        catch (OperationCanceledException)
        {
            return cached;
        }
        catch (HttpRequestException)
        {
            return cached;
        }
        catch (JsonException)
        {
            return cached;
        }
        catch (InvalidDataException)
        {
            return cached;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    /// <summary>
    /// 指定した呼出日を対象に Frankfurter の日付指定 API を呼ぶ、日次取得とは別経路。
    /// <c>fx_last_attempt_local_date</c> は読み書きせず、404 や応答不正は null として扱う。
    /// 後追い取得のレートは通常の日次キャッシュへ保存せず、呼出行へだけ記録する。
    /// これにより、後追い取得が当日の通常取得を抑止することはない。
    /// </summary>
    internal async Task<FxRate?> FetchForDateAsync(
        DateOnly requestedDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _fetchGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            FxRate? fetched = await FetchAsync(requestedDate, cancellationToken);
            if (fetched is null || fetched.RateDate > requestedDate)
                return null;

            return fetched;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    public void Dispose()
    {
        _fetchGate.Dispose();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private async Task<FxRate?> FetchAsync(
        DateOnly? requestedDate,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestedDate is DateOnly date ? BuildEndpoint(date) : Endpoint);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseContentRead, linked.Token);
        if (!response.IsSuccessStatusCode)
            return null;

        string body = await response.Content.ReadAsStringAsync(linked.Token);
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetString(root, "base", out string? @base) ||
            !string.Equals(@base, "USD", StringComparison.Ordinal) ||
            !TryGetString(root, "quote", out string? quote) ||
            !string.Equals(quote, "JPY", StringComparison.Ordinal) ||
            !TryGetString(root, "date", out string? dateText) ||
            !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly rateDate) ||
            !root.TryGetProperty("rate", out JsonElement rateElement) ||
            !rateElement.TryGetDecimal(out decimal rate) || rate <= 0m)
        {
            throw new InvalidDataException("Frankfurter の USD/JPY レスポンスが不正です。");
        }

        if (requestedDate is DateOnly requested && rateDate > requested)
            return null;

        return new FxRate(rateDate, rate, _now());
    }

    private bool TrySave(FxRate rate)
    {
        try
        {
            _database.Execute(
                """
                INSERT INTO fx_rates (rate_date, usd_jpy, fetched_at)
                VALUES ($rate_date, $usd_jpy, $fetched_at)
                ON CONFLICT(rate_date) DO UPDATE SET
                    usd_jpy = excluded.usd_jpy,
                    fetched_at = excluded.fetched_at;
                """,
                ("$rate_date", rate.RateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$usd_jpy", rate.UsdJpy.ToString(CultureInfo.InvariantCulture)),
                ("$fetched_at", rate.FetchedAt.ToString("O", CultureInfo.InvariantCulture)));
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private bool WasAttemptedToday(DateOnly today)
    {
        if (_lastAttemptLocalDate == today)
            return true;

        try
        {
            string? value = _database.Read(
                "SELECT value FROM app_metadata WHERE key = $key;",
                reader => reader.Read() ? reader.GetString(0) : null,
                ("$key", LastAttemptDateKey));
            if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly attempted))
            {
                return false;
            }

            _lastAttemptLocalDate = attempted;
            return attempted == today;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private void MarkAttempted(DateOnly today)
    {
        // DB書込みに失敗しても同一プロセス内では必ず抑止する。
        _lastAttemptLocalDate = today;
        try
        {
            _database.Execute(
                """
                INSERT INTO app_metadata (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """,
                ("$key", LastAttemptDateKey),
                ("$value", today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            // 次回の同一プロセス呼出はメモリ値で抑止し、アプリ動作は続ける。
        }
    }

    private static bool TryReadRate(Microsoft.Data.Sqlite.SqliteDataReader reader, out FxRate? rate)
    {
        rate = null;
        if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
            !DateOnly.TryParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly rateDate) ||
            !decimal.TryParse(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture),
                NumberStyles.Number, CultureInfo.InvariantCulture, out decimal usdJpy) ||
            usdJpy <= 0m ||
            !DateTimeOffset.TryParse(reader.GetString(2), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset fetchedAt))
        {
            return false;
        }

        rate = new FxRate(rateDate, usdJpy, fetchedAt);
        return true;
    }

    private static DateOnly LocalDate(DateTimeOffset value)
    {
        DateTime local = value.LocalDateTime;
        return new DateOnly(local.Year, local.Month, local.Day);
    }

    private static bool WasFetchedToday(FxRate? rate, DateOnly today)
        => rate is not null && LocalDate(rate.FetchedAt) == today;

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        value = null;
        return element.TryGetProperty(property, out JsonElement child) &&
               child.ValueKind == JsonValueKind.String &&
               (value = child.GetString()) is not null;
    }

    private static HttpClient CreateHttpClient()
        => new() { Timeout = Timeout.InfiniteTimeSpan };

    private static Uri BuildEndpoint(DateOnly requestedDate)
        => new(
            $"https://api.frankfurter.dev/v2/rate/USD/JPY?date=" +
            $"{requestedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&providers=ECB");
}
