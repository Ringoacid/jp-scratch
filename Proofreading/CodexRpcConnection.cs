using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace JpScratch.Proofreading;

/// <summary>JSONL transport. Reader owns stdout; notifications never block response dispatch.</summary>
internal sealed class CodexRpcConnection : IDisposable
{
    private sealed class ProtocolError(int? code) : Exception
    {
        internal int? Code { get; } = code;
    }
    private readonly Process _process;
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private long _id;
    internal event Action<JsonElement>? Notification;
    internal event Action<Exception>? Disconnected;

    internal CodexRpcConnection(ProcessStartInfo startInfo)
    {
        _process = Process.Start(startInfo) ?? throw new IOException("Codexを起動できませんでした。");
        _ = ReadAsync();
        _ = DrainErrorsAsync();
    }

    internal async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken token)
    {
        long id = Interlocked.Increment(ref _id);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, timeout.Token);
            return await completion.Task.WaitAsync(timeout.Token);
        }
        catch (ProtocolError error)
        {
            string guidance = error.Code is -32600 or -32601 or -32602
                ? $"アプリとCLIの通信仕様を確認してください（対応CLI {SubscriptionCliInstaller.CodexVersion}）。再ログインだけでは解消しない場合があります。"
                : "接続設定・認証・利用枠を確認してください。";
            // Never echo server error.message/data: they can contain input text or credentials.
            throw new GeminiClientException(GeminiClientError.BackendUnavailable,
                $"Codex の {method} が拒否されました（RPC {error.Code?.ToString() ?? "不明"}）。\n{guidance}");
        }
        finally { _pending.TryRemove(id, out _); }
    }

    internal Task NotifyAsync(string method, object? parameters, CancellationToken token)
        => WriteAsync(new { method, @params = parameters }, token);

    private async Task WriteAsync(object message, CancellationToken token)
    {
        await _write.WaitAsync(token);
        try { await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), token); await _process.StandardInput.FlushAsync(token); }
        finally { _write.Release(); }
    }

    private async Task ReadAsync()
    {
        Exception error = new IOException("Codex接続が終了しました。送信済みの要求は自動再送しません。");
        try
        {
            while (await _process.StandardOutput.ReadLineAsync(_lifetime.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement.Clone();
                if (root.Get("id").ValueKind == JsonValueKind.Number && root.Get("method").ValueKind == JsonValueKind.Undefined &&
                    _pending.TryRemove(root.Get("id").GetInt64(), out var waiter))
                {
                    if (root.Get("error").ValueKind != JsonValueKind.Undefined)
                    {
                        var code = root.Get("error").Get("code");
                        waiter.TrySetException(new ProtocolError(code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int value) ? value : null));
                    }
                    else waiter.TrySetResult(root.Get("result"));
                }
                else if (root.Get("id").ValueKind != JsonValueKind.Undefined && root.Text("method") is not null)
                {
                    // This app never grants agent tool/permission requests.
                    await WriteAsync(new { id = root.Get("id"), error = new { code = -32601, message = "Tools are disabled in proofreading." } }, _lifetime.Token);
                }
                else Notification?.Invoke(root);
            }
        }
        catch (Exception ex) { error = ex; }
        finally
        {
            _lifetime.Cancel();
            foreach (var waiter in _pending.Values) waiter.TrySetException(error);
            _pending.Clear();
            Disconnected?.Invoke(error);
        }
    }

    private async Task DrainErrorsAsync()
    {
        try { while (await _process.StandardError.ReadLineAsync(_lifetime.Token) is not null) { } }
        catch (Exception) when (_lifetime.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        SubscriptionRuntime.Stop(_process);
        _process.Dispose();
    }
}
