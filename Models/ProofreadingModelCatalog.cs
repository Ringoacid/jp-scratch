namespace JpScratch.Models;

/// <summary>校正で利用できるAIモデルと、そのAPIプロバイダーの対応。</summary>
public static class ProofreadingModelCatalog
{
    public const string GeminiModel = "gemini-3.5-flash-lite";
    public const string OpenAiModel = "gpt-5.6-luna";

    public static IReadOnlyList<string> SupportedModels { get; } =
        [GeminiModel, OpenAiModel];

    public static bool IsSupported(string? model)
        => model is not null && SupportedModels.Contains(model, StringComparer.Ordinal);

    public static bool IsOpenAi(string? model)
        => string.Equals(model, OpenAiModel, StringComparison.Ordinal);

    public static string DisplayName(string model)
        => model switch
        {
            GeminiModel => "Gemini 3.5 Flash Lite",
            OpenAiModel => "GPT 5.6 Luna",
            _ => model,
        };
}
