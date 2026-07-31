using JpScratch.Services;

namespace JpScratch.Proofreading;

/// <summary>few-shot選定の入力候補（<see cref="ReactionRepository.GetFewShotCandidates"/>が返す）。</summary>
internal sealed record FewShotCandidate(
    string Original,
    string Suggestion,
    ProofreadingReaction Reaction,
    string? UserReason,
    DateTimeOffset ReactedAt);

/// <summary>プロンプトへ同梱する1件のfew-shot例。</summary>
internal sealed record FewShotExample(
    string Original,
    string Suggestion,
    ProofreadingReaction Reaction,
    string? UserReason)
{
    /// <summary>要件3.4.1の送信例フォーマット（例: 「文章ア」→「文章が」: 許可された）。</summary>
    internal string FormatLine()
    {
        string verdict = Reaction switch
        {
            ProofreadingReaction.Accept => "許可された",
            ProofreadingReaction.Reject => "拒否された",
            ProofreadingReaction.RejectWithReason => string.IsNullOrWhiteSpace(UserReason)
                ? "拒否された"
                : $"拒否された（理由: {UserReason}）",
            _ => "拒否された",
        };
        return $"- 「{Original}」→「{Suggestion}」: {verdict}";
    }
}

/// <summary>
/// <see cref="Select"/>の結果。<see cref="ConsideredCount"/>は選定対象になった候補の総数、
/// <see cref="DroppedForBudgetCount"/>は文字数上限に触れて選定から漏れた件数。
/// </summary>
internal sealed record FewShotSelection(
    IReadOnlyList<FewShotExample> Examples,
    int ConsideredCount,
    int DroppedForBudgetCount);

/// <summary>
/// 要件3.4.1のfew-shot選定ロジック。優先順位は
/// (a) 拒否・理由つき拒否を優先 (b) 校正対象テキストと語句が重なるものを優先 (c) 新しいものを優先。
/// R-6（few-shotの肥大でトークンを浪費する）対策として、件数上限だけでなく総文字数の上限も掛ける。
/// </summary>
internal static class FewShotSelector
{
    /// <summary>要件3.4.1の既定K。</summary>
    internal const int MaxExamples = 15;

    /// <summary>
    /// 送信例1件あたりの文脈（前後1段落を含む校正対象文）はこのブロックには含まれないが、
    /// original/suggestion/reasonだけでも件数×長さで入力トークンが読めなくなるため上限を掛ける。
    /// </summary>
    internal const int MaxTotalCharacters = 2000;

    internal static FewShotSelection Select(
        IReadOnlyList<FewShotCandidate> candidates,
        string targetText)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(targetText);

        if (candidates.Count == 0)
            return new FewShotSelection([], 0, 0);

        FewShotCandidate[] ordered = candidates
            .Select(candidate => (
                Candidate: candidate,
                CategoryRank: CategoryRank(candidate.Reaction),
                Overlap: ComputeOverlapScore(candidate.Original + candidate.Suggestion, targetText)))
            .OrderBy(entry => entry.CategoryRank)
            .ThenByDescending(entry => entry.Overlap)
            .ThenByDescending(entry => entry.Candidate.ReactedAt)
            .Select(entry => entry.Candidate)
            .ToArray();

        List<FewShotExample> selected = [];
        int totalCharacters = 0;
        int droppedForBudget = 0;

        for (int index = 0; index < ordered.Length; index++)
        {
            if (selected.Count >= MaxExamples)
            {
                droppedForBudget += ordered.Length - index;
                break;
            }

            FewShotCandidate candidate = ordered[index];
            var example = new FewShotExample(
                candidate.Original,
                candidate.Suggestion,
                candidate.Reaction,
                candidate.UserReason);
            string line = example.FormatLine();

            // 1行だけで予算を超える異常に長い例は、全体を締め出さないよう静かにスキップする。
            if (totalCharacters + line.Length > MaxTotalCharacters)
            {
                droppedForBudget++;
                continue;
            }

            selected.Add(example);
            totalCharacters += line.Length;
        }

        return new FewShotSelection(selected, ordered.Length, droppedForBudget);
    }

    private static int CategoryRank(ProofreadingReaction reaction)
        => reaction switch
        {
            ProofreadingReaction.Reject => 0,
            ProofreadingReaction.RejectWithReason => 0,
            ProofreadingReaction.Accept => 1,
            _ => 2,
        };

    /// <summary>文字2-gramのJaccard類似度。日本語は分かち書きが無いため、語単位の代わりに用いる。</summary>
    private static double ComputeOverlapScore(string candidateText, string targetText)
    {
        HashSet<string> a = ExtractNGrams(candidateText);
        HashSet<string> b = ExtractNGrams(targetText);
        if (a.Count == 0 || b.Count == 0)
            return 0;

        int intersection = a.Count(b.Contains);
        int union = a.Count + b.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> ExtractNGrams(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        string trimmed = text.Trim();
        if (trimmed.Length < 2)
        {
            if (trimmed.Length == 1)
                set.Add(trimmed);
            return set;
        }

        for (int i = 0; i < trimmed.Length - 1; i++)
            set.Add(trimmed.Substring(i, 2));
        return set;
    }
}

/// <summary>結果。<see cref="Considered"/>は入力候補の総数、選ばれたのは<see cref="Examples"/>だけ。</summary>
internal sealed record StyleGuideSourceSelection(
    IReadOnlyList<FewShotExample> Examples,
    int Considered);

/// <summary>
/// 要件3.4.2のスタイルガイド自動生成に渡す入力の選定。few-shotと違い校正対象テキストが無いため
/// 語句の重なりでは絞れないので、直近優先で件数・文字数の上限まで詰める。
/// </summary>
internal static class StyleGuideSourceSelector
{
    internal const int MaxReactions = 300;
    internal const int MaxTotalCharacters = 12_000;

    /// <summary>
    /// <paramref name="candidatesByRecency"/>は新しい順であることを前提とする
    /// （<see cref="Services.ReactionRepository.GetFewShotCandidates"/>の並びと同じ）。
    /// </summary>
    internal static StyleGuideSourceSelection Select(
        IReadOnlyList<FewShotCandidate> candidatesByRecency)
    {
        ArgumentNullException.ThrowIfNull(candidatesByRecency);

        List<FewShotExample> selected = [];
        int totalCharacters = 0;
        foreach (FewShotCandidate candidate in candidatesByRecency.Take(MaxReactions))
        {
            var example = new FewShotExample(
                candidate.Original,
                candidate.Suggestion,
                candidate.Reaction,
                candidate.UserReason);
            string line = example.FormatLine();
            if (totalCharacters + line.Length > MaxTotalCharacters)
                break;

            selected.Add(example);
            totalCharacters += line.Length;
        }

        return new StyleGuideSourceSelection(
            selected,
            Math.Min(candidatesByRecency.Count, MaxReactions));
    }
}
