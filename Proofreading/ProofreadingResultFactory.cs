namespace JpScratch.Proofreading;

internal static class ProofreadingResultFactory
{
    internal static GeminiProofreadingResult Create(string source, GeminiRawTextResult raw)
    {
        if (string.IsNullOrWhiteSpace(raw.Text)) throw new GeminiClientException(GeminiClientError.InvalidResponse, "校正結果が空です。", usage: raw.Usage);
        string text = ProofreadingPrompt.UnescapeDocumentBoundary(raw.Text);
        return new(text, DocumentDiff.Create(source, text), raw.Usage, raw.Elapsed, raw.Attempts);
    }

    internal static GeminiAlternativeResult Alternative(ProofreadingProposal proposal, GeminiProofreadingResult result)
    {
        string text = result.CorrectedText.Trim();
        if (text == proposal.Original || text == proposal.Suggestion || text.Length == 0)
            throw new GeminiClientException(GeminiClientError.InvalidResponse, "有効な別案が返されませんでした。", usage: result.Usage, elapsed: result.Elapsed);
        return new(text, result.Usage, result.Elapsed, result.Attempts);
    }
}
