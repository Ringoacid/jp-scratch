using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JpScratch.Services;

internal sealed record ReleaseUpdateInfo(
    string TagName,
    string Title,
    string HtmlUrl,
    Version Version);

internal sealed record UpdateCheckResult(
    ReleaseUpdateInfo? Latest,
    bool IsNewer,
    string? Error)
{
    public bool Succeeded => Error is null && Latest is not null;
}

/// <summary>
/// GitHub Releases の最新タグだけを確認する。ダウンロードや自動インストールは行わない。
/// </summary>
internal static class ReleaseUpdateService
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateCheckResult> CheckLatestAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(
                ReleaseInfo.LatestReleaseApiUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string detail = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Release情報が見つかりませんでした。リポジトリのURLが変更された可能性があります。"
                    : $"更新情報の取得に失敗しました（HTTP {(int)response.StatusCode}）。";
                return new UpdateCheckResult(null, false, detail);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;

            string tagName = root.TryGetProperty("tag_name", out JsonElement tag)
                ? tag.GetString() ?? ""
                : "";
            string htmlUrl = root.TryGetProperty("html_url", out JsonElement url)
                ? url.GetString() ?? ReleaseInfo.ReleasesUrl
                : ReleaseInfo.ReleasesUrl;
            string title = root.TryGetProperty("name", out JsonElement name)
                ? name.GetString() ?? tagName
                : tagName;

            if (!TryParseVersion(tagName, out Version? latestVersion))
                return new UpdateCheckResult(null, false, "Releaseのバージョン番号を解釈できませんでした。");

            if (!TryParseVersion(currentVersion, out Version? installedVersion))
                return new UpdateCheckResult(null, false, "現在のバージョン番号を解釈できませんでした。");

            var latest = new ReleaseUpdateInfo(tagName, title, htmlUrl, latestVersion!);
            return new UpdateCheckResult(latest, latestVersion! > installedVersion!, null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(null, false, "更新確認がタイムアウトまたはキャンセルされました。");
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(null, false, "更新情報を取得できませんでした。ネットワーク接続を確認してください。");
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(null, false, "更新情報の形式を解釈できませんでした。");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("JP-Scratch", ReleaseInfo.CurrentVersion));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        string normalized = value.Trim().TrimStart('v', 'V');
        int separator = normalized.IndexOfAny(['-', '+']);
        if (separator >= 0) normalized = normalized[..separator];

        return Version.TryParse(normalized, out version) && version is not null;
    }
}
