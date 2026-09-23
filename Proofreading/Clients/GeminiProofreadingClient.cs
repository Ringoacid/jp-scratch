using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>
/// 文脈込み全文を Gemini へ送り、修正版全文を安全な局所提案へ変換する。
/// APIキーはリクエストヘッダーにだけ設定し、例外へ含めない。
/// </summary>
internal sealed class GeminiProofreadingClient : ProofreadingClientBase
{
    internal const string DefaultModel = ProofreadingModelCatalog.GeminiModel;

    /// <summary>1 リクエストの出力トークン上限（思考トークンを含む）。<see cref="BuildRequestJson"/> 参照。</summary>
    private const int MaxOutputTokens = 16384;

    private static readonly Uri DefaultBaseAddress =
        new("https://generativelanguage.googleapis.com/");

    private readonly Func<ProofreadingPurpose> _purposeProvider;

    protected override string ProviderName => "Gemini";

    internal GeminiProofreadingClient(
        CredentialService credentials,
        Func<ApiKeySource> sourceProvider,
        Func<string> modelProvider,
        Func<TimeSpan> requestTimeoutProvider,
        Func<ProofreadingPurpose> purposeProvider)
        : base(
            () => credentials.GetApiKey(ApiProvider.Google, sourceProvider()),
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

    internal GeminiProofreadingClient(
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
        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"v1beta/models/{Uri.EscapeDataString(Model)}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(requestJson, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// 打ち切り（MAX_TOKENS）・安全フィルタ等で途中までしか生成されていない応答を、
    /// 正常な「修正版全文」として扱ってはいけない（基底クラスの説明を参照）。
    /// finishReason は生成が正常終了したときだけ STOP になる（未指定は旧仕様互換として許容）。
    /// </summary>
    protected override void EnsureCompleted(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out JsonElement candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Gemini APIから校正結果が返されませんでした。");
        }

        if (candidates[0].TryGetProperty("finishReason", out JsonElement finishReason) &&
            finishReason.ValueKind == JsonValueKind.String &&
            !string.Equals(finishReason.GetString(), "STOP", StringComparison.Ordinal))
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                $"Gemini APIの生成が最後まで完了しませんでした（{finishReason.GetString()}）。");
        }
    }

    protected override string ExtractText(JsonElement root)
    {
        JsonElement first = root.GetProperty("candidates")[0];

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

    protected override GeminiUsage ExtractUsage(JsonElement root)
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

    protected override string BuildRequestJson(
        string systemInstruction,
        string userMessage)
    {
        // raw文字列リテラルの改行はソースの改行コードに依存する。
        // 検証済みプロンプトを環境にかかわらず同じバイト列で送るためLFへ統一する。
        string normalizedSystemInstruction =
            systemInstruction.ReplaceLineEndings("\n");
        JsonObject generationConfig = new()
        {
            ["temperature"] = 1.0,
            ["responseMimeType"] = "text/plain",
            // 出力上限を明示しないと既定値まかせになり、打ち切りの発生条件がモデル側の
            // 都合で変わる。校正対象は 1 リクエストあたり 2,000 文字以下（要件 3.3.2）で、
            // 出力は「修正版全文」1 本なので、思考トークンを含めても十分な余裕を取った値。
            // 万一これで足りなくても finishReason=MAX_TOKENS として弾かれる（黙って切れない）。
            ["maxOutputTokens"] = MaxOutputTokens
        };

        // Gemini 3 系は thinkingLevel を明示しないとモデル既定に従う。gemini-3.1-pro-preview は
        // 既定が high 思考で、思考トークンは出力単価で課金されるため必ず明示する（要件3.5.1）。
        // thinkingBudget との併用は 400 になるので送らない。
        if (ProofreadingModelCatalog.TryGetGeminiThinkingLevel(Model, _purposeProvider(), out string? level))
        {
            generationConfig["thinkingConfig"] = new JsonObject
            {
                ["thinkingLevel"] = level
            };
        }

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
            ["generationConfig"] = generationConfig
        };

        return request.ToJsonString();
    }
}
