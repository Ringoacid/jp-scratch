using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using JpScratch.Models;

namespace JpScratch.Proofreading;

internal static class SubscriptionCliInstaller
{
    internal const string CodexVersion = "0.153.4";
    internal const string CopilotVersion = "1.0.83";
    private const long MaximumExpandedBytes = 1_500_000_000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    internal static string VersionFor(BackendKind backend) => backend == BackendKind.CodexAppServer ? CodexVersion : CopilotVersion;
    internal static string InstallRoot(BackendKind backend) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JpScratchRuntimes", "cli", backend.ToString());

    internal static string? InstalledExecutable(BackendKind backend)
    {
        string root = InstallRoot(backend), marker = Path.Combine(root, "current.path");
        try
        {
            if (!File.Exists(marker)) return null;
            string executable = File.ReadAllText(marker).Trim();
            return Path.GetFullPath(executable).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && File.Exists(executable) && Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? executable : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    // SHA-256 values published on the official GitHub release assets. Pin with the protocol version.
    internal static (Uri Url, string Sha256) Package(BackendKind backend, Architecture architecture)
    {
        bool arm = architecture == Architecture.Arm64;
        if (!arm && architecture != Architecture.X64) throw new NotSupportedException("CLIの自動導入は64ビットWindowsに対応しています。");
        return backend switch
        {
            BackendKind.CodexAppServer => (new Uri($"https://github.com/openai/codex/releases/download/rust-v{CodexVersion}/codex-package-{(arm ? "aarch64" : "x86_64")}-pc-windows-msvc.tar.gz"),
                arm ? "ac51b1a5932e07dffcaa6e98f4801f13b25192094739b732fc8b40ddb41bbda2" : "a6ef3442cb12766a88b39311d79244289e4f9763e2c53ff4fbebc2cb653cc5f3"),
            BackendKind.GitHubCopilot => (new Uri($"https://github.com/github/copilot-cli/releases/download/v{CopilotVersion}/copilot-win32-{(arm ? "arm64" : "x64")}.zip"),
                arm ? "63f35c0ce1a5fdcc6f3e584890d689b1ede8f930933394aaf7b5e139b53d2cc1" : "0e07221a275fdf7e61619c53566e3a421fd646d74d8e9ca491dbbff221f22945"),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
    }

    internal static async Task<string> InstallAsync(BackendKind backend, IProgress<string> progress, CancellationToken token)
    {
        var package = Package(backend, RuntimeInformation.OSArchitecture);
        string root = InstallRoot(backend);
        Directory.CreateDirectory(root);
        string staging = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string archive = Path.Combine(staging, "package");
            progress.Report("公式配布元に接続中…");
            using (var response = await Http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(archive);
                byte[] buffer = new byte[81920];
                long total = 0, lastReported = -1;
                while (await input.ReadAsync(buffer, token) is int count && count > 0)
                {
                    total += count;
                    if (total > 400_000_000) throw new IOException("配布ファイルが想定サイズを超えました。");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    long mb = total / 1_000_000;
                    if (mb != lastReported) { progress.Report($"ダウンロード中… {mb} MB"); lastReported = mb; }
                }
            }
            progress.Report("配布ファイルの整合性を確認中…");
            await using (var file = File.OpenRead(archive))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("公式配布ファイルのハッシュと一致しません。インストールを中止しました。");
            string extracted = Path.Combine(staging, "files");
            Directory.CreateDirectory(extracted);
            progress.Report("展開してバージョンを確認中…");
            await Task.Run(() => Extract(archive, extracted, backend == BackendKind.GitHubCopilot, token), token);
            string name = backend == BackendKind.CodexAppServer ? "codex.exe" : "copilot.exe";
            string[] matches = Directory.GetFiles(extracted, name, SearchOption.AllDirectories);
            if (matches.Length != 1) throw new IOException("配布ファイル内のCLIを特定できませんでした。");
            var runtime = new SubscriptionRuntime(backend, matches[0]);
            await runtime.CheckVersionAsync(token); // --version only; no login or generation.
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(extracted, matches[0]);
            string destination = Path.Combine(root, VersionFor(backend) + "-" + Guid.NewGuid().ToString("N"));
            Directory.Move(extracted, destination);
            string executable = Path.Combine(destination, relative);
            string marker = Path.Combine(staging, "current.path");
            await File.WriteAllTextAsync(marker, executable, CancellationToken.None);
            File.Move(marker, Path.Combine(root, "current.path"), overwrite: true);
            return executable;
        }
        finally
        {
            // This unique staging directory is always a direct child of our installation root.
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static string EntryPath(string root, string name)
    {
        if (Path.IsPathRooted(name) || name.Contains(':')) throw new IOException("不正な展開先です。");
        string path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("配布ファイルが展開先の外を参照しています。");
        return path;
    }

    internal static void Extract(string archive, string root, bool zip, CancellationToken token)
    {
        long expanded = 0;
        void Write(string name, long length, Stream? input, bool directory)
        {
            token.ThrowIfCancellationRequested();
            if (name is "." or "./") return;
            string path = EntryPath(root, name);
            if (directory) { Directory.CreateDirectory(path); return; }
            expanded = checked(expanded + length);
            if (expanded > MaximumExpandedBytes || input is null) throw new IOException("配布ファイルを安全に展開できませんでした。");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var output = new FileStream(path, FileMode.CreateNew);
            input.CopyToAsync(output, token).GetAwaiter().GetResult();
        }
        if (zip)
        {
            using var file = ZipFile.OpenRead(archive);
            foreach (var entry in file.Entries)
            {
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("リンクを含む配布ファイルは展開できません。");
                using var input = entry.Open();
                Write(entry.FullName, entry.Length, input, entry.FullName.EndsWith('/'));
            }
        }
        else
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            while (tar.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory))
                    throw new IOException("未対応の形式を含む配布ファイルです。");
                Write(entry.Name, entry.Length, entry.DataStream, entry.EntryType == TarEntryType.Directory);
            }
        }
    }
}
