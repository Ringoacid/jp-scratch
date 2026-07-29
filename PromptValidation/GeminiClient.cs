using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JpScratch.PromptValidation;

internal sealed class GeminiClient : IDisposable
{
    internal const decimal InputUsdPerMillion = 0.30m;
    internal const decimal OutputUsdPerMillion = 2.50m;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    internal GeminiClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
    }

    internal async Task<ProbeResult> GenerateAsync(
        ValidationCase testCase,
        string variant,
        CancellationToken cancellationToken)
    {
        bool fullRewrite = variant.StartsWith("full-rewrite", StringComparison.Ordinal);
        string userText = fullRewrite
            ? PromptFactory.BuildRewriteRequest(testCase, variant)
            : PromptFactory.BuildUserPrompt(testCase);

        JsonObject generationConfig = new()
        {
            ["temperature"] = 1.0,
            ["responseMimeType"] = fullRewrite ? "text/plain" : "application/json"
        };
        if (!fullRewrite)
            generationConfig["responseSchema"] = PromptFactory.CreateResponseSchema();

        JsonObject request = new()
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["text"] = PromptFactory.BuildSystemInstruction(variant)
                })
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject
                {
                    ["text"] = userText
                })
            }),
            ["generationConfig"] = generationConfig
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        using HttpResponseMessage response = await SendWithRetryAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            string safeBody = body.Replace(_apiKey, "[REDACTED]", StringComparison.Ordinal);
            throw new InvalidOperationException(
                $"Gemini API が {(int)response.StatusCode} ({response.ReasonPhrase}) を返しました: {safeBody}");
        }

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string candidateText = ExtractCandidateText(root);
        CorrectionResponse correctionResponse = fullRewrite
            ? new CorrectionResponse([])
            : JsonSerializer.Deserialize<CorrectionResponse>(candidateText, JsonOptions)
                ?? throw new InvalidOperationException("校正レスポンスを読み取れませんでした。");

        Usage usage = root.TryGetProperty("usageMetadata", out JsonElement usageElement)
            ? JsonSerializer.Deserialize<Usage>(usageElement.GetRawText(), JsonOptions)
                ?? new Usage(0, 0, 0)
            : new Usage(0, 0, 0);

        decimal cost =
            usage.PromptTokens / 1_000_000m * InputUsdPerMillion +
            usage.CandidateTokens / 1_000_000m * OutputUsdPerMillion;
        return new ProbeResult(
            correctionResponse,
            fullRewrite ? candidateText : null,
            usage,
            stopwatch.Elapsed,
            cost);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        JsonObject request,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpRequestMessage message = new(
                HttpMethod.Post,
                $"v1beta/models/{Uri.EscapeDataString(_model)}:generateContent")
            {
                Content = JsonContent.Create(request)
            };

            HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
            if (attempt == 1 || !IsTransient(response.StatusCode))
                return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static string ExtractCandidateText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out JsonElement candidates) ||
            candidates.GetArrayLength() == 0)
        {
            string reason = root.TryGetProperty("promptFeedback", out JsonElement feedback)
                ? feedback.GetRawText()
                : "理由なし";
            throw new InvalidOperationException($"候補が返されませんでした: {reason}");
        }

        JsonElement parts = candidates[0].GetProperty("content").GetProperty("parts");
        string text = string.Concat(
            parts.EnumerateArray()
                .Where(part => part.TryGetProperty("text", out _))
                .Select(part => part.GetProperty("text").GetString()));

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidOperationException("候補にテキストが含まれていません。")
            : text;
    }
}
