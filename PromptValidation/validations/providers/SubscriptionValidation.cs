using System.Diagnostics;
using System.Text.Json;
using ICSharpCode.AvalonEdit.Document;
using JpScratch.Models;
using JpScratch.Proofreading;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

internal static class SubscriptionValidation
{
    private sealed class FakeBackend : ISubscriptionBackend
    {
        internal SubscriptionState State = new(true, "test-account", "test-plan", 25, null, DateTimeOffset.Now, [new("same-model", "Test model")]);
        internal string Text = "テストです。";
        internal int Sends;
        internal bool Block;
        internal bool Disposed;
        public Task<SubscriptionState> ReadStateAsync(CancellationToken token) => Task.FromResult(State with { CheckedAt = DateTimeOffset.Now });
        public Task LoginAsync(bool deviceCode, Action<string> showCode, CancellationToken token,
            Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null) => Task.CompletedTask;
        public Task LogoutAsync(CancellationToken token) { State = State with { Authenticated = false }; return Task.CompletedTask; }
        public async Task<GeminiRawTextResult> GenerateAsync(string model, string instructions, string prompt, CancellationToken token)
        {
            Sends++;
            if (Block) await Task.Delay(Timeout.Infinite, token);
            return new(Text, GeminiUsage.Unknown, TimeSpan.FromMilliseconds(1), 1);
        }
        public void Dispose() => Disposed = true;
    }

