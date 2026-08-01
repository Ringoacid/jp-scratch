using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>
/// OpenAI Responses API を使う校正クライアント。
/// Geminiクライアントと同じ全文校正・差分検査の契約を使うため、UIと課金ログは共通化できる。
/// </summary>
internal sealed class OpenAiProofreadingClient : IProofreadingClient
{
    internal const string DefaultModel = ProofreadingModelCatalog.OpenAiModel;
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly Uri DefaultBaseAddress =
        new("https://api.openai.com/");

    private readonly Func<string?> _apiKeyProvider;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _ownsHttpClient;

    public string Model => _model;

    internal OpenAiProofreadingClient(
        CredentialService credentials,
        Func<GeminiApiKeySource> sourceProvider,
        string model = DefaultModel)
        : this(
            () => credentials.GetOpenAiApiKey(sourceProvider()),
            CreateHttpClient(),
            model,
            ownsHttpClient: true)
    {
    }

    internal OpenAiProofreadingClient(
        Func<string?> apiKeyProvider,
        HttpClient httpClient,
        string model = DefaultModel,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? requestTimeout = null,
        bool ownsHttpClient = false)
    {
        _apiKeyProvider = apiKeyProvider;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= DefaultBaseAddress;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        _delay = delay ?? Task.Delay;
        _requestTimeout = requestTimeout ?? RequestTimeout;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<GeminiProofreadingResult> ProofreadAsync(
        ProofreadingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await GenerateAsync(
            request.SourceText,
            ProofreadingPrompt.BuildUserMessage(
                request.SourceText,
                request.BeforeContext,
                request.AfterContext),
            request.SystemInstructionOverride ?? ProofreadingPrompt.SystemInstruction,
            cancellationToken);
    }

    public async Task<GeminiAlternativeResult> GenerateAlternativeAsync(
        ProofreadingProposal proposal,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!proposal.IsActive)
            throw new ArgumentException("失効した提案の別案は生成できません。", nameof(proposal));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("別案生成には理由が必要です。", nameof(reason));

        GeminiProofreadingResult generated = await GenerateAsync(
            proposal.Original,
            ProofreadingPrompt.BuildAlternativeUserMessage(
                proposal.Original,
                proposal.Suggestion,
                reason.Trim(),
                proposal.LeftContext,
                proposal.RightContext),
            ProofreadingPrompt.AlternativeSystemInstruction,
            cancellationToken);

        string alternative = generated.CorrectedText.Trim();
        if (string.Equals(alternative, proposal.Original, StringComparison.Ordinal) ||
            string.Equals(alternative, proposal.Suggestion, StringComparison.Ordinal))
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "OpenAI APIから有効な別案が返されませんでした。",
                usage: generated.Usage,
                elapsed: generated.Elapsed);
        }

        return new GeminiAlternativeResult(
            alternative,
            generated.Usage,
            generated.Elapsed,
            generated.Attempts);
    }

    public async Task<GeminiStyleGuideResult> GenerateStyleGuideAsync(
        IReadOnlyList<FewShotExample> reactionHistory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reactionHistory);

        GeminiRawTextResult raw = await SendWithRetryAsync(
            ProofreadingPrompt.StyleGuideSystemInstruction,
            ProofreadingPrompt.BuildStyleGuideUserMessage(reactionHistory),
            ParseRawSuccess,
            cancellationToken);

        string content = raw.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "OpenAI APIから有効なスタイルガイドが返されませんでした。",
                usage: raw.Usage,
                elapsed: raw.Elapsed);
        }

        return new GeminiStyleGuideResult(content, raw.Usage, raw.Elapsed, raw.Attempts);
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private static HttpClient CreateHttpClient()
        => new()
        {
            BaseAddress = DefaultBaseAddress,
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

    private async Task<GeminiProofreadingResult> GenerateAsync(
        string sourceText,
        string userMessage,
        string systemInstruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (sourceText.Length == 0)
            throw new ArgumentException("空の文書は校正できません。", nameof(sourceText));

        return await SendWithRetryAsync(
            systemInstruction,
            userMessage,
            (body, elapsed, attempt) => ParseSuccess(body, sourceText, elapsed, attempt),
            cancellationToken);
    }

    /// <summary>
    /// APIキー確認・リクエスト構築・タイムアウト15秒・1回だけの再試行を、校正とスタイルガイド生成で共有する。
    /// </summary>
    private async Task<T> SendWithRetryAsync<T>(
        string systemInstruction,
        string userMessage,
        Func<string, TimeSpan, int, T> parseSuccess,
        CancellationToken cancellationToken)
    {
        string? apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiClientException(
                GeminiClientError.MissingApiKey,
                "OpenAI APIキーが設定されていません。設定画面で登録または取得元を選択してください。");
        }

        string requestJson = BuildRequestJson(systemInstruction, userMessage);
        Stopwatch stopwatch = new();

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 所要時間は「今回の送信」だけを計る。先頭で一度だけ開始すると、再試行の
            // バックオフ（1秒）と初回の失敗分が含まれ、ログの所要時間が実送信より膨らむ。
            stopwatch.Restart();

            try
            {
                using HttpResponseMessage response =
                    await SendOnceAsync(requestJson, apiKey.Trim(), cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 1 && IsTransient(response.StatusCode))
                    {
                        await BackoffAsync(response, cancellationToken);
                        continue;
                    }

                    throw new GeminiClientException(
                        GeminiClientError.RequestFailed,
                        $"OpenAI APIがHTTP {(int)response.StatusCode}を返しました。",
                        response.StatusCode);
                }

                T result = parseSuccess(body, stopwatch.Elapsed, attempt);
                stopwatch.Stop();
                return result;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 1)
                {
                    await BackoffAsync(null, cancellationToken);
                    continue;
                }

                throw new GeminiClientException(
                    GeminiClientError.Timeout,
                    "OpenAI APIへの接続が15秒以内に完了しませんでした。",
                    innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 1)
                {
                    await _delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                throw new GeminiClientException(
                    GeminiClientError.RequestFailed,
                    "OpenAI APIへ接続できませんでした。",
                    innerException: ex);
            }
        }

        throw new UnreachableException();
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        string requestJson,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(_requestTimeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using HttpRequestMessage request = new(HttpMethod.Post, "v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            linked.Token);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           (int)statusCode >= 500;

    private async Task BackoffAsync(
        HttpResponseMessage? response,
        CancellationToken cancellationToken)
    {
        // 429 で Retry-After が指定されていればそれに従う（Delta と HTTP-date の両形式に対応）。
        // 無ければ固定1秒。長すぎる待ちで無駄に占有しないよう、下限1秒・上限5秒でクランプする
        // （1試行あたりのタイムアウト15秒に対して、10秒待つと1リクエストの最悪時間が40秒近くに
        // 伸びるため。5秒程度が妥当）。
        TimeSpan delay = TimeSpan.FromSeconds(1);
        if (response is { StatusCode: HttpStatusCode.TooManyRequests } &&
            response.Headers.RetryAfter is { } retryAfter)
        {
            TimeSpan? retryAfterDelay = retryAfter.Delta ??
                (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            if (retryAfterDelay is { } d)
                delay = TimeSpan.FromSeconds(Math.Clamp(d.TotalSeconds, 1, 5));
        }

        await _delay(delay, cancellationToken);
    }

    private static GeminiProofreadingResult ParseSuccess(
        string body,
        string sourceText,
        TimeSpan elapsed,
        int attempts)
    {
        (string correctedText, GeminiUsage usage) = ParseRaw(body);
        DocumentDiffResult diff = DocumentDiff.Create(sourceText, correctedText);
        return new GeminiProofreadingResult(correctedText, diff, usage, elapsed, attempts);
    }

    private static GeminiRawTextResult ParseRawSuccess(
        string body,
        TimeSpan elapsed,
        int attempts)
    {
        (string text, GeminiUsage usage) = ParseRaw(body);
        return new GeminiRawTextResult(text, usage, elapsed, attempts);
    }

    private static (string Text, GeminiUsage Usage) ParseRaw(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string text = ExtractOutputText(root);
            GeminiUsage usage = ExtractUsage(root);
            return (text, usage);
        }
        catch (GeminiClientException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "OpenAI APIのレスポンスを読み取れませんでした。",
                innerException: ex);
        }
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out JsonElement outputText) &&
            outputText.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(outputText.GetString()))
        {
            return outputText.GetString()!;
        }

        if (!root.TryGetProperty("output", out JsonElement output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "OpenAI APIから校正結果が返されませんでした。");
        }

        string text = string.Concat(
            output.EnumerateArray().Where(item =>
                    item.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "message" &&
                    item.TryGetProperty("content", out _))
                .SelectMany(item => item.GetProperty("content").EnumerateArray())
                .Where(content =>
                    content.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "output_text" &&
                    content.TryGetProperty("text", out _))
                .Select(content => content.GetProperty("text").GetString()));

        return string.IsNullOrWhiteSpace(text)
            ? throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "OpenAI APIの校正結果が空でした。")
            : text;
    }

    private static GeminiUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage))
            return new GeminiUsage(0, 0, 0, 0, 0);

        int promptTokens = ReadInt(usage, "input_tokens");
        int outputTokens = ReadInt(usage, "output_tokens");
        int thoughtsTokens = usage.TryGetProperty("output_tokens_details", out JsonElement details)
            ? ReadInt(details, "reasoning_tokens")
            : 0;
        int candidateTokens = Math.Max(0, outputTokens - thoughtsTokens);
        int cachedTokens = usage.TryGetProperty("input_tokens_details", out JsonElement inputDetails)
            ? ReadInt(inputDetails, "cached_tokens")
            : 0;
        int totalTokens = ReadInt(usage, "total_tokens");
        return new GeminiUsage(
            promptTokens,
            candidateTokens,
            thoughtsTokens,
            cachedTokens,
            totalTokens);
    }

    private static int ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) &&
           value.TryGetInt32(out int count)
            ? count
            : 0;

    private string BuildRequestJson(string systemInstruction, string userMessage)
    {
        JsonObject request = new()
        {
            ["model"] = _model,
            ["instructions"] = systemInstruction.ReplaceLineEndings("\n"),
            ["input"] = userMessage,
            ["reasoning"] = new JsonObject { ["effort"] = "low" },
            ["store"] = false,
        };
        return request.ToJsonString();
    }
}
