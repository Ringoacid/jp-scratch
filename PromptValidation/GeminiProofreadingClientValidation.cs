using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICSharpCode.AvalonEdit.Document;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class GeminiProofreadingClientValidation
{
    private const string TestApiKey = "local-test-api-key";

    internal static async Task<bool> RunSelfTestsAsync()
    {
        bool successPass = await TestSuccessAsync();
        bool contextPass = await TestContextRequestAsync();
        bool alternativePass = await TestAlternativeRequestAsync();
        bool invalidAlternativeUsagePass = await TestInvalidAlternativeUsageAsync();
        bool retryPass = await TestRetryAsync();
        bool permanentFailurePass = await TestPermanentFailureAsync();
        bool timeoutPass = await TestTimeoutAsync();
        bool missingKeyPass = await TestMissingKeyAsync();
        bool truncatedPass = await TestTruncatedResponseAsync();

        Console.WriteLine($"Geminiクライアント（成功・差分）: {(successPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（前後文脈）: {(contextPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（理由つき別案）: {(alternativePass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（無効別案の使用量）: {(invalidAlternativeUsagePass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（1回リトライ）: {(retryPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（恒久エラー）: {(permanentFailurePass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（タイムアウト）: {(timeoutPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（キー未設定）: {(missingKeyPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"Geminiクライアント（打ち切り応答の拒否・maxOutputTokens）: {(truncatedPass ? "PASS" : "FAIL")}");

        return successPass && contextPass && alternativePass && invalidAlternativeUsagePass &&
               retryPass && permanentFailurePass &&
               timeoutPass && missingKeyPass && truncatedPass;
    }

    /// <summary>
    /// 出力上限などで途中までしか生成されなかった応答（finishReason ≠ STOP）を、正常な
    /// 「修正版全文」として採用しないこと。採用してしまうと、切れた本文がそのまま差分に掛かって
    /// 「本文末尾を削除する提案」になり、削除量が小さければ安全検査も通ってしまう。
    /// あわせて、出力上限をリクエストで明示していることも確かめる。
    /// </summary>
    private static async Task<bool> TestTruncatedResponseAsync()
    {
        var handler = new StubHandler((_, _, _) =>
        {
            HttpResponseMessage response = SuccessResponse("この文章は", 11, 1, 2, 14);
            // 応答本体へ finishReason=MAX_TOKENS を差し込む。
            string body = response.Content.ReadAsStringAsync().Result;
            JsonNode node = JsonNode.Parse(body)!;
            node["candidates"]![0]!["finishReason"] = "MAX_TOKENS";
            response.Content = new StringContent(
                node.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        });
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);

        try
        {
            await client.ProofreadAsync(new ProofreadingRequest(
                0, 12, "この文章は誤りです。", null, null, "hash", 0, 0, 1));
            return false;   // 例外にならず採用されてしまった
        }
        catch (GeminiClientException ex) when (ex.Error == GeminiClientError.InvalidResponse)
        {
            // 期待どおり。続けて maxOutputTokens が送られていることを確認する。
        }

        if (handler.LastBody is null)
            return false;

        using JsonDocument request = JsonDocument.Parse(handler.LastBody);
        return request.RootElement
            .GetProperty("generationConfig")
            .TryGetProperty("maxOutputTokens", out JsonElement maxOutputTokens) &&
            maxOutputTokens.GetInt32() > 0;
    }

    private static async Task<bool> TestAlternativeRequestAsync()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(
            SuccessResponse("が", 11, 1, 2, 14)));
        using HttpClient http = new(handler)
        {
            BaseAddress = new Uri("https://example.invalid/")
        };
        using var client = CreateClient(http);
        var document = new TextDocument("この文章ア誤りです。");
        using var session = new ProofreadingSession(document);
        session.LoadCorrectedDocument("この文章は誤りです。");
        ProofreadingProposal proposal = session.Proposals.Single();

        GeminiAlternativeResult result = await client.GenerateAlternativeAsync(
            proposal,
            "助詞は「は」ではありません");
        if (handler.LastBody is null || result.Alternative != "が")
            return false;

        using JsonDocument request = JsonDocument.Parse(handler.LastBody);
        string system = request.RootElement.GetProperty("systemInstruction")
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        string user = request.RootElement.GetProperty("contents")[0]
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        bool systemPass =
            system ==
            ProofreadingPrompt.AlternativeSystemInstruction.ReplaceLineEndings("\n");
        bool originalPass =
            user.Contains("<original>\nア\n</original>", StringComparison.Ordinal);
        bool suggestionPass =
            user.Contains(
                "<rejected-suggestion>\nは\n</rejected-suggestion>",
                StringComparison.Ordinal);
        bool reasonPass =
            user.Contains(
                "<user-reason>\n助詞は「は」ではありません\n</user-reason>",
                StringComparison.Ordinal);
        bool usagePass = result.Usage.BillableOutputTokens == 3;
        bool pass =
            systemPass && originalPass && suggestionPass && reasonPass && usagePass;
        if (!pass)
        {
            Console.WriteLine(
                "  理由つき別案の内訳: " +
                $"system={systemPass}, original={originalPass}, " +
                $"suggestion={suggestionPass}, reason={reasonPass}, usage={usagePass}");
            Console.WriteLine($"  別案リクエスト: {EscapeForDiagnostic(user)}");
            Console.WriteLine(
                $"  出力トークン: {result.Usage.BillableOutputTokens}");
        }
        return pass;
    }

    private static async Task<bool> TestInvalidAlternativeUsageAsync()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(
            SuccessResponse("は", prompt: 11, candidate: 1, thoughts: 2, total: 14)));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);
        var document = new TextDocument("この文章ア誤りです。");
        using var session = new ProofreadingSession(document);
        session.LoadCorrectedDocument("この文章は誤りです。");
        ProofreadingProposal proposal = session.Proposals.Single();

        try
        {
            await client.GenerateAlternativeAsync(proposal, "助詞は「は」ではありません");
            return false;
        }
        catch (GeminiClientException ex)
        {
            return handler.Count == 1 &&
                   ex.Error == GeminiClientError.InvalidResponse &&
                   ex.Usage is { PromptTokens: 11, BillableOutputTokens: 3 } &&
                   ex.Elapsed is { } elapsed && elapsed >= TimeSpan.Zero;
        }
    }

    private static async Task<bool> TestContextRequestAsync()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(
            SuccessResponse("文章です。", 5, 2, 0, 7)));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);
        var request = new ProofreadingRequest(
            10,
            5,
            "文s尿です。",
            "前の段落。",
            "後の段落。",
            "hash",
            1,
            0,
            1);

        GeminiProofreadingResult result = await client.ProofreadAsync(request);
        if (handler.LastBody is null || !result.Diff.Accepted)
            return false;

        using JsonDocument document = JsonDocument.Parse(handler.LastBody);
        string user = document.RootElement.GetProperty("contents")[0]
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        string expected = (
            """
            <context-before correction-allowed="false">
            前の段落。
            </context-before>
            <document>
            文s尿です。
            </document>
            <context-after correction-allowed="false">
            後の段落。
            </context-after>
            """).ReplaceLineEndings("\n");
        bool pass = user == expected;
        if (!pass)
        {
            Console.WriteLine($"  前後文脈の期待値: {EscapeForDiagnostic(expected)}");
            Console.WriteLine($"  前後文脈の実際値: {EscapeForDiagnostic(user)}");
        }
        return pass;
    }

    private static async Task<bool> TestSuccessAsync()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(
            SuccessResponse("この文章です。", prompt: 12, candidate: 4, thoughts: 3, total: 19)));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);

        GeminiProofreadingResult result =
            await client.ProofreadAsync("この文s尿です。");

        bool requestPass = ValidateRequest(handler);
        bool usagePass =
            result.Usage.PromptTokens == 12 &&
            result.Usage.CandidateTokens == 4 &&
            result.Usage.ThoughtsTokens == 3 &&
            result.Usage.BillableOutputTokens == 7 &&
            result.Usage.TotalTokens == 19;
        bool diffPass =
            result.Diff.Accepted &&
            result.Diff.Changes.Count == 1 &&
            DocumentDiff.Apply("この文s尿です。", result.Diff.Changes) == "この文章です。";

        return handler.Count == 1 && result.Attempts == 1 &&
               requestPass && usagePass && diffPass;
    }

    private static async Task<bool> TestRetryAsync()
    {
        var handler = new StubHandler((count, _, _) => Task.FromResult(
            count == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : SuccessResponse("文章です。", 2, 1, 0, 3)));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);

        GeminiProofreadingResult result = await client.ProofreadAsync("文s尿です。");
        return handler.Count == 2 && result.Attempts == 2 && result.Diff.Accepted;
    }

    private static async Task<bool> TestPermanentFailureAsync()
    {
        var handler = new StubHandler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"invalid key: {TestApiKey}")
            }));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);

        try
        {
            await client.ProofreadAsync("文章です。");
            return false;
        }
        catch (GeminiClientException ex)
        {
            return handler.Count == 1 &&
                   ex.Error == GeminiClientError.RequestFailed &&
                   ex.StatusCode == HttpStatusCode.BadRequest &&
                   !ex.Message.Contains(TestApiKey, StringComparison.Ordinal) &&
                   !ex.Message.Contains("invalid key", StringComparison.Ordinal);
        }
    }

    private static async Task<bool> TestTimeoutAsync()
    {
        var handler = new StubHandler((_, _, cancellationToken) =>
            Task.FromException<HttpResponseMessage>(
                new OperationCanceledException(cancellationToken)));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = CreateClient(http);

        try
        {
            await client.ProofreadAsync("文章です。");
            return false;
        }
        catch (GeminiClientException ex)
        {
            return handler.Count == 2 && ex.Error == GeminiClientError.Timeout;
        }
    }

    private static async Task<bool> TestMissingKeyAsync()
    {
        var handler = new StubHandler((_, _, _) =>
            Task.FromResult(SuccessResponse("文章です。", 1, 1, 0, 2)));
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new GeminiProofreadingClient(
            () => null,
            http,
            delay: (_, _) => Task.CompletedTask);

        try
        {
            await client.ProofreadAsync("文章です。");
            return false;
        }
        catch (GeminiClientException ex)
        {
            return handler.Count == 0 && ex.Error == GeminiClientError.MissingApiKey;
        }
    }

    private static GeminiProofreadingClient CreateClient(HttpClient http)
        => new(
            () => TestApiKey,
            http,
            delay: (_, _) => Task.CompletedTask,
            requestTimeout: TimeSpan.FromSeconds(1));

    private static string EscapeForDiagnostic(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool ValidateRequest(StubHandler handler)
    {
        if (handler.LastBody is null ||
            handler.LastRequestUri is null ||
            handler.LastApiKey != TestApiKey ||
            handler.LastRequestUri.Contains(TestApiKey, StringComparison.Ordinal))
        {
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(handler.LastBody);
        JsonElement root = document.RootElement;
        JsonElement config = root.GetProperty("generationConfig");
        string system = root.GetProperty("systemInstruction")
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
        string user = root.GetProperty("contents")[0]
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";

        return config.GetProperty("temperature").GetDouble() == 1.0 &&
               config.GetProperty("responseMimeType").GetString() == "text/plain" &&
               system ==
                   ProofreadingPrompt.SystemInstruction.ReplaceLineEndings("\n") &&
               user == "<document>\nこの文s尿です。\n</document>";
    }

    private static HttpResponseMessage SuccessResponse(
        string correctedText,
        int prompt,
        int candidate,
        int thoughts,
        int total)
    {
        JsonObject body = new()
        {
            ["candidates"] = new JsonArray(new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject
                    {
                        ["text"] = correctedText
                    })
                }
            }),
            ["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = prompt,
                ["candidatesTokenCount"] = candidate,
                ["thoughtsTokenCount"] = thoughts,
                ["totalTokenCount"] = total
            }
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class StubHandler(
        Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
        : HttpMessageHandler
    {
        internal int Count { get; private set; }
        internal string? LastBody { get; private set; }
        internal string? LastRequestUri { get; private set; }
        internal string? LastApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Count++;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastRequestUri = request.RequestUri?.ToString();
            LastApiKey = request.Headers.TryGetValues("x-goog-api-key", out IEnumerable<string>? values)
                ? values.SingleOrDefault()
                : null;
            return await response(Count, request, cancellationToken);
        }
    }
}
