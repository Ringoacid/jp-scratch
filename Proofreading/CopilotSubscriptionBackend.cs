using System.Diagnostics;
using System.Text.Json;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace JpScratch.Proofreading;

#pragma warning disable GHCP001 // Permission RPC and metrics belong to the pinned SDK/CLI contract.

internal sealed class CopilotSubscriptionBackend(SubscriptionRuntime runtime) : ISubscriptionBackend
{
    private CopilotClient? _client;
    private async Task<CopilotClient> ConnectAsync(CancellationToken token)
    {
        if (_client is not null) return _client;
        await runtime.CheckVersionAsync(token);
        var connection = RuntimeConnection.ForStdio(runtime.Executable);
        connection.Environment = runtime.EnvironmentVariables();
        var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty, Connection = connection,
            BaseDirectory = runtime.Home, WorkingDirectory = runtime.WorkingDirectory,
            UseLoggedInUser = true,
        });
        try { await client.StartAsync(token); _client = client; return client; }
        catch { await client.DisposeAsync(); throw; }
    }

    public async Task<SubscriptionState> ReadStateAsync(CancellationToken token)
    {
        var client = await ConnectAsync(token);
        var auth = await client.GetAuthStatusAsync(token);
        bool authenticated = auth.IsAuthenticated && auth.AuthType == "user";
        if (auth.IsAuthenticated && !authenticated)
            throw new GeminiClientException(GeminiClientError.AuthenticationRequired,
                "このアプリでのログインが完了していません。「ブラウザーでログイン」から保存方法を確認し、認証を完了してください。別のCLIや環境変数の認証情報は使用しません。");
        if (!authenticated) return new(false, null, null, null, null, DateTimeOffset.Now, []);
        var models = await client.ListModelsAsync(token);
        double? used = null;
        DateTimeOffset? reset = null;
        var checkedAt = DateTimeOffset.Now;
        try
        {
            var quota = await client.Rpc.Account.GetQuotaAsync(cancellationToken: token);
            checkedAt = DateTimeOffset.Now;
            (used, reset) = CopilotQuota.Read(JsonSerializer.SerializeToElement(quota), checkedAt);
        }
        catch (Exception) when (!token.IsCancellationRequested) { }
        return new(true, auth.Login, "Copilot", used, reset, checkedAt,
            models.Select(m =>
            {
                var billing = JsonSerializer.SerializeToElement(m).Get("billing");
                return new SubscriptionModel(m.Id, m.Name, billing.Number("multiplier"),
                    billing.Get("tokenPrices").Number("inputPrice"), billing.Get("tokenPrices").Number("outputPrice"));
            }).ToArray());
    }

    public async Task LoginAsync(bool deviceCode, Action<string> showCode, CancellationToken token,
        Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null)
    {
        showCode("Copilotの接続を準備しています。既存の接続がある場合は終了を待ちます…");
        await DisposeAsync();
        token.ThrowIfCancellationRequested();
        await runtime.CheckVersionAsync(token);
        await CopilotLoginSettings.PrepareAsync(runtime.Home, confirmCredentialStorage, token);
        showCode(deviceCode ? "ログイン用コードを取得しています…" : "ブラウザー認証を開始しています…");
        await runtime.RunAsync(["login", deviceCode ? "--device-code" : "--web-flow"], showCode, token,
            confirmCredentialStorage);
        showCode("CLIのログイン処理が終了しました。保存されたログイン情報で接続を確認しています…");
    }

    public Task LogoutAsync(CancellationToken token)
        => throw new NotSupportedException("Copilotの資格情報削除は公式CLIの /logout を使用してください。接続解除はこのアプリの利用を停止します。");

    public async Task<GeminiRawTextResult> GenerateAsync(string model, string instructions, string prompt, CancellationToken token)
    {
        var client = await ConnectAsync(token);
        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = model, WorkingDirectory = runtime.WorkingDirectory,
            AvailableTools = [], EnableConfigDiscovery = false, EnableSkills = false,
            EnableFileHooks = false, EnableHostGitOperations = false, EnableSessionStore = false,
            SkipCustomInstructions = true, EnableOnDemandInstructionDiscovery = false,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = instructions },
            OnPermissionRequest = (_, _) => Task.FromResult(PermissionDecision.Reject("校正ではツールを使用しません。")),
        }, token);
        bool failed = false;
        using var errorSubscription = session.On<SessionErrorEvent>(_ => failed = true);
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, Timeout.InfiniteTimeSpan, token);
            if (failed || string.IsNullOrWhiteSpace(response?.Data.Content))
                throw new GeminiClientException(GeminiClientError.InvalidResponse, "Copilotから完了した校正結果を取得できませんでした。");
            GeminiUsage usage = GeminiUsage.Unknown;
            try
            {
#pragma warning disable GHCP001 // Optional usage metrics; SDK and CLI are pinned together.
                var metrics = await session.Rpc.Usage.GetMetricsAsync(token);
#pragma warning restore GHCP001
                var json = JsonSerializer.SerializeToElement(metrics);
                if (json.Number("lastCallInputTokens") is { } input && json.Number("lastCallOutputTokens") is { } output)
                    usage = new((int)input, (int)output, 0, 0, (int)(input + output)) { SubscriptionUnits = json.Number("totalNanoAiu"), SubscriptionUnit = "nano-AI unit" };
            }
            catch (Exception) when (!token.IsCancellationRequested) { }
            return new(response.Data.Content, usage, watch.Elapsed, 1);
        }
        catch
        {
            try { await session.AbortAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); } catch { await DisposeAsync(); }
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        var client = Interlocked.Exchange(ref _client, null);
        // The pinned SDK captures SynchronizationContext in its shutdown awaits.
        // Start it on a worker even for the synchronous application-exit adapter below.
        return client is null ? ValueTask.CompletedTask : new(Task.Run(async () => await client.DisposeAsync()));
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
