using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GitHub.Copilot;
using JpScratch.Models;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class CopilotLifecycleValidation
{
    private sealed class UiContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));
        internal void Pump() { if (_queue.TryTake(out var work, 10)) work.Callback(work.State); }
    }

    internal static async Task<bool> RunAsync()
    {
        // Use the real pinned SDK with an entirely local fake CLI. No credentials or model calls.
        string root = Path.Combine(Path.GetTempPath(), "jpscratch-copilot-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var args = new List<string>();
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") args.Add(typeof(CopilotLifecycleValidation).Assembly.Location);
        args.Add("--mock-copilot-lifecycle");
        var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.Empty,
            Connection = RuntimeConnection.ForStdio(Environment.ProcessPath!, args),
            BaseDirectory = root, WorkingDirectory = root, UseLoggedInUser = false,
        });
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await client.StartAsync(limit.Token);
            using var backend = new CopilotSubscriptionBackend(new SubscriptionRuntime(BackendKind.GitHubCopilot, Environment.ProcessPath!));
            typeof(CopilotSubscriptionBackend).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(backend, client);
            var ui = new UiContext();
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int ticks = 0;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(ui);
                // Cancel while the SDK waits for runtime.shutdown. Login must never get as
                // far as starting a real browser; the UI must keep pumping during cleanup.
                using var cancel = new CancellationTokenSource(100);
                try
                {
                    var login = backend.LoginAsync(false, _ => { }, cancel.Token);
                    while (!login.IsCompleted) { ui.Pump(); Interlocked.Increment(ref ticks); }
                    login.GetAwaiter().GetResult(); finished.TrySetResult(false);
                }
                catch (OperationCanceledException) { finished.TrySetResult(ticks > 0); }
                catch (Exception) { finished.TrySetResult(false); }
            }) { IsBackground = true };
            thread.Start();
            bool responded = await Task.WhenAny(finished.Task, Task.Delay(3000)) == finished.Task;
            // If a regression blocks the UI, drain its queued SDK continuations from the
            // test runner to release it and allow the fake child process to be reaped.
            if (!responded)
            {
                var recovery = Stopwatch.StartNew();
                while (!finished.Task.IsCompleted && recovery.Elapsed < TimeSpan.FromSeconds(3)) ui.Pump();
            }
            return responded && await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await client.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task<int> RunMockAsync()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        while (true)
        {
            var header = new StringBuilder();
            byte[] one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await input.ReadAsync(one) == 0) return 0;
                header.Append((char)one[0]);
                if (header.Length > 1024) return 1;
            }
            int length = int.Parse(header.ToString().Split("\r\n")[0].Split(':')[1]);
            if (length > 1_000_000) return 1;
            byte[] body = new byte[length]; await input.ReadExactlyAsync(body);
            using var document = JsonDocument.Parse(body);
            var message = document.RootElement;
            if (message.Get("id").ValueKind == JsonValueKind.Undefined) continue;
            string? method = message.Text("method");
            if (method == "runtime.shutdown") await Task.Delay(350);
            object result = method == "connect" ? new { protocolVersion = 3 } : new { };
            byte[] response = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = message.Get("id"), result });
            await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {response.Length}\r\n\r\n"));
            await output.WriteAsync(response); await output.FlushAsync();
        }
    }
}
