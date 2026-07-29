using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class ParagraphProofreadingPlannerValidation
{
    internal static bool RunSelfTests()
    {
        (string Name, Func<bool> Test)[] tests =
        [
            ("空行区切り", TestBlankLineParagraphs),
            ("改行単位フォールバック", TestLineFallback),
            ("変更段落と前後文脈", TestChangedParagraphContext),
            ("段落挿入時の重複抑止", TestInsertedParagraph),
            ("2,000文字分割", TestLongParagraph),
            ("選択範囲", TestSelection),
            ("複数応答の全文統合", TestResultMerge),
        ];

        bool passed = true;
        foreach ((string name, Func<bool> test) in tests)
        {
            bool result = test();
            Console.WriteLine($"段落計画（{name}）: {(result ? "PASS" : "FAIL")}");
            passed &= result;
        }
        return passed;
    }

    private static bool TestBlankLineParagraphs()
    {
        const string text = "一行目\r\n二行目\r\n\r\n三行目\n\n四行目";
        IReadOnlyList<ProofreadingParagraph> paragraphs =
            ParagraphProofreadingPlanner.SplitParagraphs(text);
        return paragraphs.Count == 3 &&
               paragraphs[0].Text == "一行目\r\n二行目" &&
               paragraphs[1].Text == "三行目" &&
               paragraphs[2].Text == "四行目" &&
               text.Substring(paragraphs[1].Start, paragraphs[1].Length) == "三行目";
    }

    private static bool TestLineFallback()
    {
        IReadOnlyList<ProofreadingParagraph> paragraphs =
            ParagraphProofreadingPlanner.SplitParagraphs("一行目\r\n二行目\n三行目");
        return paragraphs.Select(paragraph => paragraph.Text)
            .SequenceEqual(["一行目", "二行目", "三行目"]);
    }

    private static bool TestChangedParagraphContext()
    {
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial =
            planner.CreateAutomaticPlan("前段落\n\n対象です\n\n後段落");
        if (initial.Requests.Count != 3)
            return false;
        planner.MarkSent(initial);

        ProofreadingPlan unchanged =
            planner.CreateAutomaticPlan("前段落\n\n対象です\n\n後段落");
        if (unchanged.Requests.Count != 0)
            return false;

        ProofreadingPlan changed =
            planner.CreateAutomaticPlan("前段落\n\n対象でs\n\n後段落");
        return changed.Requests.Count == 1 &&
               changed.Requests[0].SourceText == "対象でs" &&
               changed.Requests[0].BeforeContext == "前段落" &&
               changed.Requests[0].AfterContext == "後段落";
    }

    private static bool TestInsertedParagraph()
    {
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial =
            planner.CreateAutomaticPlan("甲\n\n乙\n\n丙");
        planner.MarkSent(initial);

        ProofreadingPlan inserted =
            planner.CreateAutomaticPlan("甲\n\n追加\n\n乙\n\n丙");
        return inserted.Requests.Count == 1 &&
               inserted.Requests[0].SourceText == "追加" &&
               inserted.Requests[0].BeforeContext == "甲" &&
               inserted.Requests[0].AfterContext == "乙";
    }

    private static bool TestLongParagraph()
    {
        string source = new string('あ', 1999) + "😀" + "か\u3099";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan(source);
        return plan.Requests.Count == 2 &&
               plan.Requests.All(request =>
                   request.SourceLength <= ParagraphProofreadingPlanner.MaxTargetLength) &&
               string.Concat(plan.Requests.Select(request => request.SourceText)) == source &&
               !char.IsHighSurrogate(plan.Requests[0].SourceText[^1]) &&
               !char.IsLowSurrogate(plan.Requests[1].SourceText[0]) &&
               plan.Requests[0].AfterContext == plan.Requests[1].SourceText &&
               plan.Requests[1].BeforeContext == plan.Requests[0].SourceText;
    }

    private static bool TestSelection()
    {
        const string text = "前段落\n\n対象の前半。誤り。対象の後半。\n\n後段落";
        const string selected = "誤り";
        int start = text.IndexOf(selected, StringComparison.Ordinal);
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan =
            planner.CreateSelectionPlan(text, start, selected.Length);

        return plan.Requests.Count == 1 &&
               plan.Requests[0].SourceStart == start &&
               plan.Requests[0].SourceText == selected &&
               plan.Requests[0].BeforeContext == "前段落\n\n対象の前半。" &&
               plan.Requests[0].AfterContext == "。対象の後半。\n\n後段落";
    }

    private static bool TestResultMerge()
    {
        const string source = "文s尿です。\n\n誤字アあります。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan(source);
        if (plan.Requests.Count != 2)
            return false;

        GeminiUsage usage = new(1, 1, 0, 0, 2);
        var results = new[]
        {
            (
                plan.Requests[0],
                new GeminiProofreadingResult(
                    "文章です。",
                    DocumentDiff.Create(plan.Requests[0].SourceText, "文章です。"),
                    usage,
                    TimeSpan.Zero,
                    1)),
            (
                plan.Requests[1],
                new GeminiProofreadingResult(
                    "誤字があります。",
                    DocumentDiff.Create(
                        plan.Requests[1].SourceText,
                        "誤字があります。"),
                    usage,
                    TimeSpan.Zero,
                    1)),
        };

        return ProofreadingResultMerger.Merge(plan, results) ==
            "文章です。\n\n誤字があります。";
    }
}
