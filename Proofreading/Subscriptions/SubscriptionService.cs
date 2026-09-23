using JpScratch.Models;

namespace JpScratch.Proofreading;

/// <summary>
/// One serialized operation per backend. State reads never cause generation or login.
/// All entry points, state access, events and Dispose must run on the owning UI context.
/// Backend I/O alone runs off-context; callers must marshal here before accessing shared state.
/// </summary>
internal sealed class SubscriptionService : IDisposable
{
    private readonly Func<BackendKind, string, ISubscriptionBackend>? _factory;
    internal SubscriptionService(Func<BackendKind, string, ISubscriptionBackend>? factory = null) => _factory = factory;
    private readonly Dictionary<BackendKind, ISubscriptionBackend> _backends = [];
    private readonly Dictionary<BackendKind, SubscriptionState> _states = [];
    private readonly Dictionary<BackendKind, string> _paths = [];
    private readonly HashSet<BackendKind> _suspended = [];
    private readonly Dictionary<BackendKind, SemaphoreSlim> _gates = new()
    {
        [BackendKind.CodexAppServer] = new(1, 1),
        [BackendKind.GitHubCopilot] = new(1, 1),
    };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<BackendKind> _initializing = [];
    private readonly Dictionary<BackendKind, string> _initializationErrors = [];
    private Task? _initialization;
    private DateTimeOffset? _lastSent;
    internal event Action? StateChanged;

    internal SubscriptionState? State(BackendKind backend) => _states.GetValueOrDefault(backend);
    internal bool IsInitializing(BackendKind backend) => _initializing.Contains(backend);
    internal string? InitializationError(BackendKind backend) => _initializationErrors.GetValueOrDefault(backend);
    internal bool Suspended(BackendKind backend) => _suspended.Contains(backend);
    internal void Suspend(BackendKind backend) => _suspended.Add(backend);
    internal TimeSpan DelayBeforeSend(int configuredSeconds) => _lastSent is { } last
        ? SubscriptionPolicy.Interval(configuredSeconds) - (DateTimeOffset.UtcNow - last) : TimeSpan.Zero;

    internal Task InitializeAsync(AppSettings settings)
        => InitializeAsync(settings.CodexCliPath, settings.CopilotCliPath,
            new[] { settings.AutoBackend, settings.ManualBackend });

    internal Task InitializeAsync(string codexPath, string copilotPath, IReadOnlyCollection<BackendKind> selectedBackends)
        => _initialization ??= InitializeCoreAsync(codexPath, copilotPath, selectedBackends);

    private async Task InitializeCoreAsync(string codexPath, string copilotPath, IReadOnlyCollection<BackendKind> selectedBackends)
    {
        var connections = new[] { (BackendKind.CodexAppServer, codexPath), (BackendKind.GitHubCopilot, copilotPath) }
            .Where(connection => selectedBackends.Contains(connection.Item1)).ToArray();
        _initializing.UnionWith(connections.Select(connection => connection.Item1));
        // Let the initial window render; keep state mutations on the caller's UI context.
        await Task.Yield();
        try
        {
            StateChanged?.Invoke();
            foreach (var (backend, path) in connections)
            {
                if (_lifetime.IsCancellationRequested) break;
                try { await RefreshAsync(backend, path); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // An absent CLI or expired login must not prevent the other service or app from starting.
                    _initializationErrors[backend] = ex is OperationCanceledException
                        ? "接続確認がタイムアウトしました。「接続を確認」で再試行できます。" : ex.Message;
                }
                finally
                {
                    _initializing.Remove(backend);
                    StateChanged?.Invoke();
                }
            }
        }
        finally { _initializing.Clear(); StateChanged?.Invoke(); }
    }

    private async Task<ISubscriptionBackend> GetAsync(BackendKind backend, string path)
    {
        if (_backends.TryGetValue(backend, out var existing) && _paths.GetValueOrDefault(backend) == path) return existing;
        if (existing is not null)
        {
            _backends.Remove(backend);
            await existing.DisposeAsync();
        }
        ISubscriptionBackend created = _factory?.Invoke(backend, path) ?? CreateBackend(backend, path);
        _backends[backend] = created; _paths[backend] = path;
        _states.Remove(backend);
        return created;
    }

    private static ISubscriptionBackend CreateBackend(BackendKind backend, string path)
    {
        var runtime = new SubscriptionRuntime(backend, path);
        return backend switch
        {
            BackendKind.CodexAppServer => new CodexSubscriptionBackend(runtime),
            BackendKind.GitHubCopilot => new CopilotSubscriptionBackend(runtime),
            _ => throw new InvalidOperationException("未対応の接続方式です。"),
        };
    }

