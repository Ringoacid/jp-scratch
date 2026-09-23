using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>
/// Preferred Networks の PLaMo API を使う校正クライアント。
/// OpenAI 互換だが提供されるのは Chat Completions API であり、Responses API とは応答形が違うため
/// <see cref="OpenAiProofreadingClient"/> は流用できない。
/// </summary>
internal sealed class PlamoProofreadingClient : ProofreadingClientBase
{
    internal const string DefaultModel = "plamo-3.0-prime";

    /// <summary>1 リクエストの出力トークン上限。PLaMo 3.0 Prime の上限 20,000 の範囲内。</summary>
    private const int MaxOutputTokens = 16384;

    private static readonly Uri DefaultBaseAddress =
        new("https://api.platform.preferredai.jp/");

    private readonly Func<ProofreadingPurpose> _purposeProvider;

    protected override string ProviderName => "PLaMo";

    internal PlamoProofreadingClient(
        CredentialService credentials,
        Func<ApiKeySource> sourceProvider,
        Func<string> modelProvider,
        Func<TimeSpan> requestTimeoutProvider,
        Func<ProofreadingPurpose> purposeProvider)
        : base(
            () => credentials.GetApiKey(ApiProvider.PreferredNetworks, sourceProvider()),
            CreateHttpClient(),
            modelProvider,
            DefaultModel,
            DefaultBaseAddress,
            delay: null,
            requestTimeoutProvider,
            ownsHttpClient: true)
    {
        _purposeProvider = purposeProvider;
    }

    internal PlamoProofreadingClient(
        Func<string?> apiKeyProvider,
        HttpClient httpClient,
        string model = DefaultModel,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? requestTimeout = null,
        Func<ProofreadingPurpose>? purposeProvider = null,
        bool ownsHttpClient = false)
        : base(
            apiKeyProvider,
            httpClient,
            () => model,
            DefaultModel,
            DefaultBaseAddress,
            delay,
            requestTimeout is { } fixedTimeout ? () => fixedTimeout : null,
            ownsHttpClient)
    {
        _purposeProvider = purposeProvider ?? (() => ProofreadingPurpose.Automatic);
    }

    private static HttpClient CreateHttpClient()
        => new()
        {
            BaseAddress = DefaultBaseAddress,
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

    protected override HttpRequestMessage CreateHttpRequest(string requestJson, string apiKey)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// 生成が正常終了したことを確認する（基底クラスの説明を参照）。
    /// Chat Completions の finish_reason は完了時だけ "stop"。"length" は打ち切り。
    /// このプロバイダーには旧仕様が無いので、フィールドの欠落も異常として扱う（安全側）。
    /// </summary>
    protected override void EnsureCompleted(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "PLaMo APIから校正結果が返されませんでした。");
        }

        if (!choices[0].TryGetProperty("finish_reason", out JsonElement finishReason) ||
            finishReason.ValueKind != JsonValueKind.String)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "PLaMo APIの応答に停止理由がありません。");
        }

        if (!string.Equals(finishReason.GetString(), "stop", StringComparison.Ordinal))
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                $"PLaMo APIの生成が最後まで完了しませんでした（{finishReason.GetString()}）。");
        }
    }

    protected override string ExtractText(JsonElement root)
    {
        if (!root.GetProperty("choices")[0].TryGetProperty("message", out JsonElement message) ||
            !message.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.String)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "PLaMo APIの校正結果に本文がありません。");
        }

        string? text = content.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "PLaMo APIの校正結果が空でした。")
            : text;
    }

    /// <summary>
    /// PLaMo は推論トークンの内訳とキャッシュトークン数を返さない。
    /// 共通モデルでは該当項目を 0 とし、出力へ全量を寄せる。
    /// </summary>
    protected override GeminiUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage))
            return new GeminiUsage(0, 0, 0, 0, 0);

        return new GeminiUsage(
            ReadInt(usage, "prompt_tokens"),
            ReadInt(usage, "completion_tokens"),
            0,
            0,
            ReadInt(usage, "total_tokens"));
    }

    protected override string BuildRequestJson(string systemInstruction, string userMessage)
    {
        JsonObject request = new()
        {
            ["model"] = Model,
            ["max_tokens"] = MaxOutputTokens,
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = systemInstruction.ReplaceLineEndings("\n"),
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = userMessage,
                }),
        };

        // PLaMo の reasoning_effort は none / medium の 2 段階のみ（要件 3.5.1）。
        if (ProofreadingModelCatalog.Get(Model).EffortFor(_purposeProvider()) is { } effort)
            request["reasoning_effort"] = effort;

        return request.ToJsonString();
    }
}
