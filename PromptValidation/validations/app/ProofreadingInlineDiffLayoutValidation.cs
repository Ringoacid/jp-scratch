using JpScratch.Editor;

namespace JpScratch.PromptValidation;

/// <summary>
/// 校正提案のインライン差分表示（要件 3.3.5）の自己テスト。
/// 描画用スナップショットの位置決めロジック（<see cref="ProofreadingInlineDiffLayout"/>）だけを
/// 検証する。generator 本体（WPF の TextFormatter）は取り込まない。APIは呼ばない。
/// </summary>
internal static class ProofreadingInlineDiffLayoutValidation
{
    internal static bool RunSelfTests()
    {
        bool firstInterestedPassed = RunFirstInterestedOffsetTests();
        bool tryFindPassed = RunTryFindAtTests();
        bool hasSuggestionPassed = RunHasSuggestionTextTests();

        bool passed = firstInterestedPassed && tryFindPassed && hasSuggestionPassed;
        Console.WriteLine(
            "インライン差分の位置決め（FirstInterestedOffset・TryFindAt・削除提案の判定）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunFirstInterestedOffsetTests()
    {
        bool passed = true;

        // 1. startOffset 以降で最も小さい Start を返す（リストの並び順に依存しないこと）
        ProofreadingInlineDiff[] unsorted =
        [
            new(Start: 30, Length: 1, Original: "c", Suggestion: "C"),
            new(Start: 10, Length: 1, Original: "a", Suggestion: "A"),
            new(Start: 20, Length: 1, Original: "b", Suggestion: "B"),
        ];
        passed &= Check(
            "順不同リストから最小の開始位置を返す",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(unsorted, 0, 0, 100) == 10,
            ref passed);

        // 2. startOffset と完全に一致する Start を返す（境界が >= であること）。
        //    ここが off-by-one だと要素が一切生成されず、例外も出ない（何も表示されないだけ）。
        passed &= Check(
            "startOffset と完全一致する開始位置を返す",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(unsorted, 10, 0, 100) == 10,
            ref passed);

        // 3. startOffset より前の提案は返さない
        passed &= Check(
            "startOffset より前の提案を返さない",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(unsorted, 11, 0, 100) == 20,
            ref passed);

        // 4. 該当が無ければ -1
        passed &= Check(
            "該当が無ければ -1",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(unsorted, 31, 0, 100) == -1,
            ref passed);

        // 5. 行をまたぐ提案を飛ばして、その次の提案を返す
        //    （行 0〜99 の行終端 100。Start=20, Length=90 は 20+90=110 > 100 で行外）
        ProofreadingInlineDiff[] spanning =
        [
            new(Start: 20, Length: 90, Original: "span", Suggestion: "spam"),
            new(Start: 50, Length: 2, Original: "ok", Suggestion: "good"),
        ];
        passed &= Check(
            "行をまたぐ提案を飛ばして次の提案を返す",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(spanning, 0, 0, 100) == 50,
            ref passed);

        // 6. 行頭より前から始まる提案を飛ばす
        ProofreadingInlineDiff[] beforeLine =
        [
            new(Start: 5, Length: 3, Original: "abc", Suggestion: "def"),
            new(Start: 20, Length: 2, Original: "ok", Suggestion: "good"),
        ];
        passed &= Check(
            "行頭より前から始まる提案を飛ばす",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(beforeLine, 0, 10, 100) == 20,
            ref passed);

        // 7. Length <= 0 の提案を飛ばす
        ProofreadingInlineDiff[] zeroLength =
        [
            new(Start: 10, Length: 0, Original: "", Suggestion: "x"),
            new(Start: 20, Length: 2, Original: "ok", Suggestion: "good"),
        ];
        passed &= Check(
            "長さ0の提案を飛ばす",
            ProofreadingInlineDiffLayout.FirstInterestedOffset(zeroLength, 0, 0, 100) == 20,
            ref passed);

        return passed;
    }

    private static bool RunTryFindAtTests()
    {
        bool passed = true;

        ProofreadingInlineDiff[] diffs =
        [
            new(Start: 10, Length: 4, Original: "abcd", Suggestion: "wxyz"),
            new(Start: 50, Length: 90, Original: "span", Suggestion: "spam"),
        ];

        // 8. 開始位置が完全一致するときだけ true
        bool exact = ProofreadingInlineDiffLayout.TryFindAt(
            diffs, 10, 0, 100, out ProofreadingInlineDiff found);
        passed &= Check(
            "TryFindAt は開始位置の完全一致で true",
            exact && found.Original == "abcd",
            ref passed);

        // 範囲の途中のオフセットでは false（要素の開始位置でしか呼ばれない）
        bool middle = ProofreadingInlineDiffLayout.TryFindAt(
            diffs, 12, 0, 100, out _);
        passed &= Check(
            "TryFindAt は範囲の途中では false",
            !middle,
            ref passed);

        // 9. TryFindAt も行をまたぐ提案を弾く（50+90=140 > 100）
        bool spanning = ProofreadingInlineDiffLayout.TryFindAt(
            diffs, 50, 0, 100, out _);
        passed &= Check(
            "TryFindAt は行をまたぐ提案を弾く",
            !spanning,
            ref passed);

        return passed;
    }

    private static bool RunHasSuggestionTextTests()
    {
        bool passed = true;

        // 10. 削除提案（修正案が空）では false、通常提案では true
        ProofreadingInlineDiff deletion = new(0, 2, "aa", "");
        ProofreadingInlineDiff normal = new(0, 2, "aa", "bb");

        passed &= Check(
            "削除提案では修正後テキストを出さない",
            !ProofreadingInlineDiffLayout.HasSuggestionText(deletion) &&
            ProofreadingInlineDiffLayout.HasSuggestionText(normal),
            ref passed);

        return passed;
    }

    private static bool Check(string label, bool condition, ref bool passed)
    {
        if (condition)
            return true;

        passed = false;
        Console.WriteLine($"    NG {label}");
        return false;
    }
}
