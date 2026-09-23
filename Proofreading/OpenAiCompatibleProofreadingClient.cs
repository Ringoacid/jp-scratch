using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>ユーザー登録の Chat Completions 互換エンドポイントへ送る校正クライアント。</summary>
internal sealed class OpenAiCompatibleProofreadingClient : ProofreadingClientBase
{
    private readonly Func<OpenAiCompatibleProfile?> _profileProvider;

    protected override string ProviderName => "OpenAI API互換";
    protected override bool RequiresApiKey => false;

    internal OpenAiCompatibleProofreadingClient(
        CredentialService credentials,
        Func<OpenAiCompatibleProfile?> profileProvider,
        Func<string> modelProvider,
        Func<TimeSpan> timeoutProvider)
        : this(
            () => profileProvider() is { } profile ? credentials.GetCompatibleApiKey(profile.Id) : null,
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            profileProvider, modelProvider, timeoutProvider, ownsHttpClient: true)
    {
    }

    internal OpenAiCompatibleProofreadingClient(
        Func<string?> apiKeyProvider,
        HttpClient httpClient,
        Func<OpenAiCompatibleProfile?> profileProvider,
        Func<string> modelProvider,
        Func<TimeSpan>? timeoutProvider = null,
        bool ownsHttpClient = false)
        : base(apiKeyProvider, httpClient, modelProvider, "openai-compatible:missing",
            new Uri("https://example.invalid/"), delay: null, timeoutProvider, ownsHttpClient)
    {
        _profileProvider = profileProvider;
    }

    private OpenAiCompatibleProfile Profile => _profileProvider() is { } profile &&
        OpenAiCompatibleProfile.IsValid(profile)
            ? profile
            : throw new GeminiClientException(GeminiClientError.BackendUnavailable,
                "OpenAI API互換の接続設定が見つかりません。");

    protected override HttpRequestMessage CreateHttpRequest(string requestJson, string apiKey)
    {
        HttpRequestMessage request = new(HttpMethod.Post, Profile.EndpointUrl);
        if (apiKey.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return request;
    }

    protected override string BuildRequestJson(string systemInstruction, string userMessage)
        => new JsonObject
        {
            ["model"] = Profile.ModelId,
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
        }.ToJsonString();

    protected override void EnsureCompleted(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("finish_reason", out JsonElement reason) ||
            reason.ValueKind != JsonValueKind.String ||
            reason.GetString() != "stop")
            throw new GeminiClientException(GeminiClientError.InvalidResponse,
                "OpenAI API互換の応答が最後まで完了しませんでした。");
    }

    protected override string ExtractText(JsonElement root)
    {
        JsonElement choice = root.GetProperty("choices")[0];
        if (!choice.TryGetProperty("message", out JsonElement message) ||
            !message.TryGetProperty("content", out JsonElement content) ||
            content.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(content.GetString()))
            throw new GeminiClientException(GeminiClientError.InvalidResponse,
                "OpenAI API互換の応答に本文がありません。");
        return content.GetString()!;
    }

    protected override GeminiUsage ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) ||
            usage.ValueKind != JsonValueKind.Object ||
            !usage.TryGetProperty("prompt_tokens", out JsonElement prompt) ||
            prompt.ValueKind != JsonValueKind.Number ||
            !prompt.TryGetInt32(out int promptTokens) || promptTokens < 0 ||
            !usage.TryGetProperty("completion_tokens", out JsonElement output) ||
            output.ValueKind != JsonValueKind.Number ||
            !output.TryGetInt32(out int outputTokens) || outputTokens < 0)
            return GeminiUsage.Unknown;

        return new GeminiUsage(promptTokens, outputTokens, 0, 0,
            ReadInt(usage, "total_tokens"));
    }
}
