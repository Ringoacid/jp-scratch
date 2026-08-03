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

    /// <summary>
    /// 部分結果保持（proofreading-ux-fixes-plan.md §7.2）用の全文構築。
    /// 本文が送信時スナップショットから編集されていても、編集されていない文章ブロックの結果は
    /// 現在の本文位置へ安全に対応付けて提案として表示する。
    ///
    /// <paramref name="currentDocument"/> は現在の本文全文。<paramref name="results"/> の各要素は
    /// 「そのリクエストの対象が現在も一致する位置（現在座標のパート先頭）」と結果の組。
    /// 呼び出し側（MainWindow）はリクエスト対象の段落が現在も無変更であることを確認済みで、
    /// <see cref="CurrentPartStart"/> はその段落内での相対位置を使って現在座標へ変換したもの。
    ///
    /// 編集されたブロックは currentDocument と changes の双方に現れないため、この戻り値を
    /// <see cref="DocumentDiff.Create"/>（現在本文 vs この結果）で検査すると、有効な結果の変更だけが
    /// 提案として出る。破棄された結果の変更はここに含めない。
    /// </summary>
    internal static string MergePartial(
        string currentDocument,
        IReadOnlyList<(int CurrentPartStart, GeminiProofreadingResult Result)> results)
    {
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(results);

        List<DocumentChange> changes = [];
        foreach ((int partStart, GeminiProofreadingResult result) in results)
        {
            if (!result.Diff.Accepted)
                continue;

            foreach (DocumentChange change in result.Diff.Changes)
            {
                if (change.Start < 0 || partStart + change.Start < 0)
                    continue;
                changes.Add(change with { Start = partStart + change.Start });
            }
        }

        if (changes.Count == 0)
            return currentDocument;

        // 各リクエストの対象は互いに素（異なる段落・または同一段落の互いに素なパート）なので、
        // オフセットを現在座標へ直した後は重なり得ない。DocumentDiff.Apply が降順適用と原文照合で
        // 安全性を再確認する。
        return DocumentDiff.Apply(
            currentDocument,
            changes.OrderBy(change => change.Start).ToArray());
    }
}
