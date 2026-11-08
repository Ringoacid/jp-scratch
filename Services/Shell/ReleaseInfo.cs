using System.Reflection;
using System.Runtime.InteropServices;

namespace JpScratch.Services;

/// <summary>公開ページ、更新確認、診断情報で共有するリリース情報。</summary>
internal static class ReleaseInfo
{
    public const string RepositoryUrl = "https://github.com/Ringoacid/jp-scratch";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string IssuesUrl = RepositoryUrl + "/issues/new";
    public const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Ringoacid/jp-scratch/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            string? informational = typeof(ReleaseInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+', 2)[0];

            return typeof(ReleaseInfo).Assembly.GetName().Version?.ToString(3) ?? "不明";
        }
    }

    public static string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
}
