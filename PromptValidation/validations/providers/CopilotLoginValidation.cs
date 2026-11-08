using JpScratch.Models;
using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class CopilotLoginValidation
{
    internal static async Task<bool> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "jpscratch-login-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if (!await TestStorageSettingsAsync(root)) return false;
            var runtime = new SubscriptionRuntime(BackendKind.GitHubCopilot, Environment.ProcessPath!);
            // Self-test runs through the apphost. The child only asks a local question and
            // writes a boolean fixture; it never authenticates or calls an external service.
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") return false;
            foreach (bool accepted in new[] { true, false })
            {
                string marker = Path.Combine(root, accepted ? "accepted" : "declined");
                int questions = 0;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                bool declined = false;
                string output = "";
                try
                {
                    output = await runtime.RunAsync(["login", "--mock-copilot-login", marker], null, timeout.Token,
                        _ => { questions++; return Task.FromResult(accepted); });
                }
                catch (InvalidOperationException e) when (e.Message.Contains("保存しなかった")) { declined = true; }
                if (questions != 1 || File.Exists(marker) != accepted || declined == accepted ||
                    (accepted && !output.Contains("Signed in successfully"))) return false;
            }
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            bool canceled = false;
            string canceledMarker = Path.Combine(root, "canceled");
            try
            {
                await runtime.RunAsync(["login", "--mock-copilot-login", canceledMarker], null, cancel.Token,
                    async t => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, t); return true; });
            }
            catch (OperationCanceledException) { canceled = true; }
            if (!canceled || File.Exists(canceledMarker)) return false;
            using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            bool failureReported = false;
            try { await runtime.RunAsync(["login", "--mock-copilot-login", "failure"], null, failureTimeout.Token); }
            catch (IOException e)
            {
                failureReported = e.Message.Contains("終了コード 7") && e.Message.Contains("non-interactive") &&
                    !e.Message.Contains("fixturesecret") && !e.Message.Contains("https://");
            }
            if (!failureReported) return false;
            var environment = runtime.EnvironmentVariables();
            return environment["COPILOT_HOME"] == runtime.Home && environment["COPILOT_DISABLE_KEYTAR"] == "1" &&
                environment["GH_CONFIG_DIR"] == Path.Combine(runtime.Home, "gh") &&
                !environment.Keys.Any(k => k.StartsWith("COPILOT_PROVIDER_", StringComparison.OrdinalIgnoreCase)) &&
                !SubscriptionRuntime.IsCredentialStoragePrompt("Some unknown question? (y/N)");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<bool> TestStorageSettingsAsync(string root)
    {
        string home = Path.Combine(root, "settings-test");
        string path = Path.Combine(home, "settings.json");
        bool declined = false;
        try { await CopilotLoginSettings.PrepareAsync(home, _ => Task.FromResult(false), CancellationToken.None); }
        catch (InvalidOperationException) { declined = true; }
        if (!declined || Directory.Exists(home)) return false;
        using var cancel = new CancellationTokenSource();
        bool canceled = false;
        try
        {
            await CopilotLoginSettings.PrepareAsync(home,
                async t => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, t); return true; }, cancel.Token);
        }
        catch (OperationCanceledException) { canceled = true; }
        if (!canceled || Directory.Exists(home)) return false;
        Directory.CreateDirectory(home);
        string credentialPath = Path.Combine(home, "config.json");
        const string credentialFixture = "{\"fixture\":\"CLI owns this file\"}";
        await File.WriteAllTextAsync(credentialPath, credentialFixture);
        await File.WriteAllTextAsync(path, "{\"theme\":\"dim\",\"custom\":{\"enabled\":true}}");
        int questions = 0;
        await CopilotLoginSettings.PrepareAsync(home, _ => { questions++; return Task.FromResult(true); }, CancellationToken.None);
        var settings = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        if (questions != 1 || settings["storeTokenPlaintext"]?.GetValue<bool>() != true ||
            settings["theme"]?.GetValue<string>() != "dim" || settings["custom"]?["enabled"]?.GetValue<bool>() != true ||
            await File.ReadAllTextAsync(credentialPath) != credentialFixture || Directory.GetFiles(home, "*.tmp").Length != 0) return false;
        await File.WriteAllTextAsync(path, "broken JSON");
        bool malformed = false;
        try { await CopilotLoginSettings.PrepareAsync(home, _ => Task.FromResult(true), CancellationToken.None); }
        catch (InvalidOperationException) { malformed = true; }
        return malformed && await File.ReadAllTextAsync(path) == "broken JSON";
    }

    internal static async Task<int> RunMockAsync(string marker)
    {
        if (marker == "failure")
        {
            await Console.Error.WriteLineAsync("Cannot store credentials in non-interactive mode.\nhttps://localhost/callback?code=fixturesecret\ngho_fixturesecret\naccess_token: fixturesecret");
            return 7;
        }
        // Deliberately no newline, split writes, and ANSI decoration, just like a CLI prompt.
        await Console.Error.WriteAsync("Browser authorization received.\n\u001b[33mSystem keychain unavailable. Store tok");
        await Console.Error.FlushAsync();
        await Task.Delay(30);
        await Console.Error.WriteAsync("en in plaintext config file? (y/N)\u001b[0m ");
        await Console.Error.FlushAsync();
        string? answer = await Console.In.ReadLineAsync();
        if (answer != "y") return 1;
        await File.WriteAllTextAsync(marker, "saved");
        await Console.Out.WriteLineAsync("Signed in successfully.");
        return 0;
    }
}
