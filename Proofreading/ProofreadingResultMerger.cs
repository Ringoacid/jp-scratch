namespace JpScratch.Proofreading;

/// <summary>段落・分割単位のローカル差分を、送信時点の文書全文へ戻す。</summary>
internal static class ProofreadingResultMerger
{
    internal static string Merge(
        ProofreadingPlan plan,
        IReadOnlyList<(ProofreadingRequest Request, GeminiProofreadingResult Result)> results)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(results);

        List<DocumentChange> changes = [];
        foreach ((ProofreadingRequest request, GeminiProofreadingResult result) in results)
        {
            if (!result.Diff.Accepted)
                continue;

            changes.AddRange(result.Diff.Changes.Select(change => change with
            {
                Start = request.SourceStart + change.Start,
            }));
        }

        return changes.Count == 0
            ? plan.DocumentText
            : DocumentDiff.Apply(
                plan.DocumentText,
                changes.OrderBy(change => change.Start).ToArray());
    }
}
