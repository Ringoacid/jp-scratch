using System.Text.Json.Serialization;

namespace JpScratch.PromptValidation;

internal sealed record ValidationCase(
    string Id,
    string Kind,
    string Text,
    string? BeforeContext,
    string? AfterContext,
    IReadOnlyList<ExpectedChange>? ExpectedChanges);

internal sealed record ExpectedChange(string From, IReadOnlyList<string> To);

internal sealed record CorrectionResponse(
    [property: JsonPropertyName("corrections")]
    IReadOnlyList<Correction> Corrections);

internal sealed record Correction(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("original")] string Original,
    [property: JsonPropertyName("suggestion")] string Suggestion,
    [property: JsonPropertyName("left_context")] string LeftContext,
    [property: JsonPropertyName("right_context")] string RightContext,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("confidence")] double Confidence);

internal sealed record Usage(
    [property: JsonPropertyName("promptTokenCount")] int PromptTokens,
    [property: JsonPropertyName("candidatesTokenCount")] int CandidateTokens,
    [property: JsonPropertyName("totalTokenCount")] int TotalTokens);

internal sealed record ProbeResult(
    CorrectionResponse Response,
    string? CorrectedText,
    Usage Usage,
    TimeSpan Elapsed,
    decimal CostUsd);

internal sealed record ResolvedCorrection(Correction Correction, int Start);

internal sealed record CaseResult(
    string Id,
    string Kind,
    string Variant,
    int Iteration,
    bool Passed,
    IReadOnlyList<string> Failures,
    IReadOnlyList<Correction> Corrections,
    string? CorrectedText,
    int DiscardedCount,
    int PromptTokens,
    int CandidateTokens,
    double ElapsedMilliseconds,
    decimal CostUsd);
