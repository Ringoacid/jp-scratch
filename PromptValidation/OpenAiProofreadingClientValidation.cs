using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class OpenAiProofreadingClientValidation
{
    private const string TestApiKey = "openai-local-test-key";

    internal static async Task<bool> RunSelfTestsAsync()
    {
        bool successPass = await TestSuccessAsync();
        bool missingKeyPass = await TestMissingKeyAsync();
        bool errorMessagePass = await TestErrorDoesNotLeakBodyAsync();
        bool truncatedPass = await TestIncompleteResponseAsync();

        Console.WriteLine($"OpenAIクライアント（Responses API成功・使用量）: {(successPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"OpenAIクライアント（キー未設定）: {(missingKeyPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"OpenAIクライアント（エラー本文非表示）: {(errorMessagePass ? "PASS" : "FAIL")}");
        Console.WriteLine($"OpenAIクライアント（打ち切り応答の拒否・max_output_tokens）: {(truncatedPass ? "PASS" : "FAIL")}");
        return successPass && missingKeyPass && errorMessagePass && truncatedPass;
    }

    /// <summary>
    /// 途中までしか生成されなかった応答（status ≠ completed）を採用しないこと。
    /// 採用すると、切れた本文がそのまま差分に掛かって「本文末尾を削除する提案」になる。
    /// あわせて出力上限をリクエストで明示していることも確かめる。
    /// </summary>
    private static async Task<bool> TestIncompleteResponseAsync()
    {
        var handler = new StubHandler((_, _) =>
        {
            JsonObject body = new()
            {
                ["status"] = "incomplete",
                ["incomplete_details"] = new JsonObject { ["reason"] = "max_output_tokens" },
                ["output_text"] = "この文",
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = 12,
                    ["output_tokens"] = 7,
                    ["total_tokens"] = 19,
                },
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            });
        });
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new OpenAiProofreadingClient(
            () => TestApiKey,
            http,
            delay: (_, _) => Task.CompletedTask);

        try
        {
            await client.ProofreadAsync(
                new ProofreadingRequest(0, 0, "この文s尿です。", "", "", "hash", 1, 0, 1));
            return false;   // 例外にならず採用されてしまった
        }
        catch (GeminiClientException ex) when (ex.Error == GeminiClientError.InvalidResponse)
        {
            // 期待どおり。
        }

        if (handler.Body is null)
            return false;

        using JsonDocument request = JsonDocument.Parse(handler.Body);
        return request.RootElement
            .TryGetProperty("max_output_tokens", out JsonElement maxOutputTokens) &&
            maxOutputTokens.GetInt32() > 0;
    }

    private static async Task<bool> TestSuccessAsync()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(SuccessResponse()));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new OpenAiProofreadingClient(
            () => TestApiKey,
            http,
            delay: (_, _) => Task.CompletedTask);

        GeminiProofreadingResult result = await client.ProofreadAsync(
            new ProofreadingRequest(0, 0, "この文s尿です。", "", "", "hash", 1, 0, 1));

        if (handler.Body is null || handler.Authorization != $"Bearer {TestApiKey}")
            return false;

        using JsonDocument request = JsonDocument.Parse(handler.Body);
        JsonElement root = request.RootElement;
        bool requestPass =
            root.GetProperty("model").GetString() == "gpt-5.6-luna" &&
            root.GetProperty("instructions").GetString() ==
                ProofreadingPrompt.SystemInstruction.ReplaceLineEndings("\n") &&
            root.GetProperty("input").GetString() == "<document>\nこの文s尿です。\n</document>" &&
            root.GetProperty("reasoning").GetProperty("effort").GetString() == "low" &&
            root.GetProperty("store").GetBoolean() == false;
        bool usagePass =
            result.Usage.PromptTokens == 12 &&
            result.Usage.CandidateTokens == 5 &&
            result.Usage.ThoughtsTokens == 2 &&
            result.Usage.BillableOutputTokens == 7 &&
            result.Usage.CachedContentTokens == 1 &&
            result.Usage.TotalTokens == 19;
        return handler.Count == 1 && result.CorrectedText == "この文章です。" &&
               result.Diff.Accepted && requestPass && usagePass;
    }

    private static async Task<bool> TestMissingKeyAsync()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(SuccessResponse()));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new OpenAiProofreadingClient(
            () => null,
            http,
            delay: (_, _) => Task.CompletedTask);

        try
        {
            await client.ProofreadAsync(
                new ProofreadingRequest(0, 0, "文章です。", "", "", "hash", 1, 0, 1));
            return false;
        }
        catch (GeminiClientException ex)
        {
            return handler.Count == 0 && ex.Error == GeminiClientError.MissingApiKey;
        }
    }

    private static async Task<bool> TestErrorDoesNotLeakBodyAsync()
    {
        const string secret = "secret-response-body";
        var handler = new StubHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(secret, Encoding.UTF8, "application/json")
            }));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new OpenAiProofreadingClient(
            () => TestApiKey,
            http,
            delay: (_, _) => Task.CompletedTask);

        try
        {
            await client.ProofreadAsync(
                new ProofreadingRequest(0, 0, "文章です。", "", "", "hash", 1, 0, 1));
            return false;
        }
        catch (GeminiClientException ex)
        {
            return ex.Error == GeminiClientError.RequestFailed &&
                   !ex.Message.Contains(secret, StringComparison.Ordinal);
        }
    }

    private static HttpResponseMessage SuccessResponse()
    {
        JsonObject body = new()
        {
            ["output_text"] = "この文章です。",
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 12,
                ["output_tokens"] = 7,
                ["total_tokens"] = 19,
                ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 1 },
                ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 2 },
            },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(
        Func<int, CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        internal int Count { get; private set; }
        internal string? Body { get; private set; }
        internal string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Authorization = request.Headers.Authorization?.ToString();
            return await response(Count, cancellationToken);
        }
    }
}
