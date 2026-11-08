using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JpScratch.Proofreading;

internal static class CopilotLoginSettings
{
    internal static async Task PrepareAsync(string home,
        Func<CancellationToken, Task<bool>>? confirmStorage, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Redirected stdin is not a terminal: the CLI can decline plaintext storage
        // without issuing a question. Obtain consent before OAuth and use its official
        // preference. Credentials themselves remain entirely managed by the CLI.
        if (confirmStorage is null || !await confirmStorage(token).WaitAsync(token).ConfigureAwait(false))
            throw new InvalidOperationException("ログイン情報の保存を許可しなかったため、ログインを開始しませんでした。");
        token.ThrowIfCancellationRequested();
        string path = Path.Combine(home, "settings.json");
        JsonObject settings;
        try
        {
            settings = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, token).ConfigureAwait(false),
                    documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                    ?? throw new JsonException()
                : new JsonObject();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Copilotの設定ファイルを読み取れません。既存の設定を保護するため、ログインを開始しませんでした。");
        }
        settings["storeTokenPlaintext"] = true;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(home);
            await File.WriteAllTextAsync(temporary, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
