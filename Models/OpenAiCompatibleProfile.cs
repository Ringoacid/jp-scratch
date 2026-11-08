using System.Globalization;

namespace JpScratch.Models;

/// <summary>ユーザーが登録する OpenAI Chat Completions 互換の接続先と概算単価。</summary>
public sealed record OpenAiCompatibleProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    /// <summary>/chat/completions までを含む完全な URL。</summary>
    public string EndpointUrl { get; init; } = "";
    public string ModelId { get; init; } = "";
    public decimal InputUsdPerMillion { get; init; }
    public decimal OutputUsdPerMillion { get; init; }
    public string PricingUpdatedAt { get; init; } = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string SelectionId => ModelKey(Id);

    public static string ModelKey(string id) => "openai-compatible:" + id;

    public static bool TryGetProfileId(string? model, out string id)
    {
        id = "";
        const string prefix = "openai-compatible:";
        if (model is null || !model.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string candidate = model[prefix.Length..];
        if (!Guid.TryParseExact(candidate, "N", out _)) return false;
        id = candidate;
        return true;
    }

    public static OpenAiCompatibleProfile? Find(AppSettings settings, string? selectionId)
        => TryGetProfileId(selectionId, out string id)
            ? settings.OpenAiCompatibleProfiles.FirstOrDefault(p => p.Id == id)
            : null;

    public static bool IsValid(OpenAiCompatibleProfile? profile)
        => profile is not null &&
           Guid.TryParseExact(profile.Id, "N", out _) &&
           !string.IsNullOrWhiteSpace(profile.Name) &&
           !string.IsNullOrWhiteSpace(profile.ModelId) &&
           Uri.TryCreate(profile.EndpointUrl, UriKind.Absolute, out Uri? uri) &&
           uri.Scheme is "https" or "http" &&
           uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) &&
           string.IsNullOrEmpty(uri.UserInfo) &&
           string.IsNullOrEmpty(uri.Query) &&
           string.IsNullOrEmpty(uri.Fragment) &&
           profile.InputUsdPerMillion is >= 0m and <= 1_000_000_000m &&
           profile.OutputUsdPerMillion is >= 0m and <= 1_000_000_000m &&
           DateOnly.TryParseExact(profile.PricingUpdatedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture,
               DateTimeStyles.None, out _);
}
