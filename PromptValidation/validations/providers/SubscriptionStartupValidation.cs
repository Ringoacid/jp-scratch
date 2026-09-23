using System.Collections.Concurrent;
using System.Diagnostics;
using JpScratch.Models;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class SubscriptionStartupValidation
{
    private sealed class FakeBackend(bool authenticated = true) : ISubscriptionBackend
    {
        internal SubscriptionState State = new(authenticated, authenticated ? "saved-account" : null,
            authenticated ? "saved-plan" : null, authenticated ? 25 : null, null, DateTimeOffset.Now,
            authenticated ? [new("saved-model", "Saved model")] : []);
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReadFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Block;
        internal Exception? Failure;
        internal int Reads, Logins, Sends, Disposals;
        internal bool Cancelled;
        internal bool ReadResourceReleased;

        public async Task<SubscriptionState> ReadStateAsync(CancellationToken token)
        {
            // Model a handshake resource that backend.Dispose cannot reach yet.
            var localResource = new MemoryStream();
            Reads++;
            Started.TrySetResult();
            try
            {
                if (Block) await Release.Task.WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (Failure is { } failure) throw failure;
                return State;
            }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            finally
            {
                localResource.Dispose();
                ReadResourceReleased = !localResource.CanRead;
                ReadFinished.TrySetResult();
            }
        }

        public Task LoginAsync(bool deviceCode, Action<string> showCode, CancellationToken token,
            Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null)
        { Logins++; return Task.CompletedTask; }
        public Task LogoutAsync(CancellationToken token) => Task.CompletedTask;
        public Task<GeminiRawTextResult> GenerateAsync(string model, string instructions, string prompt, CancellationToken token)
        { Sends++; return Task.FromResult(new GeminiRawTextResult("", GeminiUsage.Unknown, TimeSpan.Zero, 1)); }
        public void Dispose() { Disposals++; }
    }

    private sealed class UiContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        internal void PumpUntil(Func<bool> complete)
        {
            var limit = Stopwatch.StartNew();
            while (!complete())
            {
                if (limit.Elapsed > TimeSpan.FromSeconds(3)) throw new TimeoutException("Startup did not finish.");
                if (_queue.TryTake(out var work, 10)) work.Callback(work.State);
            }
        }
    }

    internal static async Task<bool> RunAsync()
    {
        bool passed = true;
        (string Name, Func<Task<bool>> Run)[] checks =
        [
            ("保存済み認証・モデルの自動取得、UI応答・通知・直列化・起動時1回", () => Task.FromResult(TestSignedInStartup())),
            ("未ログインでもブラウザー認証・生成を開始しない", TestSignedOutAsync),
            ("CLI未検出・認証失敗でも別サービスの接続を継続", TestFailuresAsync),
            ("起動処理前の終了でCLIを起動しない", () => Task.FromResult(TestDisposedBeforeRead())),
            ("終了時に取得を中断し、次のサービスを起動しない", TestCancellationAsync),
            ("UI停止後も起動中の取得を中断し、ローカル資源を解放", () => Task.FromResult(TestCleanupAfterUiStops())),
            ("選択したサービスだけを起動し、APIのみならCLIを作らない", TestSelectedBackendsAsync),
            ("サービス間は独立、同じサービスの待機中断は状態を壊さない", () => Task.FromResult(TestIndependentBackends())),
        ];
        foreach (var (name, run) in checks)
        {
            bool success;
            try { success = await run(); }
            catch (Exception ex)
            {
                Console.WriteLine($"契約自動接続（{name}）: {ex.GetType().Name}: {ex.Message}");
                success = false;
            }
            Console.WriteLine($"契約自動接続（{name}）: {(success ? "PASS" : "FAIL")}");
            passed &= success;
        }
        return passed;
    }

    private static async Task<bool> TestSelectedBackendsAsync()
    {
        foreach (var (auto, manual, expected) in new[]
        {
            (BackendKind.Api, BackendKind.Api, Array.Empty<BackendKind>()),
            (BackendKind.Api, BackendKind.CodexAppServer, new[] { BackendKind.CodexAppServer }),
            (BackendKind.GitHubCopilot, BackendKind.Api, new[] { BackendKind.GitHubCopilot }),
            (BackendKind.GitHubCopilot, BackendKind.GitHubCopilot, new[] { BackendKind.GitHubCopilot }),
            (BackendKind.CodexAppServer, BackendKind.GitHubCopilot, new[] { BackendKind.CodexAppServer, BackendKind.GitHubCopilot }),
        })
        {
            var created = new List<BackendKind>();
            using var service = new SubscriptionService((backend, _) => { created.Add(backend); return new FakeBackend(); });
            await service.InitializeAsync(new AppSettings { AutoBackend = auto, ManualBackend = manual });
            if (!created.SequenceEqual(expected)) return false;
        }
        return true;
    }

    private static bool TestIndependentBackends()
    {
        var previous = SynchronizationContext.Current;
        var ui = new UiContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var codex = new FakeBackend();
            var copilot = new FakeBackend();
            using var service = new SubscriptionService((backend, _) => backend == BackendKind.CodexAppServer ? codex : copilot);
            var initial = service.RefreshAsync(BackendKind.CodexAppServer, "mock");
            ui.PumpUntil(() => initial.IsCompleted);
            initial.GetAwaiter().GetResult();
            codex.Block = true;
            var busy = service.RefreshAsync(BackendKind.CodexAppServer, "mock");
            using var cancellation = new CancellationTokenSource();
            var queued = service.RefreshAsync(BackendKind.CodexAppServer, "mock", cancellation.Token);
            var independent = service.RefreshAsync(BackendKind.GitHubCopilot, "mock");
            ui.PumpUntil(() => independent.IsCompleted);
            independent.GetAwaiter().GetResult();
            bool isolated = !busy.IsCompleted && !queued.IsCompleted && copilot.Reads == 1 &&
                !service.Suspended(BackendKind.GitHubCopilot);
            cancellation.Cancel();
            ui.PumpUntil(() => queued.IsCompleted);
            try { queued.GetAwaiter().GetResult(); return false; }
            catch (OperationCanceledException) { }
            bool preserved = service.State(BackendKind.CodexAppServer) == codex.State &&
                !service.Suspended(BackendKind.CodexAppServer);
            codex.Release.TrySetResult();
            ui.PumpUntil(() => busy.IsCompleted);
            busy.GetAwaiter().GetResult();
            return isolated && preserved && codex.Reads == 2;
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static bool TestSignedInStartup()
    {
        var previous = SynchronizationContext.Current;
        var ui = new UiContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var codex = new FakeBackend { Block = true };
            var copilot = new FakeBackend();
            var paths = new List<(BackendKind Backend, string Path)>();
            bool onUi = true, sawCodexLoading = false, sawCopilotLoading = false, sawPublished = false;
            using var service = new SubscriptionService((backend, path) =>
            {
                onUi &= SynchronizationContext.Current == ui;
                paths.Add((backend, path));
                return backend == BackendKind.CodexAppServer ? codex : copilot;
            });
            service.StateChanged += () =>
            {
                onUi &= SynchronizationContext.Current == ui;
                sawCodexLoading |= service.IsInitializing(BackendKind.CodexAppServer);
                sawCopilotLoading |= service.IsInitializing(BackendKind.GitHubCopilot);
                sawPublished |= service.State(BackendKind.CodexAppServer)?.Models.Count == 1 &&
                    service.State(BackendKind.GitHubCopilot)?.Models.Count == 1;
            };
            var startup = service.InitializeAsync("saved-codex-path", "saved-copilot-path", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]);
            bool yielded = !startup.IsCompleted && paths.Count == 0;
            ui.PumpUntil(() => codex.Started.Task.IsCompleted);
            bool serialized = codex.Reads == 1 && copilot.Reads == 0;
            bool responded = false;
            ui.Post(_ => responded = true, null);
            ui.PumpUntil(() => responded);
            bool pendingWhileResponsive = !startup.IsCompleted;
            codex.Release.TrySetResult();
            ui.PumpUntil(() => startup.IsCompleted);
            startup.GetAwaiter().GetResult();
            var repeated = service.InitializeAsync("saved-codex-path", "saved-copilot-path", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]);
            ui.PumpUntil(() => repeated.IsCompleted);
            repeated.GetAwaiter().GetResult();
            return yielded && serialized && responded && pendingWhileResponsive && onUi &&
                sawCodexLoading && sawCopilotLoading && sawPublished &&
                paths.SequenceEqual([(BackendKind.CodexAppServer, "saved-codex-path"), (BackendKind.GitHubCopilot, "saved-copilot-path")]) &&
                service.State(BackendKind.CodexAppServer) == codex.State && service.State(BackendKind.GitHubCopilot) == copilot.State &&
                !service.IsInitializing(BackendKind.CodexAppServer) && !service.IsInitializing(BackendKind.GitHubCopilot) &&
                service.InitializationError(BackendKind.CodexAppServer) is null && service.InitializationError(BackendKind.GitHubCopilot) is null &&
                codex.Reads == 1 && copilot.Reads == 1 && codex.Logins + copilot.Logins + codex.Sends + copilot.Sends == 0;
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task<bool> TestSignedOutAsync()
    {
        var codex = new FakeBackend(false);
        var copilot = new FakeBackend(false);
        using var service = new SubscriptionService((backend, _) => backend == BackendKind.CodexAppServer ? codex : copilot);
        await service.InitializeAsync("mock", "mock", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]).WaitAsync(TimeSpan.FromSeconds(3));
        return service.State(BackendKind.CodexAppServer)?.Authenticated == false &&
            service.State(BackendKind.GitHubCopilot)?.Authenticated == false &&
            codex.Reads == 1 && copilot.Reads == 1 && codex.Logins + copilot.Logins + codex.Sends + copilot.Sends == 0;
    }

    private static async Task<bool> TestFailuresAsync()
    {
        foreach (var failure in new Exception[] { new FileNotFoundException("Mock CLI is missing."),
            new GeminiClientException(GeminiClientError.AuthenticationRequired, "Mock authentication expired.") })
        {
            var codex = new FakeBackend { Failure = failure };
            var copilot = new FakeBackend();
            using var service = new SubscriptionService((backend, _) => backend == BackendKind.CodexAppServer ? codex : copilot);
            await service.InitializeAsync("mock", "mock", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]).WaitAsync(TimeSpan.FromSeconds(3));
            if (service.State(BackendKind.CodexAppServer) is not null ||
                string.IsNullOrWhiteSpace(service.InitializationError(BackendKind.CodexAppServer)) ||
                service.State(BackendKind.GitHubCopilot)?.Authenticated != true ||
                service.InitializationError(BackendKind.GitHubCopilot) is not null ||
                service.IsInitializing(BackendKind.CodexAppServer) || service.IsInitializing(BackendKind.GitHubCopilot) ||
                codex.Reads != 1 || copilot.Reads != 1 || codex.Disposals != 1 ||
                codex.Logins + copilot.Logins + codex.Sends + copilot.Sends != 0)
                return false;
        }
        return true;
    }

    private static bool TestDisposedBeforeRead()
    {
        var previous = SynchronizationContext.Current;
        var ui = new UiContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            int creations = 0;
            using var service = new SubscriptionService((_, _) => { creations++; return new FakeBackend(); });
            var startup = service.InitializeAsync("mock", "mock", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]);
            service.Dispose();
            ui.PumpUntil(() => startup.IsCompleted);
            startup.GetAwaiter().GetResult();
            var repeated = service.InitializeAsync("mock", "mock", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]);
            ui.PumpUntil(() => repeated.IsCompleted);
            repeated.GetAwaiter().GetResult();
            return creations == 0 && !service.IsInitializing(BackendKind.CodexAppServer) &&
                !service.IsInitializing(BackendKind.GitHubCopilot);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task<bool> TestCancellationAsync()
    {
        var codex = new FakeBackend { Block = true };
        var copilot = new FakeBackend();
        int creations = 0;
        using var service = new SubscriptionService((backend, _) =>
        { creations++; return backend == BackendKind.CodexAppServer ? codex : copilot; });
        var startup = service.InitializeAsync("mock", "mock", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]);
        await codex.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.Dispose();
        await startup.WaitAsync(TimeSpan.FromSeconds(3));
        return codex.Cancelled && creations == 1 && copilot.Reads == 0 &&
            service.State(BackendKind.CodexAppServer) is null && service.State(BackendKind.GitHubCopilot) is null &&
            !service.IsInitializing(BackendKind.CodexAppServer) && !service.IsInitializing(BackendKind.GitHubCopilot) &&
            codex.Logins + copilot.Logins + codex.Sends + copilot.Sends == 0;
    }

    private static bool TestCleanupAfterUiStops()
    {
        var previous = SynchronizationContext.Current;
        var ui = new UiContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        try
        {
            var codex = new FakeBackend { Block = true };
            var copilot = new FakeBackend();
            using var service = new SubscriptionService((backend, _) =>
                backend == BackendKind.CodexAppServer ? codex : copilot);
            var startup = service.InitializeAsync("mock", "mock", [BackendKind.CodexAppServer, BackendKind.GitHubCopilot]);
            ui.PumpUntil(() => codex.Started.Task.IsCompleted);
            service.Dispose();
            // Deliberately stop pumping the UI, as happens when its dispatcher shuts down.
            // A backend read that captured this context cannot execute its cleanup here.
            bool cleanedWithoutUi = codex.ReadFinished.Task.Wait(TimeSpan.FromSeconds(3)) &&
                codex.Cancelled && codex.ReadResourceReleased;
            // Drain even on failure so a regression does not strand test continuations.
            ui.PumpUntil(() => startup.IsCompleted);
            startup.GetAwaiter().GetResult();
            return cleanedWithoutUi && copilot.Reads == 0 &&
                service.State(BackendKind.CodexAppServer) is null &&
                !service.IsInitializing(BackendKind.CodexAppServer) && !service.IsInitializing(BackendKind.GitHubCopilot);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
