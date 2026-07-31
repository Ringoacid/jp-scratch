using JpScratch.Proofreading;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 要件3.4.1のfew-shot選定（<see cref="FewShotSelector"/>）とスタイルガイド生成の入力選定
/// （<see cref="StyleGuideSourceSelector"/>）の自己テスト。実APIは呼ばない。
/// </summary>
internal static class FewShotSelectorValidation
{
    internal static bool RunSelfTests()
    {
        bool priorityPass = RunPriorityTest();
        bool overlapPass = RunOverlapTest();
        bool countCapPass = RunCountCapTest();
        bool budgetPass = RunCharacterBudgetTest();
        bool formatPass = RunFormatLineTest();
        bool styleGuideSourcePass = RunStyleGuideSourceTest();

        bool passed = priorityPass && overlapPass && countCapPass && budgetPass &&
                      formatPass && styleGuideSourcePass;
        Console.WriteLine($"few-shot選定（優先順位・重なり・件数上限・文字数上限・書式）: {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static bool RunPriorityTest()
    {
        // (a) 拒否・理由つき拒否を優先。newerだがacceptの候補より、古いrejectを優先すべき。
        var now = DateTimeOffset.Now;
        var accepted = new FewShotCandidate("同じ", "違う", ProofreadingReaction.Accept, null, now);
        var rejected = new FewShotCandidate("同じ", "違う", ProofreadingReaction.Reject, null, now.AddDays(-10));

        FewShotSelection selection = FewShotSelector.Select([accepted, rejected], "無関係なテキスト");
        return selection.Examples.Count == 2 &&
               selection.Examples[0].Reaction == ProofreadingReaction.Reject &&
               selection.Examples[1].Reaction == ProofreadingReaction.Accept;
    }

    private static bool RunOverlapTest()
    {
        // (b) 校正対象テキストと語句が重なるものを優先。同じ拒否カテゴリ内で比較する。
        var now = DateTimeOffset.Now;
        var overlapping = new FewShotCandidate(
            "コミニュケーション", "コミュニケーション", ProofreadingReaction.Reject, null, now.AddDays(-5));
        var unrelated = new FewShotCandidate(
            "全く別のことば", "別の言い回し", ProofreadingReaction.Reject, null, now);

        FewShotSelection selection = FewShotSelector.Select(
            [unrelated, overlapping], "コミニュケーションについて話す文章");
        return selection.Examples.Count == 2 &&
               selection.Examples[0].Original == "コミニュケーション";
    }

    private static bool RunCountCapTest()
    {
        var now = DateTimeOffset.Now;
        var candidates = Enumerable.Range(0, 30)
            .Select(i => new FewShotCandidate(
                $"原文{i}", $"修正{i}", ProofreadingReaction.Reject, null, now.AddSeconds(-i)))
            .ToArray();

        FewShotSelection selection = FewShotSelector.Select(candidates, "テキスト");
        return selection.Examples.Count == FewShotSelector.MaxExamples &&
               selection.ConsideredCount == 30 &&
               selection.DroppedForBudgetCount == 30 - FewShotSelector.MaxExamples;
    }

    private static bool RunCharacterBudgetTest()
    {
        var now = DateTimeOffset.Now;
        // 1件が文字数上限に迫るほど長い候補を複数用意し、件数上限より先に文字数上限へ触れさせる。
        string longOriginal = new('あ', 300);
        string longSuggestion = new('い', 300);
        var candidates = Enumerable.Range(0, 10)
            .Select(i => new FewShotCandidate(
                longOriginal, longSuggestion, ProofreadingReaction.Reject, null, now.AddSeconds(-i)))
            .ToArray();

        FewShotSelection selection = FewShotSelector.Select(candidates, "テキスト");
        int totalChars = selection.Examples.Sum(example => example.FormatLine().Length);
        return selection.Examples.Count < candidates.Length &&
               totalChars <= FewShotSelector.MaxTotalCharacters &&
               selection.DroppedForBudgetCount > 0;
    }

    private static bool RunFormatLineTest()
    {
        var accept = new FewShotExample("文章ア", "文章が", ProofreadingReaction.Accept, null);
        var reject = new FewShotExample("思ってた", "思っていた", ProofreadingReaction.Reject, null);
        var rejectWithReason = new FewShotExample(
            "思ってた", "思っていた", ProofreadingReaction.RejectWithReason, "話し言葉として意図的");

        return accept.FormatLine() == "- 「文章ア」→「文章が」: 許可された" &&
               reject.FormatLine() == "- 「思ってた」→「思っていた」: 拒否された" &&
               rejectWithReason.FormatLine() ==
                   "- 「思ってた」→「思っていた」: 拒否された（理由: 話し言葉として意図的）";
    }

    private static bool RunStyleGuideSourceTest()
    {
        var now = DateTimeOffset.Now;
        // 新しい順（GetFewShotCandidatesの並びと同じ前提）で400件用意し、件数上限300で切られることを確認する。
        var candidates = Enumerable.Range(0, 400)
            .Select(i => new FewShotCandidate(
                $"原文{i}", $"修正{i}", ProofreadingReaction.Reject, null, now.AddSeconds(-i)))
            .ToArray();

        StyleGuideSourceSelection selection = StyleGuideSourceSelector.Select(candidates);
        return selection.Examples.Count == StyleGuideSourceSelector.MaxReactions &&
               selection.Considered == StyleGuideSourceSelector.MaxReactions &&
               selection.Examples[0].Original == "原文0";
    }
}