    internal async Task<SubscriptionState> RefreshAsync(BackendKind backend, string path, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var gate = _gates[backend];
        await gate.WaitAsync(timeout.Token);
        // Queueing is not a connection failure. Start the I/O timeout only after acquiring this backend.
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        bool cleanedUp = false;
        try
        {
            var client = await GetAsync(backend, path);
            // Handshake/cleanup must also finish when shutdown stops the UI dispatcher.
            // Only backend I/O runs here; publish shared state back on the caller's context.
            var state = await Task.Run(async () =>
            {
                try
                {
                    var snapshot = await client.ReadStateAsync(timeout.Token).ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested();
                    return snapshot;
                }
                catch
                {
                    // Cleanup must finish even after the UI dispatcher stops. Awaiting this
                    // task publishes the flag before the caller removes the failed backend.
                    cleanedUp = true;
                    await client.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            });
            _states[backend] = state;
            _initializationErrors.Remove(backend);
            if (state.Authenticated && !state.Exhausted && state.UsedPercent is not null) _suspended.Remove(backend);
            StateChanged?.Invoke(); return state;
        }
        catch
        {
            _states.Remove(backend); _suspended.Add(backend);
            if (_backends.Remove(backend, out var failed) && !cleanedUp) await failed.DisposeAsync();
            throw;
        }
        finally { gate.Release(); }
    }

    internal async Task LoginAsync(BackendKind backend, string path, bool deviceCode, Action<string> showCode, CancellationToken token,
        Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var gate = _gates[backend];
        await gate.WaitAsync(linked.Token);
        linked.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            var client = await GetAsync(backend, path);
            _states.Remove(backend); _suspended.Add(backend);
            await client.LoginAsync(deviceCode, showCode, linked.Token, confirmCredentialStorage);
        }
        finally { gate.Release(); }
        var state = await RefreshAsync(backend, path, linked.Token);
        if (!state.Authenticated)
            throw new GeminiClientException(GeminiClientError.AuthenticationRequired,
                "ログイン情報を確認できませんでした。ブラウザーの「Authorization received」だけでは完了しません。もう一度ログインし、保存方法の確認とブラウザー認証を完了してください。");
    }

    internal async Task DisconnectAsync(BackendKind backend, bool logout, CancellationToken token, string? path = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var gate = _gates[backend];
        await gate.WaitAsync(linked.Token);
        try
        {
            if (logout && path is not null) await GetAsync(backend, path);
            if (_backends.Remove(backend, out var client))
            {
                try { if (logout) await client.LogoutAsync(token); }
                finally { await client.DisposeAsync(); }
            }
        }
        finally { _states.Remove(backend); _initializationErrors.Remove(backend); _suspended.Add(backend); StateChanged?.Invoke(); gate.Release(); }
    }

    internal async Task<GeminiRawTextResult> SendAsync(BackendKind backend, string path, string model,
        string? account, bool allowUnknownQuota, TimeSpan timeout, int minimumInterval,
        string instructions, string prompt, CancellationToken token, Func<bool>? isCurrent = null)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var gate = _gates[backend];
        await gate.WaitAsync(lifetime.Token);
        try
        {
            var delay = DelayBeforeSend(minimumInterval);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, lifetime.Token);
            var client = await GetAsync(backend, path);
            var state = State(backend);
            if (state is null || !SubscriptionPolicy.IsFresh(state.CheckedAt, DateTimeOffset.Now))
            {
                using var stateTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                stateTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                _states[backend] = state = await client.ReadStateAsync(stateTimeout.Token);
            }
            if (!state.Authenticated || account != state.Account)
                throw new GeminiClientException(GeminiClientError.AuthenticationRequired, "ログイン状態が変わりました。接続を確認して再実行してください。");
            if (state.Exhausted) throw new GeminiClientException(GeminiClientError.QuotaExhausted, "利用枠に達したため送信を停止しました。");
            if (state.UsedPercent is null && !allowUnknownQuota)
                throw new GeminiClientException(GeminiClientError.QuotaUnavailable, "残量を確認できないため自動送信を停止しました。");
            if (!state.Models.Any(m => m.Id == model)) throw new GeminiClientException(GeminiClientError.BackendUnavailable, "選択モデルは現在利用できません。設定でモデル一覧を更新してください。");
            if (isCurrent?.Invoke() == false) throw new SubscriptionRequestStaleException();
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            execution.CancelAfter(timeout);
            _lastSent = DateTimeOffset.UtcNow;
            var result = await client.GenerateAsync(model, instructions, prompt, execution.Token);
            // Expire the snapshot after every completed request; next send refreshes account quota.
            _states[backend] = state with { CheckedAt = DateTimeOffset.MinValue };
            return result with { Usage = result.Usage with { Backend = backend } };
        }
        catch (SubscriptionRequestStaleException) { throw; }
        catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
        { _suspended.Add(backend); throw new GeminiClientException(GeminiClientError.Timeout, "応答がタイムアウトしました。自動再送は行いません。"); }
        catch (OperationCanceledException) { _suspended.Add(backend); throw; }
        catch (GeminiClientException ex)
        {
            _suspended.Add(backend);
            throw new GeminiClientException(ex.Error, ex.Message, ex.StatusCode, ex,
                ex.Usage is { } usage ? usage with { Backend = backend } : null, ex.Elapsed);
        }
        catch (Exception ex)
        { _suspended.Add(backend); throw new GeminiClientException(GeminiClientError.BackendUnavailable, "接続が失敗しました。設定画面で再接続してください。", innerException: ex); }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        foreach (var client in _backends.Values) client.Dispose();
        _backends.Clear();
    }
}
