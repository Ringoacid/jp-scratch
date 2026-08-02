namespace JpScratch.Editor;

/// <summary>
/// 校正提案1件の描画用スナップショット。
/// <see cref="JpScratch.Proofreading.ProofreadingProposal"/> は失効すると
/// Start / Length の取得で例外を投げるため、描画層へは必ずこの不変レコードを渡す。
/// </summary>
internal readonly record struct ProofreadingInlineDiff(
    int Start,
    int Length,
    string Original,
    string Suggestion);

/// <summary>
/// インライン差分表示の位置決めロジック。WPF に依存しないので自己テストできる。
/// </summary>
internal static class ProofreadingInlineDiffLayout
{
    /// <summary>
    /// その行の中に完全に収まる提案だけを描く。行をまたぐ提案を要素にすると
    /// VisualLine の構築が壊れるため、ここで必ず弾く。
    /// </summary>
    internal static bool IsRenderable(
        ProofreadingInlineDiff diff,
        int lineStartOffset,
        int lineEndOffset)
        => diff.Length > 0 &&
           diff.Start >= lineStartOffset &&
           diff.Start + diff.Length <= lineEndOffset;

    /// <summary>
    /// startOffset 以降で最初に描画対象となる提案の開始位置。無ければ -1。
    /// </summary>
    internal static int FirstInterestedOffset(
        IReadOnlyList<ProofreadingInlineDiff> diffs,
        int startOffset,
        int lineStartOffset,
        int lineEndOffset)
    {
        int best = -1;
        foreach (ProofreadingInlineDiff diff in diffs)
        {
            if (diff.Start < startOffset)
                continue;
            if (!IsRenderable(diff, lineStartOffset, lineEndOffset))
                continue;
            if (best < 0 || diff.Start < best)
                best = diff.Start;
        }

        return best;
    }

    /// <summary>指定オフセットちょうどから始まる描画対象を探す。</summary>
    internal static bool TryFindAt(
        IReadOnlyList<ProofreadingInlineDiff> diffs,
        int offset,
        int lineStartOffset,
        int lineEndOffset,
        out ProofreadingInlineDiff found)
    {
        foreach (ProofreadingInlineDiff diff in diffs)
        {
            if (diff.Start != offset)
                continue;
            if (!IsRenderable(diff, lineStartOffset, lineEndOffset))
                continue;

            found = diff;
            return true;
        }

        found = default;
        return false;
    }

    /// <summary>削除提案（修正案が空）では緑のテキストを出さない。</summary>
    internal static bool HasSuggestionText(ProofreadingInlineDiff diff)
        => diff.Suggestion.Length > 0;
}
