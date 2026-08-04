using System.Net;
using System.Net.Http;
using System.Text;
using JpScratch.Models;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

/// <summary>
/// 全プロバイダーの「完了判定」をまとめて検証する（要件 3.5.1）。
///
/// 打ち切られた応答を「修正版全文」として差分に掛けると本文末尾を削除する提案になり、
/// 削除量が 200 書記素以下かつ変更比率 20% 以下なら安全検査を通過して誤字修正と同じ見た目で
/// 提示される（一括許可で本文が失われる）。<see cref="ProofreadingClientBase.EnsureCompleted"/> は
/// このためにある抽象メソッドで、この表がその実装を守る唯一の網。
///
/// **プロバイダーを追加したら、この表に行を足すこと。**
/// </summary>
internal static class ProviderCompletionGuardValidation
{
    private const string TestApiKey = "local-test-api-key";
    private const string Source = "この文章は誤りです。";

    private sealed record Case(
        string Name,
        Func<HttpClient, IProofreadingClient> CreateClient,
        string CompletedBody,
        (string Label, string Body)[] RejectedBodies);

    private static readonly Case[] Cases =
    [
        new(
            "Gemini",
            http => new GeminiProofreadingClient(
                () => TestApiKey, http, delay: (_, _) => Task.CompletedTask),
            GeminiBody("STOP"),
            [
                ("打ち切り（MAX_TOKENS）", GeminiBody("MAX_TOKENS")),
                ("安全フィルタ（SAFETY）", GeminiBody("SAFETY")),
                ("候補なし", """{"usageMetadata":{"promptTokenCount":1}}"""),
            ]),
        new(
            "OpenAI",
            http => new OpenAiProofreadingClient(
                () => TestApiKey, http, delay: (_, _) => Task.CompletedTask),
            OpenAiBody("completed"),
            [
                ("打ち切り（incomplete）", OpenAiBody("incomplete")),
                ("失敗（failed）", OpenAiBody("failed")),
            ]),
        new(
            "Anthropic",
            http => new AnthropicProofreadingClient(
                () => TestApiKey, http, delay: (_, _) => Task.CompletedTask),
            AnthropicBody("end_turn"),
            [
                ("打ち切り（max_tokens）", AnthropicBody("max_tokens")),
                ("拒否（refusal・HTTP 200）", AnthropicRefusalBody()),
                ("停止理由なし", Fill("""{"content":[{"type":"text","text":"@TEXT@"}]}""", "")),
            ]),
        new(
            "PLaMo",
            http => new PlamoProofreadingClient(
                () => TestApiKey, http, delay: (_, _) => Task.CompletedTask),
            PlamoBody("stop"),
            [
                ("打ち切り（length）", PlamoBody("length")),
                ("停止理由なし", Fill(
                    """{"choices":[{"message":{"role":"assistant","content":"@TEXT@"}}]}""", "")),
            ]),
    ];

    internal static async Task<bool> RunSelfTestsAsync()
    {
        bool all = true;

        foreach (Case testCase in Cases)
        {
            // まず正常応答が採用されること（全部投げているだけの空振りテストにしない）。
            bool completedPass = await AcceptsAsync(testCase);
            Console.WriteLine(
                $"完了判定（{testCase.Name}・正常応答を採用）: {(completedPass ? "PASS" : "FAIL")}");
            all &= completedPass;

            foreach ((string label, string body) in testCase.RejectedBodies)
            {
                bool rejectedPass = await RejectsAsync(testCase, body);
                Console.WriteLine(
                    $"完了判定（{testCase.Name}・{label}を拒否）: {(rejectedPass ? "PASS" : "FAIL")}");
                all &= rejectedPass;
            }
        }

        return all;
    }

    private static async Task<bool> AcceptsAsync(Case testCase)
    {
        using HttpClient http = CreateHttpClient(testCase.CompletedBody);
        using IProofreadingClient client = testCase.CreateClient(http);

        try
        {
            GeminiProofreadingResult result = await client.ProofreadAsync(
                new ProofreadingRequest(0, Source.Length, Source, null, null, "hash", 0, 0, 1));
            return result.CorrectedText == Source;
        }
        catch (GeminiClientException ex)
        {
            Console.WriteLine($"  正常応答が拒否された: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RejectsAsync(Case testCase, string body)
    {
        using HttpClient http = CreateHttpClient(body);
        using IProofreadingClient client = testCase.CreateClient(http);

        try
        {
            await client.ProofreadAsync(
                new ProofreadingRequest(0, Source.Length, Source, null, null, "hash", 0, 0, 1));
            return false;   // 例外にならず採用されてしまった
        }
        catch (GeminiClientException ex)
        {
            return ex.Error == GeminiClientError.InvalidResponse;
        }
    }

    private static HttpClient CreateHttpClient(string body)
        => new(new FixedResponseHandler(body))
        {
            BaseAddress = new Uri("https://example.invalid/"),
        };

    // ---- プロバイダー別の応答テンプレート ----

    // JSON の波かっこと生文字列補間が衝突するため、埋め込みはプレースホルダー置換で行う。
    private static string Fill(string template, string reason)
        => template.Replace("@REASON@", reason, StringComparison.Ordinal)
            .Replace("@TEXT@", Source, StringComparison.Ordinal);

    private static string GeminiBody(string finishReason)
        => Fill(
            """
            {"candidates":[{"finishReason":"@REASON@",
             "content":{"parts":[{"text":"@TEXT@"}]}}],
             "usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":9,"totalTokenCount":20}}
            """,
            finishReason);

    private static string OpenAiBody(string status)
        => Fill(
            """
            {"status":"@REASON@","output_text":"@TEXT@",
             "usage":{"input_tokens":11,"output_tokens":9,"total_tokens":20}}
            """,
            status);

    private static string AnthropicBody(string stopReason)
        => Fill(
            """
            {"stop_reason":"@REASON@",
             "content":[{"type":"text","text":"@TEXT@"}],
             "usage":{"input_tokens":11,"output_tokens":9}}
            """,
            stopReason);

    /// <summary>拒否は HTTP 200 のまま本文が空になる。既存2社に無い失敗の形。</summary>
    private static string AnthropicRefusalBody()
        => """
            {"stop_reason":"refusal","stop_details":{"type":"refusal","category":"cyber"},
             "content":[],"usage":{"input_tokens":11,"output_tokens":0}}
            """;

    private static string PlamoBody(string finishReason)
        => Fill(
            """
            {"choices":[{"finish_reason":"@REASON@",
             "message":{"role":"assistant","content":"@TEXT@"}}],
             "usage":{"prompt_tokens":11,"completion_tokens":9,"total_tokens":20}}
            """,
            finishReason);

    private sealed class FixedResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
