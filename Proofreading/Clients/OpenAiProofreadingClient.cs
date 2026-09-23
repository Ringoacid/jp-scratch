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
/// 他プロバイダーと同じ全文校正・差分検査の契約を使うため、UIと課金ログは共通化できる。
/// </summary>
internal sealed class OpenAiProofreadingClient : ProofreadingClientBase
{
    internal const string DefaultModel = ProofreadingModelCatalog.OpenAiModel;

    /// <summary>1 リクエストの出力トークン上限（推論トークンを含む）。<see cref="BuildRequestJson"/> 参照。</summary>
    private const int MaxOutputTokens = 16384;

    private static readonly Uri DefaultBaseAddress =
        new("https://api.openai.com/");

    private readonly Func<ProofreadingPurpose> _purposeProvider;

    protected override string ProviderName => "OpenAI";

    internal OpenAiProofreadingClient(
        CredentialService credentials,
        Func<ApiKeySource> sourceProvider,
        Func<string> modelProvider,
        Func<TimeSpan> requestTimeoutProvider,
        Func<ProofreadingPurpose> purposeProvider)
        : base(
            () => credentials.GetApiKey(ApiProvider.OpenAi, sourceProvider()),
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

    internal OpenAiProofreadingClient(
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
        HttpRequestMessage request = new(HttpMethod.Post, "v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// 打ち切り（max_output_tokens 到達）や中断で途中までしか生成されていない応答を、
    /// 正常な「修正版全文」として扱ってはいけない（基底クラスの説明を参照）。
    /// Responses API の status は完了時だけ "completed"（未指定は旧仕様互換として許容）。
    /// </summary>
    protected override void EnsureCompleted(JsonElement root)
    {
        if (!root.TryGetProperty("status", out JsonElement status) ||
            status.ValueKind != JsonValueKind.String ||
            string.Equals(status.GetString(), "completed", StringComparison.Ordinal))
        {
            return;
        }

        string detail = root.TryGetProperty("incomplete_details", out JsonElement incomplete) &&
                        incomplete.TryGetProperty("reason", out JsonElement reason) &&
                        reason.ValueKind == JsonValueKind.String
            ? $"{status.GetString()}: {reason.GetString()}"
            : status.GetString() ?? "";
        throw new GeminiClientException(
            GeminiClientError.InvalidResponse,
            $"OpenAI APIの生成が最後まで完了しませんでした（{detail}）。");
    }

    protected override string ExtractText(JsonElement root)
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

    protected override GeminiUsage ExtractUsage(JsonElement root)
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

    protected override string BuildRequestJson(string systemInstruction, string userMessage)
    {
        JsonObject request = new()
        {
            ["model"] = Model,
            ["instructions"] = systemInstruction.ReplaceLineEndings("\n"),
            ["input"] = userMessage,
            ["store"] = false,
            // 出力上限を明示しないと既定値まかせになり、打ち切りの発生条件がモデル側の都合で
            // 変わる。校正対象は 1 リクエストあたり 2,000 文字以下（要件 3.3.2）で、出力は
            // 「修正版全文」1 本なので、推論トークンを含めても十分な余裕を取った値。
            // 万一これで足りなくても status=incomplete として弾かれる（黙って切れない）。
            ["max_output_tokens"] = MaxOutputTokens,
        };

        // 用途別の推論量（要件 3.5.1）。自動は low、手動は medium。
        if (ProofreadingModelCatalog.Get(Model).EffortFor(_purposeProvider()) is { } effort)
            request["reasoning"] = new JsonObject { ["effort"] = effort };

        return request.ToJsonString();
    }
}