    internal static async Task<bool> RunAsync()
    {
        bool passed = true;
        void Check(string name, bool value) { Console.WriteLine($"契約接続（{name}）: {(value ? "PASS" : "FAIL")}"); passed &= value; }
        var now = DateTimeOffset.Now;
        Check("起動時の自動接続・失敗分離・終了時中断", await SubscriptionStartupValidation.RunAsync());
        Check("Copilot月間利用枠のUTCリセット日時", CopilotQuotaValidation.Run());
        Check("Copilotログイン前のSDK終了待ちでUIが応答・キャンセル可能", await CopilotLifecycleValidation.RunAsync());
        Check("Copilotの改行なし保存確認・承諾・拒否・中断と認証環境の分離", await CopilotLoginValidation.RunAsync());
        using (var loginService = new SubscriptionService((_, _) => new FakeBackend
            { State = new(false, null, null, null, null, DateTimeOffset.Now, []) }))
        {
            bool rejected = false;
            try { await loginService.LoginAsync(BackendKind.GitHubCopilot, "mock", false, _ => { }, CancellationToken.None); }
            catch (GeminiClientException e) { rejected = e.Error == GeminiClientError.AuthenticationRequired; }
            Check("CLI正常終了だけでログイン成功にせず保存済み認証を確認", rejected);
        }
        var turnParameters = JsonSerializer.SerializeToElement(CodexSubscriptionBackend.TurnParameters("test-thread", "test-model", "test text"));
        Check("Codex開始要求は廃止済みreadOnly.accessを送らずツール制限を維持",
            turnParameters.Get("sandboxPolicy").Text("type") == "readOnly" &&
            turnParameters.Get("sandboxPolicy").Get("access").ValueKind == JsonValueKind.Undefined &&
            turnParameters.Get("sandboxPolicy").Get("networkAccess").ValueKind == JsonValueKind.False &&
            turnParameters.Get("environments").GetArrayLength() == 0 && turnParameters.Text("approvalPolicy") == "never");
        var economyModels = new SubscriptionModel[] { new("gpt-6-astra", "GPT 6 Astra"),
            new("claude-fable-5-1", "Claude Fable 5.1"), new("gpt-5.6-luna", "GPT 5.6 Luna") };
        Check("Astra・Fableの警告と軽量モデルの既定選択",
            SubscriptionModelSelection.IsHighCost(economyModels[0]) && SubscriptionModelSelection.IsHighCost(economyModels[1]) &&
            !SubscriptionModelSelection.IsHighCost(economyModels[2]) && SubscriptionModelSelection.Order(economyModels)[0].Id == "gpt-5.6-luna");
        Check("モデル名よりサービスの料金・利用倍率を優先",
            SubscriptionModelSelection.Order([new("small", "Small", 2), new("other", "Other", 0.5)])[0].Id == "other" &&
            SubscriptionModelSelection.IsHighCost(new("future", "Future", 4)) &&
            SubscriptionModelSelection.Order([new("mini", "Mini", 1, 4, 8), new("other", "Other", 1, 1, 2)])[0].Id == "other");
        Check("不明な料金や自動ルーティングを最安と見なさない",
            SubscriptionModelSelection.Order([new("auto", "Auto", 0), new("gpt-luna", "Luna")])[0].Id == "gpt-luna" &&
            SubscriptionModelSelection.Order([new("future", "Future", double.NaN), new("gpt-luna", "Luna")])[0].Id == "gpt-luna");
        var sdkModel = JsonSerializer.Deserialize<GitHub.Copilot.ModelInfo>("""
            {"id":"test","name":"Test","billing":{"multiplier":3,"tokenPrices":{"inputPrice":1,"outputPrice":5}}}
            """)!;
        var billing = JsonSerializer.SerializeToElement(sdkModel).Get("billing");
        Check("SDKモデルの利用倍率・トークン単価の取得", billing.Number("multiplier") == 3 && billing.Get("tokenPrices").Number("outputPrice") == 5);
        Check("CLIアーカイブの展開・不正パス・キャンセル", TestInstaller());
        Check("動的モデル保持・APIの既存移行・未知の接続方式でAPIへ戻さない",
            BackendNames.NormalizeModel(BackendKind.CodexAppServer, "future-model", "api-default") == "future-model" &&
            BackendNames.NormalizeModel(BackendKind.GitHubCopilot, "same-model", "api-default") == "same-model" &&
            BackendNames.NormalizeModel((BackendKind)999, "future-model", "api-default") == "future-model" &&
            BackendNames.NormalizeModel(BackendKind.Api, "missing", ProofreadingModelCatalog.DefaultManualModel) == ProofreadingModelCatalog.DefaultManualModel);
        Check("10秒・30秒・利用者設定の保持・残量60秒境界",
            SubscriptionPolicy.Debounce(5000).TotalSeconds == 10 && SubscriptionPolicy.Debounce(15000).TotalSeconds == 15 &&
            SubscriptionPolicy.Interval(10).TotalSeconds == 30 && SubscriptionPolicy.Interval(90).TotalSeconds == 90 &&
            SubscriptionPolicy.IsFresh(now.AddSeconds(-59), now) && !SubscriptionPolicy.IsFresh(now.AddSeconds(-60), now));
        foreach (string status in new[] { "interrupted", "failed", "inProgress", "" })
        {
            bool rejected = false;
            try { SubscriptionPolicy.RequireCompleted(status, "途中までの本文", GeminiUsage.Unknown); }
            catch (GeminiClientException e) { rejected = e.Error == GeminiClientError.InvalidResponse; }
            Check("不完全な終端を拒否: " + status, rejected);
        }
        foreach (BackendKind backend in new[] { BackendKind.CodexAppServer, BackendKind.GitHubCopilot })
        {
            {
                var fake = new FakeBackend();
                fake.State = fake.State with { UsedPercent = null };
                using var service = new SubscriptionService((_, _) => fake);
                bool stale = false;
                try
                {
                    await service.SendAsync(backend, "mock", "same-model", "test-account", true,
                        TimeSpan.FromSeconds(2), 30, "instruction", "old-document", CancellationToken.None, () => false);
                }
                catch (SubscriptionRequestStaleException) { stale = true; }
                Check($"{backend}: 残量確認後の編集を再検査・自動再開を妨げない", stale && fake.Sends == 0 && !service.Suspended(backend));
                await service.SendAsync(backend, "mock", "same-model", "test-account", true,
                    TimeSpan.FromSeconds(2), 30, "instruction", "latest-document", CancellationToken.None);
                Check($"{backend}: 残量不明の手動承諾・送信後30秒待機", fake.Sends == 1 && service.DelayBeforeSend(1).TotalSeconds > 29);
                using var cancellation = new CancellationTokenSource(50);
                try
                {
                    await service.SendAsync(backend, "mock", "same-model", "test-account", true,
                        TimeSpan.FromSeconds(2), 30, "instruction", "too-early", cancellation.Token);
                }
                catch (OperationCanceledException) { }
                Check($"{backend}: 間隔待機中は送信せず中断可能", fake.Sends == 1);
            }
            foreach (string operation in new[] { "automatic", "manual", "alternative", "style" })
            {
                var fake = new FakeBackend();
                using var service = new SubscriptionService((_, _) => fake);
                await service.RefreshAsync(backend, "mock");
                using var client = new SubscriptionProofreadingClient(service, backend, "mock", "same-model", "test-account", false, TimeSpan.FromSeconds(2), 30);
                GeminiUsage usage;
                if (operation == "style") usage = (await client.GenerateStyleGuideAsync([])).Usage;
                else if (operation == "alternative")
                {
                    var proposal = new ProofreadingProposal(new TextDocument("テストだ。"), new DocumentChange(0, 5, "テストだ。", "テストでした。", "", ""));
                    usage = (await client.GenerateAlternativeAsync(proposal, "丁寧に")).Usage;
                }
                else usage = (await client.ProofreadAsync(new(0, 6, "テストでず。", null, null, "hash", 0, 0, 1))).Usage;
                Check($"{backend}: APIキーなし {operation}・使用量不明の保持", fake.Sends == 1 && usage.Backend == backend && !usage.IsKnown);
            }
            foreach (string failure in new[] { "auth", "account", "quota", "unknown", "model", "timeout", "cancel" })
            {
                var fake = new FakeBackend();
                fake.State = failure switch
                {
                    "auth" => fake.State with { Authenticated = false },
                    "quota" => fake.State with { UsedPercent = 100 },
                    "unknown" => fake.State with { UsedPercent = null },
                    _ => fake.State,
                };
                fake.Block = failure is "timeout" or "cancel";
                using var service = new SubscriptionService((_, _) => fake);
                await service.RefreshAsync(backend, "mock");
                using var cancellation = new CancellationTokenSource();
                if (failure == "cancel") cancellation.CancelAfter(50);
                bool rejected = false;
                try
                {
                    await service.SendAsync(backend, "mock", failure == "model" ? "missing" : "same-model",
                        failure == "account" ? "different-account" : "test-account", false,
                        TimeSpan.FromMilliseconds(50), 30, "instruction", "document", cancellation.Token);
                }
                catch (GeminiClientException) { rejected = true; }
                catch (OperationCanceledException) { rejected = failure == "cancel"; }
                Check($"{backend}: {failure}停止・自動再送なし", rejected && fake.Sends == (fake.Block ? 1 : 0));
            }
        }
        Check("履歴・API金額分離・圧縮・CSV", TestHistory());
        Check("契約件数の期間・種別・オフセット・境界精度", TestSubscriptionCounts());
        Check("JSONL応答照合・未知通知・キャンセル・終了", await TestRpcAsync());
        return passed;
    }

