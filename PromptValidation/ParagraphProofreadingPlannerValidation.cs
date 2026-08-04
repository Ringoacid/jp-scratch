using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class ParagraphProofreadingPlannerValidation
{
    internal static bool RunSelfTests()
    {
        (string Name, Func<bool> Test)[] tests =
        [
            ("空行区切り", TestBlankLineParagraphs),
            ("空行なし複数行は1ブロック", TestNoBlankLineSingleBlock),
            ("文末改行・空行の不変性", TestTrailingLineBreakInvariance),
            ("15行は1リクエスト", TestFifteenLinesSingleRequest),
            ("空行1つで2ブロック", TestOneBlankLineTwoBlocks),
            ("空行だけの文書は0リクエスト", TestBlankOnlyDocument),
            ("先頭の空行は無視", TestLeadingBlankLines),
            ("2,000文字境界の分割", TestBoundarySplit),
            ("変更段落と前後文脈", TestChangedParagraphContext),
            ("文脈の長さ上限（対象に隣接する側を残す）", TestContextLengthCap),
            ("段落挿入時の重複抑止", TestInsertedParagraph),
            ("2,000文字分割", TestLongParagraph),
            ("選択範囲", TestSelection),
            ("複数応答の全文統合", TestResultMerge),
            ("中断時の部分送信済み記録", TestPartialMarkSent),
            ("同一段落の一部だけ未送信（パート単位）", TestPartialPartMarkSent),
            ("許可の引き継ぎ", TestCarryForwardAppliedEdit),
            ("引き継ぎ後の別段落編集", TestCarryForwardThenEditAnother),
            ("段落数が変わる場合は引き継がない", TestCarryForwardRejectedOnCountChange),
            ("未送信段落は引き継がない", TestCarryForwardRejectedWhenUnsent),
            ("適用段落以外が変わっていたら引き継がない", TestCarryForwardRejectedOnOtherChange),
            ("オフセットが段落外なら引き継がない", TestCarryForwardRejectedOnOffsetOutside),
            ("連続適用（同一段落2回）", TestCarryForwardSequentialSameParagraph),
            ("連続適用（別段落2箇所）", TestCarryForwardSequentialDifferentParagraphs),
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

    private static bool TestNoBlankLineSingleBlock()
    {
        // 空行が無い複数行は一つの文章ブロックとして扱う
        // （proofreading-ux-fixes-plan.md §6.2。従来の「改行単位フォールバック」は廃止）。
        IReadOnlyList<ProofreadingParagraph> paragraphs =
            ParagraphProofreadingPlanner.SplitParagraphs("一行目\r\n二行目\n三行目");
        return paragraphs.Count == 1 &&
               paragraphs[0].Text == "一行目\r\n二行目\n三行目" &&
               paragraphs[0].Start == 0;
    }

    /// <summary>
    /// 文末の改行・空行は校正単位数（リクエスト数）に影響させない（proofreading-ux-fixes-plan.md §6.4）。
    /// 同じ本文について、文末改行なし・LF・CRLF・空行1つ・空行複数のすべてでリクエスト数が一致する。
    /// </summary>
    private static bool TestTrailingLineBreakInvariance()
    {
        string[] variants =
        [
            "一行目\r\n二行目\n三行目",
            "一行目\r\n二行目\n三行目\n",
            "一行目\r\n二行目\n三行目\r\n",
            "一行目\r\n二行目\n三行目\n\n",
            "一行目\r\n二行目\n三行目\n\n\n",
        ];

        int? expected = null;
        foreach (string variant in variants)
        {
            var planner = new ParagraphProofreadingPlanner();
            ProofreadingPlan plan = planner.CreateAutomaticPlan(variant);
            if (expected is null)
                expected = plan.Requests.Count;
            else if (plan.Requests.Count != expected.Value)
                return false;
        }

        return expected == 1;
    }

    private static bool TestFifteenLinesSingleRequest()
    {
        var planner = new ParagraphProofreadingPlanner();
        string text = string.Join("\n", Enumerable.Range(1, 15).Select(i => $"行{i}"));
        ProofreadingPlan plan = planner.CreateAutomaticPlan(text);
        return plan.Requests.Count == 1 &&
               plan.Paragraphs.Count == 1 &&
               plan.Requests[0].SourceText == text;
    }

    private static bool TestOneBlankLineTwoBlocks()
    {
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan("前段\n\n後段");
        return plan.Paragraphs.Count == 2 &&
               plan.Requests.Count == 2 &&
               plan.Requests[0].SourceText == "前段" &&
               plan.Requests[1].SourceText == "後段";
    }

    private static bool TestBlankOnlyDocument()
    {
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan("\n\n\n");
        return plan.Paragraphs.Count == 0 && plan.Requests.Count == 0;
    }

    private static bool TestLeadingBlankLines()
    {
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan("\n\n本文");
        return plan.Paragraphs.Count == 1 &&
               plan.Requests.Count == 1 &&
               plan.Requests[0].SourceText == "本文";
    }

    /// <summary>
    /// 2,000文字を超えるブロックだけが追加分割される（proofreading-ux-fixes-plan.md §6.2）。
    /// 2,000文字ちょうどは分割せず、2,001文字以上は書記素を壊さずに分割する。
    /// </summary>
    private static bool TestBoundarySplit()
    {
        var plannerWithin = new ParagraphProofreadingPlanner();
        if (plannerWithin.CreateAutomaticPlan(new string('あ', 2000)).Requests.Count != 1)
            return false;

        // 2,000 + 1 の「あ」に、結合濁点つき「が」を足す。分割は書記素境界で行われなければならない。
        string over = new string('あ', 2001) + "か\u3099";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan(over);
        return plan.Requests.Count == 2 &&
               plan.Requests.All(request =>
                   request.SourceLength <= ParagraphProofreadingPlanner.MaxTargetLength) &&
               !char.IsHighSurrogate(plan.Requests[0].SourceText[^1]) &&
               !char.IsLowSurrogate(plan.Requests[1].SourceText[0]) &&
               string.Concat(plan.Requests.Select(request => request.SourceText)) == over;
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

    /// <summary>
    /// 巨大な段落を文脈として添付するとき、上限まで切り詰めたうえで
    /// **校正対象に隣接する側**を残すことを確かめる。
    /// 上限が無いと「5万文字のログを貼った下に短い段落を書く」だけで、数十文字の校正のたびに
    /// その全文が毎回入力トークンとして課金される。
    /// </summary>
    private static bool TestContextLengthCap()
    {
        const int Cap = ParagraphProofreadingPlanner.MaxContextLength;

        // 前段落は末尾が、後段落は先頭が対象に隣接する。切り詰めても隣接部分が残ること。
        string before = new string('あ', 5000) + "前段落の末尾";
        string after = "後段落の先頭" + new string('い', 5000);
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan(
            $"{before}\n\n対象です\n\n{after}");

        ProofreadingRequest? target = plan.Requests
            .FirstOrDefault(request => request.SourceText == "対象です");
        if (target?.BeforeContext is not { } beforeContext ||
            target.AfterContext is not { } afterContext)
        {
            return false;
        }

        return beforeContext.Length <= Cap &&
               afterContext.Length <= Cap &&
               beforeContext.EndsWith("前段落の末尾", StringComparison.Ordinal) &&
               afterContext.StartsWith("後段落の先頭", StringComparison.Ordinal) &&
               // 上限以下の文脈は 1 文字も変えない。
               plan.Requests.Any(request =>
                   request.SourceText == "対象です" &&
                   request.BeforeContext!.Length > Cap - 10);
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

    /// <summary>
    /// 校正ループが途中で中断したときの部分送信済み記録（MarkSent(plan, completedCount)）。
    /// 完了済みの段落は本文が変わっていなければ再送されず、未送信の段落は再送対象に残る。
    /// 0件で呼んだ場合は何も変更しない（変更なしと判定された段落は送信済みのまま）。
    /// 回帰テストとしては「一度 MarkSent(plan) で全件記録 → 別の本文で部分 MarkSent」の順序で
    /// 呼ぶ。部分 MarkSent を「今回完了した段落だけ」で丸ごと置き換える実装だと、前回送信済みで
    /// 今回のプランに現れない段落（＝変更なし）が未送信に戻り、次回再送＝二重課金になる。
    /// </summary>
    private static bool TestPartialMarkSent()
    {
        // 1回目の実行で全段落を送信済みにする（累積状態を作る）。
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial =
            planner.CreateAutomaticPlan("甲です。\n\n乙です。\n\n丙です。");
        if (initial.Requests.Count != 3)
            return false;
        planner.MarkSent(initial);

        // 甲と丙だけを編集した状態でプランを立て直す。乙は未変更＝プランに現れない。
        ProofreadingPlan edited =
            planner.CreateAutomaticPlan("甲です！\n\n乙です。\n\n丙です！");
        if (edited.Requests.Count != 2 || edited.Requests[0].SourceText != "甲です！")
            return false;

        // 甲（1件目）の送信だけが完了して中断した状況を再現する。
        planner.MarkSent(edited, completedRequestCount: 1);

        // 乙は編集していないので再送されない（累積状態が消えるバグの再発防止）。
        // 丙は未送信のまま再送対象に残る。
        ProofreadingPlan replan =
            planner.CreateAutomaticPlan("甲です！\n\n乙です。\n\n丙です！");
        bool partialWorks = replan.Requests.Count == 1 &&
                            replan.Requests[0].SourceText == "丙です！";

        // 0件（まだ1件も送信していない）なら、変更なしと判定された段落だけが送信済みのまま。
        var plannerNone = new ParagraphProofreadingPlanner();
        ProofreadingPlan planNone =
            plannerNone.CreateAutomaticPlan("甲です。\n\n乙です。");
        plannerNone.MarkSent(planNone, completedRequestCount: 0);
        ProofreadingPlan replanNone =
            plannerNone.CreateAutomaticPlan("甲です。\n\n乙です。");
        bool noneWorks = replanNone.Requests.Count == 2;

        return partialWorks && noneWorks;
    }

    /// <summary>
    /// 2,000文字を超えて複数リクエストへ分割された「同一段落」で、一部のパートだけが
    /// 未送信になるケース（review-result-2026-08-04 P2-2）。
    ///
    /// 送信済みを段落単位で持つと、前半パートが成功して後半パートが API エラーになったとき、
    /// 段落まるごとが未送信へ戻る。次回の校正で課金済みの前半も再送され、二重課金になる。
    /// 集合オーバーロード（本文編集・API 失敗による部分保持）と件数オーバーロード
    /// （ループ中断）の両方で、後半パートだけが再送対象に残ることを確認する。
    /// </summary>
    private static bool TestPartialPartMarkSent()
    {
        // 3,000文字の 1 段落 → 2,000 + 1,000 の 2 パートへ分割される。
        string firstPart = new('あ', ParagraphProofreadingPlanner.MaxTargetLength);
        string secondPart = new('い', 1000);
        string text = firstPart + secondPart;

        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan plan = planner.CreateAutomaticPlan(text);
        if (plan.Requests.Count != 2 ||
            plan.Requests[0].ParagraphIndex != plan.Requests[1].ParagraphIndex ||
            plan.Requests[0].PartIndex != 0 ||
            plan.Requests[1].PartIndex != 1)
        {
            return false;
        }

        // 前半（パート0）は成功、後半（パート1）だけが未送信。
        planner.MarkSent(plan, new HashSet<(int, int)> { (0, 1) });

        ProofreadingPlan replan = planner.CreateAutomaticPlan(text);
        bool bySetWorks = replan.Requests.Count == 1 &&
                          replan.Requests[0].PartIndex == 1 &&
                          replan.Requests[0].SourceText == secondPart;

        // 件数オーバーロード（1件目まで完了して中断）でも同じでなければならない。
        var byCount = new ParagraphProofreadingPlanner();
        ProofreadingPlan countPlan = byCount.CreateAutomaticPlan(text);
        byCount.MarkSent(countPlan, completedRequestCount: 1);
        ProofreadingPlan countReplan = byCount.CreateAutomaticPlan(text);
        bool byCountWorks = countReplan.Requests.Count == 1 &&
                            countReplan.Requests[0].PartIndex == 1 &&
                            countReplan.Requests[0].SourceText == secondPart;

        // 全パート送信済みなら再送は 0 件（パート単位化で取りこぼしが出ていないことの確認）。
        var allSent = new ParagraphProofreadingPlanner();
        allSent.MarkSent(allSent.CreateAutomaticPlan(text));
        bool allSentWorks = allSent.CreateAutomaticPlan(text).Requests.Count == 0;

        return bySetWorks && byCountWorks && allSentWorks;
    }

    /// <summary>
    /// 「許可」による本文置換を送信済みハッシュへ引き継ぐ。適用後の全文でプランを立て直しても
    /// 該当段落が再送されない（0件）ことを確認する。本文は3段落、中央を1文字だけ書き換えた
    /// after を渡す。appliedOffset は適用前の座標（中央段落の先頭 = 6）。
    /// </summary>
    private static bool TestCarryForwardAppliedEdit()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string after = "甲です。\n\n乙です！\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        if (initial.Requests.Count != 3)
            return false;
        planner.MarkSent(initial);

        planner.CarryForwardAppliedEdit(before, after, appliedOffset: 6);
        ProofreadingPlan replan = planner.CreateAutomaticPlan(after);
        return replan.Requests.Count == 0;
    }

    /// <summary>
    /// 引き継ぎの後、別の段落を編集したときはその段落だけが再送対象になる
    /// （引き継いだ段落は再送されない）。
    /// </summary>
    private static bool TestCarryForwardThenEditAnother()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string afterAccept = "甲です。\n\n乙です！\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        planner.MarkSent(initial);
        planner.CarryForwardAppliedEdit(before, afterAccept, appliedOffset: 6);
        if (planner.CreateAutomaticPlan(afterAccept).Requests.Count != 0)
            return false;

        const string afterEdit = "甲です。\n\n乙です！\n\n丙です！";
        ProofreadingPlan replan = planner.CreateAutomaticPlan(afterEdit);
        return replan.Requests.Count == 1 &&
               replan.Requests[0].SourceText == "丙です！";
    }

    /// <summary>
    /// 適用前後で段落数が変わる（空行の挿入で1段落が2つに割れる）場合は引き継がず、
    /// 適用後のプランに該当段落が再送対象として残る。
    /// </summary>
    private static bool TestCarryForwardRejectedOnCountChange()
    {
        const string before = "甲です。\n\n乙です。乙です。\n\n丙です。";
        const string after = "甲です。\n\n乙です。\n\n乙です。\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        if (initial.Requests.Count != 3)
            return false;
        planner.MarkSent(initial);

        // before の中央段落（Start=6）に適用した想定。after では段落数が4へ増える。
        planner.CarryForwardAppliedEdit(before, after, appliedOffset: 6);

        ProofreadingPlan replan = planner.CreateAutomaticPlan(after);
        return replan.Requests.Count == 2 &&
               replan.Requests.Any(request => request.SourceText == "乙です。");
    }

    /// <summary>
    /// MarkSent を一度も呼んでいない（＝手動の選択範囲校正相当）状態では引き継がず、
    /// プランが従来どおり全段落ぶん出る（未送信の段落を送信済みに化けさせない）。
    /// </summary>
    private static bool TestCarryForwardRejectedWhenUnsent()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string after = "甲です。\n\n乙です！\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();

        planner.CarryForwardAppliedEdit(before, after, appliedOffset: 6);

        ProofreadingPlan replan = planner.CreateAutomaticPlan(after);
        return replan.Requests.Count == 3 &&
               replan.Requests.Any(request => request.SourceText == "乙です！");
    }

    /// <summary>
    /// 適用段落以外の段落も変わっている場合は引き継がない（想定外の状態）。
    /// 適用段落は再送対象のまま残る。
    /// </summary>
    private static bool TestCarryForwardRejectedOnOtherChange()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string after = "甲です！\n\n乙です！\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        planner.MarkSent(initial);

        planner.CarryForwardAppliedEdit(before, after, appliedOffset: 6);

        ProofreadingPlan replan = planner.CreateAutomaticPlan(after);
        return replan.Requests.Count == 2 &&
               replan.Requests.Any(request => request.SourceText == "乙です！");
    }

    /// <summary>
    /// appliedOffset がどの段落にも含まれない（空行位置など）場合は引き継がない。
    /// 該当段落は再送対象のまま残る。
    /// </summary>
    private static bool TestCarryForwardRejectedOnOffsetOutside()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string after = "甲です。\n\n乙です！\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        planner.MarkSent(initial);

        // オフセット5は「甲です。」と「乙です。」の間の空行（どの段落にも属さない）。
        planner.CarryForwardAppliedEdit(before, after, appliedOffset: 5);

        ProofreadingPlan replan = planner.CreateAutomaticPlan(after);
        return replan.Requests.Count == 1 &&
               replan.Requests[0].SourceText == "乙です！";
    }

    /// <summary>
    /// 一括許可（AcceptAllProposals）のループを再現する。同一段落に2回連続で適用しても
    /// （前回の after を次回の before として CarryForwardAppliedEdit を連続呼び出し）、
    /// 最終的なプランは0件（引き継ぎが失われず再送されない）。
    /// </summary>
    private static bool TestCarryForwardSequentialSameParagraph()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string after1 = "甲です。\n\n乙です！\n\n丙です。";
        const string after2 = "甲です。\n\n乙です！！\n\n丙です。";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        planner.MarkSent(initial);

        // 1回目の適用（中央段落の先頭 = 6）。
        planner.CarryForwardAppliedEdit(before, after1, appliedOffset: 6);
        // 2回目の適用（同じ段落。適用後も先頭 = 6）。
        planner.CarryForwardAppliedEdit(after1, after2, appliedOffset: 6);

        return planner.CreateAutomaticPlan(after2).Requests.Count == 0;
    }

    /// <summary>
    /// 一括許可（AcceptAllProposals）のループを再現する。別の段落（P1 → P3）へ順に適用しても、
    /// 最終的なプランは0件（各適用の引き継ぎが保持される）。
    /// </summary>
    private static bool TestCarryForwardSequentialDifferentParagraphs()
    {
        const string before = "甲です。\n\n乙です。\n\n丙です。";
        const string after1 = "甲です。\n\n乙です！\n\n丙です。";
        const string after2 = "甲です。\n\n乙です！\n\n丙です！";
        var planner = new ParagraphProofreadingPlanner();
        ProofreadingPlan initial = planner.CreateAutomaticPlan(before);
        planner.MarkSent(initial);

        // P1（offset 6）→ P3（offset 12）の順に適用。
        planner.CarryForwardAppliedEdit(before, after1, appliedOffset: 6);
        planner.CarryForwardAppliedEdit(after1, after2, appliedOffset: 12);

        return planner.CreateAutomaticPlan(after2).Requests.Count == 0;
    }
}
