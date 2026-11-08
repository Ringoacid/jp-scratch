using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace JpScratch.Proofreading;

internal sealed class CodexSubscriptionBackend(SubscriptionRuntime runtime) : ISubscriptionBackend
{
    private CodexRpcConnection? _rpc;
    private Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();
    private readonly SemaphoreSlim _start = new(1, 1);

    private async Task<CodexRpcConnection> ConnectAsync(CancellationToken token)
    {
        await _start.WaitAsync(token);
        try
        {
            if (_rpc is not null) return _rpc;
            await runtime.CheckVersionAsync(token);
            List<string> args = ["app-server", "--stdio"];
            foreach (var pair in RestrictedConfig()) { args.Add("-c"); args.Add(pair.Key + "=" + JsonSerializer.Serialize(pair.Value)); }
            var rpc = new CodexRpcConnection(runtime.StartInfo(args));
            var events = Channel.CreateUnbounded<JsonElement>();
            _events = events;
            rpc.Notification += message => events.Writer.TryWrite(message);
            rpc.Disconnected += _ => events.Writer.TryWrite(JsonSerializer.SerializeToElement(new { method = "connection/closed" }));
            try
            {
                await rpc.CallAsync("initialize", new { clientInfo = new { name = "jp_scratch", title = "JP Scratch", version = "1.1.0" }, capabilities = new { experimentalApi = true } }, token);
                await rpc.NotifyAsync("initialized", new { }, token);
                await VerifyMcpDisabledAsync(rpc, null, token);
                _rpc = rpc;
                return rpc;
            }
            catch { rpc.Dispose(); throw; }
        }
        finally { _start.Release(); }
    }

    internal static Dictionary<string, object> RestrictedConfig()
    {
        var config = new Dictionary<string, object>
        {
            ["web_search"] = "disabled", ["forced_login_method"] = "chatgpt",
            ["project_doc_max_bytes"] = 0, ["cli_auth_credentials_store"] = "keyring",
            ["features.skip_host_skill_discovery"] = true,
            ["tools.update_plan.enabled"] = false, ["tools.experimental_request_user_input.enabled"] = false,
            ["include_environment_context"] = false,
        };
        foreach (string flag in new[] { "shell_tool", "unified_exec", "view_image", "sleep_tool",
            "multi_agent", "multi_agent_v2", "apps", "plugins", "hooks", "codex_hooks", "image_generation",
            "browser_use", "browser_use_external", "browser_use_full_cdp_access", "in_app_browser", "computer_use",
            "code_mode", "code_mode_host", "shell_snapshot", "remote_plugin", "skill_search", "skill_mcp_dependency_install",
            "tool_suggest", "recommended_plugins", "workspace_dependencies", "goals", "deferred_executor",
            "request_permissions_tool", "standalone_web_search", "memories", "token_budget" })
            config["features." + flag] = false;
        return config;
    }

    private static async Task VerifyMcpDisabledAsync(CodexRpcConnection rpc, string? threadId, CancellationToken token)
    {
        JsonElement inventory = await rpc.CallAsync("mcpServerStatus/list", new { threadId, limit = 1, detail = "toolsAndAuthOnly" }, token);
        if (inventory.Get("data").ValueKind != JsonValueKind.Array || inventory.Get("data").GetArrayLength() != 0 || inventory.Text("nextCursor") is not null)
            throw new IOException("校正用ランタイムにMCPが構成されているため利用できません。専用ホームのCLI設定を確認してください。");
    }

    public async Task<SubscriptionState> ReadStateAsync(CancellationToken token)
    {
        var rpc = await ConnectAsync(token);
        JsonElement account = (await rpc.CallAsync("account/read", new { refreshToken = false }, token)).Get("account");
        bool authenticated = account.Text("type") == "chatgpt";
        if (!authenticated) return new(false, null, null, null, null, DateTimeOffset.Now, []);
        List<SubscriptionModel> models = [];
        string? cursor = null;
        do
        {
            JsonElement page = await rpc.CallAsync("model/list", new { cursor, limit = 100 }, token);
            foreach (var model in page.Get("data").Items())
                if (model.Text("model") is { } id) models.Add(new(id, model.Text("displayName") ?? id));
            cursor = page.Text("nextCursor");
        } while (cursor is not null);
        double? used = null;
        DateTimeOffset? reset = null;
        try
        {
            JsonElement limits = await rpc.CallAsync("account/rateLimits/read", new { }, token);
            var buckets = limits.Get("rateLimitsByLimitId");
            IEnumerable<JsonElement> values = buckets.ValueKind == JsonValueKind.Object
                ? buckets.EnumerateObject().Select(p => p.Value).ToArray() : [limits.Get("rateLimits")];
            foreach (var bucket in values)
                foreach (string window in new[] { "primary", "secondary" })
                {
                    JsonElement value = bucket.Get(window);
                    if (value.Number("usedPercent") is { } percent && (used is null || percent > used))
                    {
                        used = percent;
                        reset = value.Number("resetsAt") is { } seconds ? DateTimeOffset.FromUnixTimeSeconds((long)seconds) : null;
                    }
                }
        }
        catch (Exception) when (!token.IsCancellationRequested) { /* Unknown quota is not zero. */ }
        return new(true, account.Text("email"), account.Text("planType"), used, reset, DateTimeOffset.Now, models);
    }

