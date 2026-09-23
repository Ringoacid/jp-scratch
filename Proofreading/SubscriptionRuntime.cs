using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using JpScratch.Infrastructure;
using JpScratch.Models;

namespace JpScratch.Proofreading;

internal sealed class SubscriptionRuntime
{
    internal string Executable { get; }
    internal string Home { get; }
    internal string WorkingDirectory { get; }
    internal BackendKind Backend { get; }
    internal static readonly string[] RemovedEnvironment = ["OPENAI_API_KEY", "OPENAI_BASE_URL", "ANTHROPIC_API_KEY",
        "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN", "GITHUB_COPILOT_API_TOKEN", "COPILOT_API_URL",
        "CODEX_HOME", "COPILOT_HOME", "CODEX_CONFIG", "COPILOT_SDK_TRANSPORT", "COPILOT_SDK_AUTH_TOKEN",
        "COPILOT_CONNECTION_TOKEN", "COPILOT_CLI_PATH", "COPILOT_DISABLE_KEYTAR", "GH_CONFIG_DIR",
        "COPILOT_OTEL_ENABLED", "COPILOT_OTEL_FILE_EXPORTER_PATH", "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"];

    internal SubscriptionRuntime(BackendKind backend, string configuredPath)
    {
        Backend = backend;
        Executable = ResolveExecutable(configuredPath, backend == BackendKind.CodexAppServer ? "codex.exe" : "copilot.exe");
        // Keep vendor-managed credentials and transcripts out of document backups; isolate test/data profiles too.
        string profile = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(AppPaths.Root))).Substring(0, 16);
        Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JpScratchRuntimes", profile, backend.ToString());
        WorkingDirectory = Path.Combine(Home, "work");
        Directory.CreateDirectory(WorkingDirectory);
    }

    internal static string ResolveExecutable(string configuredPath, string name)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string full = Path.GetFullPath(configuredPath.Trim());
            if (File.Exists(full) && Path.GetExtension(full).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return full;
            throw new InvalidOperationException("指定されたCLI実行ファイルが見つかりません。.exeを指定してください。");
        }
        if (SubscriptionCliInstaller.InstalledExecutable(name == "codex.exe" ? BackendKind.CodexAppServer : BackendKind.GitHubCopilot) is { } installed)
            return installed;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            string path = Path.Combine(directory.Trim('"'), name);
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new InvalidOperationException($"{name} が見つかりません。公式CLIをインストールし、設定で実行ファイルを指定してください。");
    }

    internal Dictionary<string, string> EnvironmentVariables()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())
            if (pair.Key is string key && pair.Value is string value && !RemovedEnvironment.Contains(key, StringComparer.OrdinalIgnoreCase)
                && !key.StartsWith("COPILOT_PROVIDER_", StringComparison.OrdinalIgnoreCase)) result[key] = value;
        result[Backend == BackendKind.CodexAppServer ? "CODEX_HOME" : "COPILOT_HOME"] = Home;
        // Match SDK Empty mode during CLI login too, so both use the same home-scoped credential store.
        if (Backend == BackendKind.GitHubCopilot)
        {
            result["COPILOT_DISABLE_KEYTAR"] = "1";
            // Do not silently fall back to a different account saved by the host's gh CLI.
            result["GH_CONFIG_DIR"] = Path.Combine(Home, "gh");
        }
        return result;
    }

    internal ProcessStartInfo StartInfo(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = WorkingDirectory };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        foreach (var pair in EnvironmentVariables()) info.Environment[pair.Key] = pair.Value;
        return info;
    }

    internal async Task<string> RunAsync(string[] args, Action<string>? output, CancellationToken token,
        Func<CancellationToken, Task<bool>>? confirmCredentialStorage = null)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var runToken = operation.Token;
        using var process = Process.Start(StartInfo(args)) ?? throw new IOException("CLIを起動できませんでした。");
        // Cancel is invoked by the UI button. Process termination/reaping must not run
        // inside CancellationTokenSource.Cancel() on that thread.
        using var registration = runToken.Register(() => _ = Task.Run(() => Stop(process)));
        var lines = new StringBuilder();
        int storagePromptSeen = 0;
        bool storageDeclined = false;
        async Task Drain(StreamReader reader)
        {
            var pending = new StringBuilder();
            char[] buffer = new char[1024];
            try
            {
                int count;
                // Interactive CLI prompts are often flushed without a trailing newline.
                // ReadLineAsync hid the storage question and left stdin unanswered.
                while ((count = await reader.ReadAsync(buffer.AsMemory(), runToken).ConfigureAwait(false)) > 0)
                {
                    string chunk = new(buffer, 0, count);
                    lock (lines)
                    {
                        lines.Append(chunk);
                        if (lines.Length > 16384) lines.Remove(0, lines.Length - 16384);
                    }
                    pending.Append(chunk);
                    if (pending.Length > 8192) pending.Remove(0, pending.Length - 8192);
                    string text = Regex.Replace(pending.ToString(), @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                    output?.Invoke(text.Trim());
                    if (Backend == BackendKind.GitHubCopilot && args.FirstOrDefault() == "login" &&
                        IsCredentialStoragePrompt(text) && Interlocked.CompareExchange(ref storagePromptSeen, 1, 0) == 0)
                    {
                        bool accepted = confirmCredentialStorage is not null &&
                            await confirmCredentialStorage(runToken).WaitAsync(runToken).ConfigureAwait(false);
                        runToken.ThrowIfCancellationRequested();
                        storageDeclined = !accepted;
                        await process.StandardInput.WriteLineAsync((accepted ? "y" : "n").AsMemory(), runToken).ConfigureAwait(false);
                        await process.StandardInput.FlushAsync(runToken).ConfigureAwait(false);
                        if (!accepted) process.StandardInput.Close();
                        pending.Clear();
                    }
                    else if (!IsCredentialStoragePrompt(text) && text.LastIndexOf('\n') is var end && end >= 0)
                        pending.Clear().Append(text[(end + 1)..]);
                }
            }
            catch { operation.Cancel(); throw; }
        }
        try
        {
            await Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError), process.WaitForExitAsync(runToken)).ConfigureAwait(false);
        }
        finally
        {
            // Reap the child before disposing Process, including cancellation/failure.
            await Task.Run(() => Stop(process)).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if (storageDeclined) throw new InvalidOperationException("ログイン情報を保存しなかったため、ログインを完了できませんでした。再度ログインすると保存方法を確認できます。");
        if (process.ExitCode != 0) throw new IOException(FailureMessage(process.ExitCode, lines.ToString()));
        return lines.ToString();
    }

    internal static string FailureMessage(int exitCode, string output)
    {
        // CLI stderr may contain OAuth callback URLs or credentials. Never attach it raw
        // to exceptions (which can also be written to the application's crash log).
        string detail = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        detail = Regex.Replace(detail, @"(?i)(?:gh[opusr]_|github_pat_)[a-z0-9_]+", "[認証情報を非表示]");
        detail = Regex.Replace(detail, @"(?i)https?://[^\s<>""']+", "[URLを非表示]");
        detail = Regex.Replace(detail, @"(?im)(?:access.?token|refresh.?token|authorization|client.?secret|password)[""']?\s*[:=].*$", "[認証情報を非表示]");
        detail = Regex.Replace(detail, @"(?i)Bearer\s+\S+", "[認証情報を非表示]");
        detail = new string(detail.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray()).Trim();
        if (detail.Length > 1600) detail = detail[^1600..];
        return $"CLIの処理が失敗しました（終了コード {exitCode}）。" +
            (detail.Length == 0 ? "CLIからエラーの詳細は返されませんでした。" : "\n" + detail);
    }

    internal static bool IsCredentialStoragePrompt(string text) => Regex.IsMatch(text,
        @"(?is)(?:store|save)[^\r\n]{0,160}(?:token|credentials)[^\r\n]{0,160}plain\s?text[^\r\n]{0,160}\?");

    internal async Task CheckVersionAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string output = await RunAsync(["--version"], null, timeout.Token);
        Match match = Regex.Match(output, @"\b(\d+\.\d+\.\d+)\b");
        Version expected = Version.Parse(SubscriptionCliInstaller.VersionFor(Backend));
        if (!match.Success || !Version.TryParse(match.Value, out var version) || version != expected)
            throw new InvalidOperationException($"対応するCLIは {expected} です。設定の「対応版CLIをインストール」から導入するか、対応版の実行ファイルを指定してください。");
    }

    internal static void Stop(Process process)
    {
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
