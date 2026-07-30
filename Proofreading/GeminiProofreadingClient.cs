using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

internal enum GeminiClientError
{
    MissingApiKey,
    Timeout,
    RequestFailed,
    InvalidResponse,
}

internal sealed class GeminiClientException : Exception
{
    internal GeminiClientError Error { get; }
    internal HttpStatusCode? StatusCode { get; }
    /// <summary>HTTP成功後に判明した使用量。応答を安全に採用できない場合だけ任意で保持する。</summary>
    internal GeminiUsage? Usage { get; }
    internal TimeSpan? Elapsed { get; }

    internal GeminiClientException(
        GeminiClientError error,
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null,
        GeminiUsage? usage = null,
        TimeSpan? elapsed = null)
        : base(message, innerException)
    {
        Error = error;
        StatusCode = statusCode;
        Usage = usage;
        Elapsed = elapsed;
    }
}

internal sealed record GeminiUsage(
    int PromptTokens,
    int CandidateTokens,
    int ThoughtsTokens,
    int CachedContentTokens,
    int TotalTokens)
{
    internal int BillableOutputTokens => CandidateTokens + ThoughtsTokens;
}

internal sealed record GeminiProofreadingResult(
    string CorrectedText,
    DocumentDiffResult Diff,
    GeminiUsage Usage,
    TimeSpan Elapsed,
    int Attempts);

internal sealed record GeminiAlternativeResult(
    string Alternative,
    GeminiUsage Usage,
    TimeSpan Elapsed,
    int Attempts);

/// <summary>
/// 文脈込み全文を Gemini へ送り、修正版全文を安全な局所提案へ変換する。
/// APIキーはリクエストヘッダーにだけ設定し、例外へ含めない。
/// </summary>
internal sealed class GeminiProofreadingClient : IDisposable
{
    internal const string DefaultModel = "gemini-3.5-flash-lite";
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private static readonly Uri DefaultBaseAddress =
        new("https://generativelanguage.googleapis.com/");

    private readonly Func<string?> _apiKeyProvider;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _requestTimeout;
    private readonly bool _ownsHttpClient;

    internal GeminiProofreadingClient(
        CredentialService credentials,
        Func<GeminiApiKeySource> sourceProvider,
        string model = DefaultModel)
        : this(
            () => credentials.GetApiKey(sourceProvider()),
            CreateHttpClient(),
            model,
            ownsHttpClient: true)
    {
    }

    internal GeminiProofreadingClient(
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

    internal async Task<GeminiProofreadingResult> ProofreadAsync(
        string sourceText,
        CancellationToken cancellationToken = default)
        => await ProofreadAsync(
            sourceText,
            ProofreadingPrompt.BuildUserMessage(sourceText),
            cancellationToken);

    internal async Task<GeminiProofreadingResult> ProofreadAsync(
        ProofreadingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ProofreadAsync(
            request.SourceText,
            ProofreadingPrompt.BuildUserMessage(
                request.SourceText,
                request.BeforeContext,
                request.AfterContext),
            cancellationToken);
    }

    internal async Task<GeminiAlternativeResult> GenerateAlternativeAsync(
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
                "Gemini APIから有効な別案が返されませんでした。",
                usage: generated.Usage,
                elapsed: generated.Elapsed);
        }

