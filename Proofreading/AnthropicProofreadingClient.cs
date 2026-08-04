using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>
/// Anthropic Messages API を使う校正クライアント。
/// 他プロバイダーと同じ全文校正・差分検査の契約を使うため、UIと課金ログは共通化できる。
/// </summary>
internal sealed class AnthropicProofreadingClient : ProofreadingClientBase
{
    internal const string DefaultModel = ProofreadingModelCatalog.DefaultManualModel;

    /// <summary>1 リクエストの出力トークン上限（思考トークンを含む）。<see cref="BuildRequestJson"/> 参照。</summary>
    private const int MaxOutputTokens = 16384;

    private const string AnthropicVersion = "2023-06-01";

    private static readonly Uri DefaultBaseAddress = new("https://api.anthropic.com/");

    private readonly Func<ProofreadingPurpose> _purposeProvider;

    protected override string ProviderName => "Anthropic";

    internal AnthropicProofreadingClient(
        CredentialService credentials,
        Func<ApiKeySource> sourceProvider,
        Func<string> modelProvider,
        Func<TimeSpan> requestTimeoutProvider,
        Func<ProofreadingPurpose> purposeProvider)
        : base(
            () => credentials.GetApiKey(ApiProvider.Anthropic, sourceProvider()),
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

    internal AnthropicProofreadingClient(
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
        HttpRequestMessage request = new(HttpMethod.Post, "v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// 生成が正常終了したことを確認する（基底クラスの説明を参照）。
    /// <c>stop_reason</c> は完了時だけ <c>end_turn</c>。<c>max_tokens</c> は打ち切り、
    /// <c>refusal</c> は安全分類器による拒否で、**HTTP 200 のまま本文が空か途中までになる**。
    /// このプロバイダーには旧仕様が無いので、フィールドの欠落も異常として扱う（安全側）。
    /// </summary>
    protected override void EnsureCompleted(JsonElement root)
    {
        if (!root.TryGetProperty("stop_reason", out JsonElement stopReason) ||
            stopReason.ValueKind != JsonValueKind.String)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Anthropic APIの応答に停止理由がありません。");
        }

        string? reason = stopReason.GetString();
        if (string.Equals(reason, "end_turn", StringComparison.Ordinal)) return;

        if (string.Equals(reason, "refusal", StringComparison.Ordinal))
        {
            string category =
                root.TryGetProperty("stop_details", out JsonElement details) &&
                details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("category", out JsonElement categoryValue) &&
                categoryValue.ValueKind == JsonValueKind.String
                    ? $"（{categoryValue.GetString()}）"
                    : "";
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                $"Anthropic APIが安全上の理由で応答を拒否しました{category}。");
        }

        throw new GeminiClientException(
            GeminiClientError.InvalidResponse,
            $"Anthropic APIの生成が最後まで完了しませんでした（{reason}）。");
    }

    protected override string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Anthropic APIから校正結果が返されませんでした。");
        }

        // thinking ブロックが先頭に来ることがあるため、text ブロックだけを連結する。
        string text = string.Concat(
            content.EnumerateArray()
                .Where(block =>
                    block.TryGetProperty("type", out JsonElement type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out _))
                .Select(block => block.GetProperty("text").GetString()));

        return string.IsNullOrWhiteSpace(text)
            ? throw new GeminiClientException(
                GeminiClientError.InvalidResponse,
                "Anthropic APIの校正結果が空でした。")
            : text;
    }

    /// <summary>
    /// Anthropic は出力トークンに思考分を含み、**思考分の内訳を返さない**。
    /// 共通モデルでは思考を 0 とし、出力へ全量を寄せる（課金対象の合計は変わらない）。
    /// </summary>
    protected override GeminiUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage))
            return new GeminiUsage(0, 0, 0, 0, 0);

        int promptTokens = ReadInt(usage, "input_tokens");
        int outputTokens = ReadInt(usage, "output_tokens");
        int cachedTokens = ReadInt(usage, "cache_read_input_tokens");
        return new GeminiUsage(
            promptTokens,
            outputTokens,
            0,
            cachedTokens,
            promptTokens + outputTokens);
    }

    protected override string BuildRequestJson(string systemInstruction, string userMessage)
    {
        ProofreadingPurpose purpose = _purposeProvider();
        ModelDescriptor descriptor = ProofreadingModelCatalog.Get(Model);

        JsonObject request = new()
        {
            ["model"] = Model,
            // 出力上限を明示しないと既定値まかせになり、打ち切りの発生条件がモデル側の都合で
            // 変わる。万一足りなくても stop_reason=max_tokens として弾かれる（黙って切れない）。
            ["max_tokens"] = MaxOutputTokens,
            ["system"] = systemInstruction.ReplaceLineEndings("\n"),
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = userMessage,
            }),
        };

        // temperature / top_p / top_k は Fable 5・Opus 5・Sonnet 5 では 400 になるため送らない
        // （要件 3.5.1）。末尾 assistant ターンによるプレフィルも同じ理由で使わない。

        if (descriptor.EffortFor(purpose) is { } effort)
            request["output_config"] = new JsonObject { ["effort"] = effort };

        // 自動用は思考を切って速度と費用を優先する。ただし Fable 5 は無効化そのものが 400、
        // Haiku 4.5 は adaptive 非対応なので、どちらにも thinking を送らない。
        if (purpose == ProofreadingPurpose.Automatic)
        {
            if (ProofreadingModelCatalog.SupportsDisabledThinking(Model))
                request["thinking"] = new JsonObject { ["type"] = "disabled" };
        }
        else if (ProofreadingModelCatalog.SupportsAdaptiveThinking(Model))
        {
            request["thinking"] = new JsonObject { ["type"] = "adaptive" };
        }

        return request.ToJsonString();
    }
}
