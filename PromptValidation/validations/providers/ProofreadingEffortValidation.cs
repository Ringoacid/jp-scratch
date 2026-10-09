using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JpScratch.Models;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

/// <summary>
/// 思考量（effort）の設定が、モデルごとの許容値・設定の正規化・実際のリクエストまで
/// 一貫して反映されることの自己テスト。範囲外の値は API が 400 を返すため、
/// 「選べない値は送らず既定へ戻る」ことを特に守る。
/// </summary>
internal static class ProofreadingEffortValidation
{
    private const string TestApiKey = "effort-local-test-key";

    internal static async Task<bool> RunSelfTestsAsync()
    {
        bool resolvePass = TestResolveEffort();
        bool thinkingOffPass = TestThinkingOffType();
        bool settingsPass = TestSettingsNormalization();
        bool catalogPass = TestEffortChoicesAreWithinKnownLevels();
        bool openAiPass = await TestOpenAiRequestAsync();
        bool anthropicPass = await TestAnthropicRequestAsync();

        Console.WriteLine($"思考量（モデルごとの許容値・既定への戻り）: {(resolvePass ? "PASS" : "FAIL")}");
        Console.WriteLine($"思考量（Anthropic の thinking 無効化は xhigh / max で送らない）: {(thinkingOffPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"思考量（設定の正規化）: {(settingsPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"思考量（選択肢の表の整合）: {(catalogPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"思考量（OpenAI リクエストへの反映）: {(openAiPass ? "PASS" : "FAIL")}");
        Console.WriteLine($"思考量（Anthropic リクエストへの反映）: {(anthropicPass ? "PASS" : "FAIL")}");
        return resolvePass && thinkingOffPass && settingsPass && catalogPass && openAiPass && anthropicPass;
    }

    private static bool TestResolveEffort()
    {
        const ProofreadingPurpose auto = ProofreadingPurpose.Automatic;
        const ProofreadingPurpose manual = ProofreadingPurpose.Manual;
        return
            // 未選択は従来どおり（自動 low / 手動 medium）。
            ProofreadingModelCatalog.ResolveEffort("claude-haiku-5-5", auto, "") == "low" &&
            ProofreadingModelCatalog.ResolveEffort("claude-haiku-5-5", manual, null) == "medium" &&
            // 選べる値はそのまま通る。
            ProofreadingModelCatalog.ResolveEffort("claude-sonnet-5-5", manual, "xhigh") == "xhigh" &&
            ProofreadingModelCatalog.ResolveEffort("gemini-3.6-flash", auto, "minimal") == "minimal" &&
            ProofreadingModelCatalog.ResolveEffort("plamo-3.0-prime", manual, "none") == "none" &&
            // そのモデルに無い値は既定へ戻す（送ると 400 になる）。
            ProofreadingModelCatalog.ResolveEffort("gemini-3.8-flash", auto, "minimal") == "low" &&
            ProofreadingModelCatalog.ResolveEffort("gpt-6.1-sol", manual, "minimal") == "medium" &&
            ProofreadingModelCatalog.ResolveEffort("gpt-6.1-sol", manual, "xhigh") == "medium" &&
            ProofreadingModelCatalog.ResolveEffort("plamo-3.0-prime", auto, "high") == "none" &&
            // 指定できないモデルは何を渡しても送らない。
            ProofreadingModelCatalog.ResolveEffort("claude-haiku-4-5-20251001", manual, "high") is null &&
            // Gemini の thinkingLevel も同じ解決を通る。
            ProofreadingModelCatalog.TryGetGeminiThinkingLevel(
                "gemini-3.6-flash", manual, out string? level, "high") && level == "high" &&
            !ProofreadingModelCatalog.TryGetGeminiThinkingLevel("gpt-6-luna", manual, out _, "high");
    }

    private static bool TestThinkingOffType()
        => ProofreadingModelCatalog.ThinkingOffType("claude-sonnet-5", "low") == "disabled" &&
           ProofreadingModelCatalog.ThinkingOffType("claude-haiku-5-5", "high") == "disabled" &&
           ProofreadingModelCatalog.ThinkingOffType("claude-haiku-5-5", "xhigh") is null &&
           ProofreadingModelCatalog.ThinkingOffType("claude-opus-5", "max") is null &&
           // Sonnet 5.5 は disabled を使わず between_tools。これも xhigh / max では送れない。
           ProofreadingModelCatalog.ThinkingOffType("claude-sonnet-5-5", "medium") == "between_tools" &&
           ProofreadingModelCatalog.ThinkingOffType("claude-sonnet-5-5", "max") is null &&
           // 思考を無効化できないモデル・非対応モデルには送らない。
           ProofreadingModelCatalog.ThinkingOffType("claude-opus-5-5", "low") is null &&
           ProofreadingModelCatalog.ThinkingOffType("claude-fable-5-1", "low") is null &&
           ProofreadingModelCatalog.ThinkingOffType("claude-haiku-4-5-20251001", null) is null;

    private static bool TestSettingsNormalization()
        // 空白は除き、そのモデルに無い値は既定（空文字）へ戻す。旧バージョンの設定（項目なし）も空文字。
        => ProofreadingModelCatalog.NormalizeEffort("claude-haiku-5-5", " high ") == "high" &&
           ProofreadingModelCatalog.NormalizeEffort("claude-sonnet-5-5", "xhigh") == "xhigh" &&
           ProofreadingModelCatalog.NormalizeEffort("gpt-6.1-sol", "minimal") == "" &&
           ProofreadingModelCatalog.NormalizeEffort("gpt-6.1-sol", "xhigh") == "" &&
           ProofreadingModelCatalog.NormalizeEffort("claude-haiku-4-5-20251001", "high") == "" &&
           ProofreadingModelCatalog.NormalizeEffort("gemini-3.8-flash", null) == "" &&
           ProofreadingModelCatalog.NormalizeEffort("gemini-3.8-flash", "") == "" &&
           new AppSettings().AutoProofreadingEffort == "" &&
           new AppSettings().ManualProofreadingEffort == "";

    /// <summary>
    /// 選択肢が低い順に並び、既定値（用途別）が必ず選択肢に含まれること。
    /// 含まれないと、設定画面の「既定（low）」が実際に送る値と食い違う。
    /// </summary>
    private static bool TestEffortChoicesAreWithinKnownLevels()
    {
        string[] order = ["none", "minimal", "low", "medium", "high", "xhigh", "max"];
        foreach (ModelDescriptor descriptor in ProofreadingModelCatalog.All)
        {
            if (descriptor.EffortChoices.Count == 0)
            {
                if (descriptor.EffortFor(ProofreadingPurpose.Automatic) is not null) return false;
                continue;
            }

            int previous = -1;
            foreach (string choice in descriptor.EffortChoices)
            {
                int index = Array.IndexOf(order, choice);
                if (index <= previous) return false;
                previous = index;
            }

            if (!descriptor.EffortChoices.Contains(descriptor.EffortFor(ProofreadingPurpose.Automatic)!) ||
                !descriptor.EffortChoices.Contains(descriptor.EffortFor(ProofreadingPurpose.Manual)!))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> TestOpenAiRequestAsync()
    {
        string? chosen = "high";
        string? effort = await SendOpenAiAsync("gpt-6.1-sol", ProofreadingPurpose.Automatic, () => chosen);
        chosen = "minimal";   // この Sol は minimal を 400 にするので、既定の low へ戻ること。
        string? fallback = await SendOpenAiAsync("gpt-6.1-sol", ProofreadingPurpose.Automatic, () => chosen);
        chosen = "";
        string? unset = await SendOpenAiAsync("gpt-6.1-sol", ProofreadingPurpose.Manual, () => chosen);
        return effort == "high" && fallback == "low" && unset == "medium";
    }

    private static async Task<string?> SendOpenAiAsync(
        string model,
        ProofreadingPurpose purpose,
        Func<string?> effortProvider)
    {
        var handler = new CaptureHandler(() => new JsonObject { ["output_text"] = "文章です。" });
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new OpenAiProofreadingClient(
            () => TestApiKey,
            http,
            model,
            delay: (_, _) => Task.CompletedTask,
            purposeProvider: () => purpose,
            effortProvider: effortProvider);

        await client.ProofreadAsync(new ProofreadingRequest(0, 0, "文章です。", "", "", "hash", 1, 0, 1));
        if (handler.Body is null) return null;
        using JsonDocument request = JsonDocument.Parse(handler.Body);
        return request.RootElement.GetProperty("reasoning").GetProperty("effort").GetString();
    }

    private static async Task<bool> TestAnthropicRequestAsync()
    {
        // 自動用の既定: Haiku 5.5 は thinking を無効化し、effort は low。
        JsonElement haikuDefault = await SendAnthropicAsync("claude-haiku-5-5", ProofreadingPurpose.Automatic, null);
        bool haikuDefaultPass =
            haikuDefault.GetProperty("output_config").GetProperty("effort").GetString() == "low" &&
            haikuDefault.GetProperty("thinking").GetProperty("type").GetString() == "disabled";

        // xhigh では thinking の無効化が 400 になるため送らない。
        JsonElement haikuDeep = await SendAnthropicAsync("claude-haiku-5-5", ProofreadingPurpose.Automatic, "xhigh");
        bool haikuDeepPass =
            haikuDeep.GetProperty("output_config").GetProperty("effort").GetString() == "xhigh" &&
            !haikuDeep.TryGetProperty("thinking", out _);

        // Sonnet 5.5 は disabled ではなく between_tools。
        JsonElement sonnet = await SendAnthropicAsync("claude-sonnet-5-5", ProofreadingPurpose.Automatic, "medium");
        bool sonnetPass =
            sonnet.GetProperty("output_config").GetProperty("effort").GetString() == "medium" &&
            sonnet.GetProperty("thinking").GetProperty("type").GetString() == "between_tools";

        // 手動用は adaptive のまま、選んだ effort を送る。
        JsonElement sonnetManual = await SendAnthropicAsync("claude-sonnet-5-5", ProofreadingPurpose.Manual, "max");
        bool manualPass =
            sonnetManual.GetProperty("output_config").GetProperty("effort").GetString() == "max" &&
            sonnetManual.GetProperty("thinking").GetProperty("type").GetString() == "adaptive";

        // Haiku 4.5 は effort を受け付けないので、何を選んでいても送らない。
        JsonElement legacy = await SendAnthropicAsync(
            "claude-haiku-4-5-20251001", ProofreadingPurpose.Manual, "high");
        bool legacyPass =
            !legacy.TryGetProperty("output_config", out _) &&
            !legacy.TryGetProperty("thinking", out _);

        return haikuDefaultPass && haikuDeepPass && sonnetPass && manualPass && legacyPass;
    }

    private static async Task<JsonElement> SendAnthropicAsync(
        string model,
        ProofreadingPurpose purpose,
        string? chosenEffort)
    {
        var handler = new CaptureHandler(() => new JsonObject
        {
            ["stop_reason"] = "end_turn",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "文章です。" }),
            ["usage"] = new JsonObject { ["input_tokens"] = 12, ["output_tokens"] = 5 },
        });
        using HttpClient http = new(handler) { BaseAddress = new Uri("https://example.invalid/") };
        using var client = new AnthropicProofreadingClient(
            () => TestApiKey,
            http,
            model,
            delay: (_, _) => Task.CompletedTask,
            purposeProvider: () => purpose,
            effortProvider: () => chosenEffort);

        await client.ProofreadAsync(new ProofreadingRequest(0, 0, "文章です。", "", "", "hash", 1, 0, 1));
        using JsonDocument request = JsonDocument.Parse(handler.Body ?? "{}");
        return request.RootElement.Clone();
    }

    private sealed class CaptureHandler(Func<JsonObject> response) : HttpMessageHandler
    {
        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response().ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
    }
}
