using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using JpScratch.Models;
using JpScratch.Proofreading;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class OpenAiCompatibleValidation
{
    internal static async Task<bool> RunSelfTestsAsync()
    {
        OpenAiCompatibleProfile profile = new()
        {
            Name = "社内サーバー",
            EndpointUrl = "https://example.invalid/v1/chat/completions",
            ModelId = "custom-model",
            InputUsdPerMillion = 1.25m,
            OutputUsdPerMillion = 4.50m,
        };
        var handler = new StubHandler();
        using HttpClient http = new(handler);
        using var client = new OpenAiCompatibleProofreadingClient(
            () => null, http, () => profile, () => profile.SelectionId);
        GeminiProofreadingResult result = await client.ProofreadAsync(
            new ProofreadingRequest(0, 0, "この文章です。", null, null, "hash", 0, 0, 1));
        using JsonDocument request = JsonDocument.Parse(handler.Body ?? "{}");
        bool requestPass = result.CorrectedText == "この文章です。" &&
                           handler.Uri == profile.EndpointUrl &&
                           handler.Authorization is null &&
                           request.RootElement.GetProperty("model").GetString() == profile.ModelId &&
                           request.RootElement.GetProperty("messages").GetArrayLength() == 2 &&
                           !request.RootElement.TryGetProperty("reasoning_effort", out _) &&
                           result.Usage.PromptTokens == 100 &&
                           result.Usage.BillableOutputTokens == 20;

        handler.ResponseBody = """{"choices":[{"finish_reason":"stop","message":{"content":"この文章です。"}}]}""";
        GeminiProofreadingResult unknownUsage = await client.ProofreadAsync(
            new ProofreadingRequest(0, 0, "この文章です。", null, null, "hash", 0, 0, 1));
        bool unknownUsagePass = !unknownUsage.Usage.IsKnown;

        using var keyedClient = new OpenAiCompatibleProofreadingClient(
            () => "test-key", http, () => profile, () => profile.SelectionId);
        await keyedClient.ProofreadAsync(
            new ProofreadingRequest(0, 0, "この文章です。", null, null, "hash", 0, 0, 1));
        bool keyPass = handler.Authorization == "Bearer test-key";

        string path = Path.Combine(Path.GetTempPath(), "jpscratch-compatible-" + Guid.NewGuid().ToString("N") + ".json");
        bool pricingPass;
        try
        {
            var pricing = new PricingService(path,
                compatibleProfileResolver: model => model == profile.SelectionId ? profile : null);
            PricingQuote quote = pricing.Calculate(profile.SelectionId, 100, 20);
            pricingPass = quote.IsUsd && quote.Cost == 0.000215m &&
                          quote.Pricing.InputUsdPerMillion == 1.25m;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        bool identityPass = OpenAiCompatibleProfile.TryGetProfileId(profile.SelectionId, out string id) &&
                            id == profile.Id &&
                            ProofreadingModelCatalog.ProviderOf(profile.SelectionId) == ApiProvider.OpenAiCompatible &&
                            OpenAiCompatibleProfile.Find(new AppSettings
                            {
                                OpenAiCompatibleProfiles = [profile],
                            }, profile.SelectionId) == profile &&
                            !OpenAiCompatibleProfile.IsValid(profile with
                            {
                                EndpointUrl = "https://example.invalid/v1/models",
                            });

        Console.WriteLine($"OpenAI API互換（URL・本文・キーなし・使用量）: {(requestPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"OpenAI API互換（キーあり・使用量不明）: {(keyPass && unknownUsagePass ? "PASS" : "FAIL")}");
        Console.WriteLine($"OpenAI API互換（接続設定別の概算単価）: {(pricingPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"OpenAI API互換（接続設定IDとモデル選択）: {(identityPass ? "PASS" : "FAIL")}");
        return requestPass && keyPass && unknownUsagePass && pricingPass && identityPass;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        internal string? Body { get; private set; }
        internal string? Uri { get; private set; }
        internal string? Authorization { get; private set; }
        internal string ResponseBody { get; set; } =
            """{"choices":[{"finish_reason":"stop","message":{"content":"この文章です。"}}],"usage":{"prompt_tokens":100,"completion_tokens":20,"total_tokens":120}}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Uri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