        return new GeminiAlternativeResult(
            alternative,
            generated.Usage,
            generated.Elapsed,
            generated.Attempts);
    }

    private async Task<GeminiProofreadingResult> ProofreadAsync(
        string sourceText,
        string userMessage,
        CancellationToken cancellationToken)
        => await GenerateAsync(
            sourceText,
            userMessage,
            ProofreadingPrompt.SystemInstruction,
            cancellationToken);

    private async Task<GeminiProofreadingResult> GenerateAsync(
        string sourceText,
        string userMessage,
        string systemInstruction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (sourceText.Length == 0)
            throw new ArgumentException("空の文書は校正できません。", nameof(sourceText));

        string? apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new GeminiClientException(
                GeminiClientError.MissingApiKey,
                "Gemini APIキーが設定されていません。設定画面で登録または取得元を選択してください。");
        }

        apiKey = apiKey.Trim();
        string requestJson = BuildRequestJson(systemInstruction, userMessage);
        Stopwatch stopwatch = Stopwatch.StartNew();

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using HttpResponseMessage response =
                    await SendOnceAsync(requestJson, apiKey, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 1 && IsTransient(response.StatusCode))
                    {
                        await BackoffAsync(cancellationToken);
                        continue;
                    }

                    throw CreateRequestFailedException(response);
                }

                GeminiProofreadingResult result =
                    ParseSuccess(body, sourceText, stopwatch.Elapsed, attempt);
                stopwatch.Stop();
                return result with { Elapsed = stopwatch.Elapsed };
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == 1)
                {
                    await BackoffAsync(cancellationToken);
                    continue;
                }

                throw new GeminiClientException(
                    GeminiClientError.Timeout,
                    "Gemini APIへの接続が15秒以内に完了しませんでした。",
                    innerException: ex);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == 1)
                {
                    await BackoffAsync(cancellationToken);
                    continue;
                }

                throw new GeminiClientException(
                    GeminiClientError.RequestFailed,
                    "Gemini APIへ接続できませんでした。",
                    innerException: ex);
            }
        }

        throw new UnreachableException();
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

    private async Task<HttpResponseMessage> SendOnceAsync(
        string requestJson,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = new(_requestTimeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(_model)}:generateContent");

        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");

        return await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            linked.Token);
    }

    private async Task BackoffAsync(CancellationToken cancellationToken)
        => await _delay(TimeSpan.FromSeconds(1), cancellationToken);

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           (int)statusCode >= 500;

    private static GeminiClientException CreateRequestFailedException(
        HttpResponseMessage response)
    {
        return new GeminiClientException(
            GeminiClientError.RequestFailed,
            $"Gemini APIがHTTP {(int)response.StatusCode}を返しました。",
            response.StatusCode);
    }

    private static GeminiProofreadingResult ParseSuccess(
        string body,
        string sourceText,
        TimeSpan elapsed,
        int attempts)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string correctedText = ExtractCandidateText(root);
            GeminiUsage usage = ExtractUsage(root);
            DocumentDiffResult diff = DocumentDiff.Create(sourceText, correctedText);

            return new GeminiProofreadingResult(
                correctedText,
                diff,
                usage,
                elapsed,
                attempts);
        }
        catch (GeminiClientException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException
                                   or InvalidOperationException
                                   or KeyNotFoundException)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Gemini APIのレスポンスを読み取れませんでした。",
                innerException: ex);
        }
    }

    private static string ExtractCandidateText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out JsonElement candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Gemini APIから校正結果が返されませんでした。");
        }

        JsonElement first = candidates[0];
        if (!first.TryGetProperty("content", out JsonElement content) ||
            !content.TryGetProperty("parts", out JsonElement parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Gemini APIの校正結果に本文がありません。");
        }

        string text = string.Concat(
            parts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString()));

        return string.IsNullOrWhiteSpace(text)
            ? throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Gemini APIの校正結果が空でした。")
            : text;
    }

    private static GeminiUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out JsonElement usage))
            return new GeminiUsage(0, 0, 0, 0, 0);

        return new GeminiUsage(
            ReadInt(usage, "promptTokenCount"),
            ReadInt(usage, "candidatesTokenCount"),
            ReadInt(usage, "thoughtsTokenCount"),
            ReadInt(usage, "cachedContentTokenCount"),
            ReadInt(usage, "totalTokenCount"));
    }

    private static int ReadInt(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) &&
           value.TryGetInt32(out int count)
            ? count
            : 0;

    private static string BuildRequestJson(
        string systemInstruction,
        string userMessage)
    {
        // raw文字列リテラルの改行はソースの改行コードに依存する。
        // 検証済みプロンプトを環境にかかわらず同じバイト列で送るためLFへ統一する。
        string normalizedSystemInstruction =
            systemInstruction.ReplaceLineEndings("\n");
        JsonObject request = new()
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["text"] = normalizedSystemInstruction
                })
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["text"] = userMessage
                })
            }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 1.0,
                ["responseMimeType"] = "text/plain"
            }
        };

        return request.ToJsonString();
    }
}
