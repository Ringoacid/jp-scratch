using System.Net;
using JpScratch.Models;

namespace JpScratch.Proofreading;

// このファイルの型はプロバイダーに依存しない共通契約。名前の Gemini 接頭辞は v2 の名残で、
// 改名すると本体・検証アプリの広い範囲へ機械的な差分が出るため据え置いている。
// 実際には Gemini / OpenAI / Anthropic / PLaMo のすべてで共有する。

internal enum GeminiClientError
{
    MissingApiKey,
    Timeout,
    RequestFailed,
    InvalidResponse,
}

internal sealed class GeminiClientException : Exception
{
    internal GeminiClientError Error { get; }
    internal HttpStatusCode? StatusCode { get; }
    /// <summary>HTTP成功後に判明した使用量。応答を安全に採用できない場合だけ任意で保持する。</summary>
    internal GeminiUsage? Usage { get; }
    internal TimeSpan? Elapsed { get; }

    internal GeminiClientException(
        GeminiClientError error,
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null,
        GeminiUsage? usage = null,
        TimeSpan? elapsed = null)
        : base(message, innerException)
    {
        Error = error;
        StatusCode = statusCode;
        Usage = usage;
        Elapsed = elapsed;
    }
}

/// <summary>
/// プロバイダー共通の使用量。思考トークンの内訳やキャッシュトークン数を返さないプロバイダー
/// （Anthropic・PLaMo）では該当項目を 0 とし、課金対象の出力へ全量を寄せる。
/// <see cref="BillableOutputTokens"/> の合計はどのプロバイダーでも課金対象の出力トークン数に一致する。
/// </summary>
internal sealed record GeminiUsage(
    int PromptTokens,
    int CandidateTokens,
    int ThoughtsTokens,
    int CachedContentTokens,
    int TotalTokens)
{
    internal int BillableOutputTokens => CandidateTokens + ThoughtsTokens;
}

internal sealed record GeminiProofreadingResult(
    string CorrectedText,
    DocumentDiffResult Diff,
    GeminiUsage Usage,
    TimeSpan Elapsed,
    int Attempts);

internal sealed record GeminiAlternativeResult(
    string Alternative,
    GeminiUsage Usage,
    TimeSpan Elapsed,
    int Attempts);

/// <summary>スタイルガイド自動生成（要件3.4.2）の結果。文書全文の差分検査は行わない。</summary>
internal sealed record GeminiStyleGuideResult(
    string Content,
    GeminiUsage Usage,
    TimeSpan Elapsed,
    int Attempts);

/// <summary>HTTP応答から抽出した生テキストと使用量。校正の差分検査より前の共通の中間結果。</summary>
internal sealed record GeminiRawTextResult(
    string Text,
    GeminiUsage Usage,
    TimeSpan Elapsed,
    int Attempts);

/// <summary>プロバイダーに依存しない校正クライアントの呼び出し口。</summary>
internal interface IProofreadingClient : IDisposable
{
    string Model { get; }

    Task<GeminiProofreadingResult> ProofreadAsync(
        ProofreadingRequest request,
        CancellationToken cancellationToken = default);

    Task<GeminiAlternativeResult> GenerateAlternativeAsync(
        ProofreadingProposal proposal,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>要件3.4.2。蓄積されたリアクション履歴からスタイルガイドの本文を生成する。</summary>
    Task<GeminiStyleGuideResult> GenerateStyleGuideAsync(
        IReadOnlyList<FewShotExample> reactionHistory,
        CancellationToken cancellationToken = default);
}