    private static bool TestInstaller()
    {
        string root = Path.Combine(Path.GetTempPath(), "jpscratch-installer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string output = Path.Combine(root, "output"); Directory.CreateDirectory(output);
            foreach (string invalid in new[] { "../outside", "..\\outside", "C:\\outside", "file:stream" })
            {
                try { SubscriptionCliInstaller.EntryPath(output, invalid); return false; }
                catch (IOException) { }
            }
            string archive = Path.Combine(root, "test.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("bin/test.txt").Open())) writer.Write("verified");
            SubscriptionCliInstaller.Extract(archive, output, true, CancellationToken.None);
            if (File.ReadAllText(Path.Combine(output, "bin", "test.txt")) != "verified") return false;
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { SubscriptionCliInstaller.Extract(archive, output, true, canceled.Token); return false; }
            catch (OperationCanceledException) { }
            string attack = Path.Combine(root, "attack.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(attack, System.IO.Compression.ZipArchiveMode.Create))
                zip.CreateEntry("../escape.txt");
            try { SubscriptionCliInstaller.Extract(attack, output, true, CancellationToken.None); return false; }
            catch (IOException) { }
            return !File.Exists(Path.Combine(root, "escape.txt")) &&
                SubscriptionCliInstaller.Package(BackendKind.GitHubCopilot, System.Runtime.InteropServices.Architecture.Arm64).Url.AbsolutePath.EndsWith("copilot-win32-arm64.zip");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static bool TestHistory()
    {
        using var db = new Database(":memory:");
        var repository = new ApiCallRepository(db);
        try { repository.Add(null!); return false; }
        catch (ArgumentNullException ex) when (ex.ParamName == "entry") { }
        long api = repository.Add(new(ApiCallTrigger.Manual, "same-model", 10, 20, 1.25m, 10, ApiCallStatus.Ok, null, 1, 0));
        long subscription = repository.Add(new(ApiCallTrigger.Manual, "same-model", 0, 0, 0m, 10, ApiCallStatus.Ok, null, 1, 0,
            IsUsdCostConfirmed: false, Backend: BackendKind.GitHubCopilot, IsUsageKnown: false, SubscriptionUnits: 15, SubscriptionUnit: "nano-AI unit"));
        var proposal = new ProofreadingProposal(new TextDocument("テストだ。"), new DocumentChange(0, 5, "テストだ。", "テストです。", "", ""));
        new ReactionRepository(db).Add(null, proposal, ProofreadingReaction.Accept, apiCallId: subscription);
        var row = repository.GetHistory().Rows.Single(r => r.Id == subscription);
        string[] fields = BillingCsvExporter.BuildCsv([row]).Split("\r\n")[1].Split(',');
        bool before = repository.GetUsageSummary().UsdCost == 1.25m && repository.GetLatest()?.Id == api &&
            repository.GetSubscriptionCallCount() == 1 && row.KnownUsdCost is null && !row.IsUsageKnown && fields[5] == "" && fields[3] == "";
        var compacted = repository.Compact(DateTimeOffset.Now.AddDays(1));
        var repeated = repository.Compact(DateTimeOffset.Now.AddDays(1));
        return before && compacted.CompactedDays == 1 && compacted.CompactedCalls == 2 && !repeated.DidCompact &&
            repository.GetSubscriptionCallCount(triggers: [ApiCallTrigger.Auto]) == 0 &&
            repository.GetSubscriptionCallCount(triggers: [ApiCallTrigger.Manual]) == 1 &&
            repository.GetSubscriptionCallCount() == 1 && repository.GetUsageSummary().UsdCost == 1.25m &&
            repository.GetHistory().Rows.Count == 0 &&
            db.Read("SELECT COUNT(*) FROM reactions WHERE api_call_id IS NULL;", r => r.Read() && r.GetInt32(0) == 1) &&
            db.Read("SELECT subscription_units, unknown_usage_calls FROM subscription_daily;", r => r.Read() && r.GetDouble(0) == 15 && r.GetInt32(1) == 1);
    }

    private static bool TestSubscriptionCounts()
    {
        using var db = new Database(":memory:");
        var start = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset stamp = start;
        var repository = new ApiCallRepository(db, () => stamp);
        foreach (var time in new[] { start.AddTicks(-1), start, start.AddTicks(1), start.AddDays(1).AddTicks(-1), start.AddDays(1) })
        {
            stamp = time.ToOffset(TimeSpan.FromHours(14));
            repository.Add(new(ApiCallTrigger.Manual, "model", 0, 0, 0m, 1, ApiCallStatus.Ok, null, 0, 0,
                Backend: BackendKind.CodexAppServer));
            stamp = time.ToOffset(TimeSpan.FromHours(-12));
            repository.Add(new(ApiCallTrigger.Auto, "model", 0, 0, 0m, 1, ApiCallStatus.Error, "mock", 0, 0,
                Backend: BackendKind.GitHubCopilot));
            repository.Add(new(ApiCallTrigger.Manual, "model", 0, 0, 0m, 1, ApiCallStatus.Ok, null, 0, 0));
        }
        foreach (var triggers in new ApiCallTrigger[][] { [], [ApiCallTrigger.Manual], [ApiCallTrigger.Auto], [ApiCallTrigger.StyleGuide] })
        {
            long expected = repository.GetHistory(start, start.AddDays(1), triggers).Rows.Count(row => row.Backend != BackendKind.Api);
            if (repository.GetSubscriptionCallCount(start, start.AddDays(1), triggers) != expected) return false;
        }
        return repository.GetSubscriptionCallCount(start, start.AddDays(1)) == 6 &&
            repository.GetSubscriptionCallCount(DateTimeOffset.MinValue, DateTimeOffset.MaxValue) == 10;
    }

    private static async Task<bool> TestRpcAsync()
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(SubscriptionValidation).Assembly.Location);
        info.ArgumentList.Add("--mock-codex-jsonl");
        using var rpc = new CodexRpcConnection(info);
        int notices = 0;
        rpc.Notification += _ => notices++;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var a = rpc.CallAsync("echo", new { value = "A" }, timeout.Token);
        var b = rpc.CallAsync("echo", new { value = "B" }, timeout.Token);
        var replies = await Task.WhenAll(a, b);
        bool rejectionExplained = false;
        try { await rpc.CallAsync("turn/start", new { }, timeout.Token); }
        catch (GeminiClientException ex)
        {
            rejectionExplained = ex.Error == GeminiClientError.BackendUnavailable && ex.Message.Contains("turn/start") &&
                ex.Message.Contains("-32600") && !ex.Message.Contains("private-input") && !ex.Message.Contains("private-token");
        }
        using var cancel = new CancellationTokenSource(50);
        bool cancelled = false;
        try { await rpc.CallAsync("wait", new { }, cancel.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        bool closed = false;
        try { await rpc.CallAsync("exit", new { }, timeout.Token); }
        catch (Exception) { closed = true; }
        return replies[0].Text("value") == "A" && replies[1].Text("value") == "B" && cancelled && closed && notices > 0 && rejectionExplained;
    }

    internal static async Task<int> RunMockAsync()
    {
        JsonElement? held = null;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var doc = JsonDocument.Parse(line);
            var message = doc.RootElement;
            if (message.Text("method") == "turn/start")
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id = message.Get("id"),
                    error = new { code = -32600, message = "private-input", data = "private-token" } }));
                await Console.Out.FlushAsync();
                continue;
            }
            if (message.Text("method") == "exit") return 0;
            if (message.Text("method") == "wait") continue;
            if (held is null) { held = message.Clone(); continue; }
            await Console.Out.WriteLineAsync("{\"method\":\"unknown/notification\",\"params\":{}}");
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id = message.Get("id"), result = message.Get("params") }));
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id = held.Value.Get("id"), result = held.Value.Get("params") }));
            held = null;
            await Console.Out.FlushAsync();
        }
        return 0;
    }
}