    public async Task LoginAsync(bool deviceCode, Action<string> showCode, CancellationToken token,
        Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null)
    {
        var rpc = await ConnectAsync(token);
        while (_events.Reader.TryRead(out _)) { }
        JsonElement result = await rpc.CallAsync("account/login/start", new { type = deviceCode ? "chatgptDeviceCode" : "chatgpt" }, token);
        string? loginId = result.Text("loginId");
        if (deviceCode) showCode($"{result.Text("verificationUrl")}\nコード: {result.Text("userCode")}");
        else if (result.Text("authUrl") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        try
        {
            await foreach (var message in _events.Reader.ReadAllAsync(token))
            {
                if (message.Text("method") == "connection/closed") throw new IOException("Codex接続が終了しました。");
                if (message.Text("method") == "account/login/completed" && message.Get("params").Text("loginId") == loginId)
                {
                    if (message.Get("params").Get("success").ValueKind != JsonValueKind.True) throw new IOException("ログインが完了しませんでした。");
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            try { await rpc.CallAsync("account/login/cancel", new { loginId }, CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task LogoutAsync(CancellationToken token)
        => await (await ConnectAsync(token)).CallAsync("account/logout", new { }, token);

    internal static object TurnParameters(string threadId, string model, string prompt) => new
    {
        threadId, model, input = new[] { new { type = "text", text = prompt } },
        environments = Array.Empty<object>(),
        // 0.153.4 explicitly rejects the removed readOnly.access field. Tool isolation is
        // enforced by the empty environments and RestrictedConfig, not by that old field.
        approvalPolicy = "never", sandboxPolicy = new { type = "readOnly", networkAccess = false },
    };

    public async Task<GeminiRawTextResult> GenerateAsync(string model, string instructions, string prompt, CancellationToken token)
    {
        var rpc = await ConnectAsync(token);
        while (_events.Reader.TryRead(out _)) { }
        string threadId = (await rpc.CallAsync("thread/start", new
        {
            model, cwd = runtime.WorkingDirectory, approvalPolicy = "never", sandbox = "read-only",
            ephemeral = true, baseInstructions = instructions, config = RestrictedConfig(),
            // Empty environments removes shell/file tools entirely in the pinned experimental protocol.
            // Do not omit this property: omission enables the default host environment.
            environments = Array.Empty<object>(), dynamicTools = Array.Empty<object>(),
        }, token)).Get("thread").Text("id") ?? throw new IOException("Codexスレッドを作成できませんでした。");
        string? turnId = null;
        var watch = Stopwatch.StartNew();
        string? final = null;
        GeminiUsage usage = GeminiUsage.Unknown;
        try
        {
            await VerifyMcpDisabledAsync(rpc, threadId, token);
            turnId = (await rpc.CallAsync("turn/start", TurnParameters(threadId, model, prompt), token))
                .Get("turn").Text("id") ?? throw new IOException("Codexターンを作成できませんでした。");
            await foreach (var message in _events.Reader.ReadAllAsync(token))
            {
                string? method = message.Text("method");
                if (method == "connection/closed") throw new IOException("Codex接続が終了しました。自動再送は行いません。");
                JsonElement p = message.Get("params");
                if (p.Text("threadId") != threadId) continue;
                if (method is "item/started" or "item/completed")
                {
                    var item = p.Get("item");
                    if (method == "item/completed" && item.Text("type") == "agentMessage" && item.Text("phase") != "commentary") final = item.Text("text");
                    else if (item.Text("type") is not ("agentMessage" or "userMessage" or "reasoning"))
                        throw new IOException("校正中に不要なツールが要求されたため中止しました。");
                }
                if (method == "thread/tokenUsage/updated")
                {
                    var u = p.Get("tokenUsage").Get("total");
                    if (u.Number("inputTokens") is { } input && u.Number("outputTokens") is { } output)
                        usage = new((int)input, (int)output, 0, (int)(u.Number("cachedInputTokens") ?? 0), (int)(input + output));
                }
                if (method == "turn/completed" && p.Get("turn").Text("id") == turnId)
                {
                    SubscriptionPolicy.RequireCompleted(p.Get("turn").Text("status"), final, usage);
                    return new(final!, usage, watch.Elapsed, 1);
                }
            }
            throw new IOException("Codex応答が完了しませんでした。");
        }
        catch
        {
            if (turnId is not null)
                try { await rpc.CallAsync("turn/interrupt", new { threadId, turnId }, CancellationToken.None); } catch { }
            // Ensure an interrupted/ambiguous turn cannot keep running or contaminate the next request.
            Dispose();
            throw;
        }
    }

    public void Dispose() { _rpc?.Dispose(); _rpc = null; }
}
