using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 校正漏れ報告（docs/proofreading-ux-fixes-plan.md §9）の操作種別判定と実行ボタン文言のテスト。
/// §9.3 の置換・挿入・削除の判定と、実行不可のケース（選択なし＋修正後空・修正前後同一）を確認する。
/// </summary>
internal static class MissedCorrectionActionValidation
{
    internal static bool RunSelfTests()
    {
        (string Name, Func<bool> Test)[] tests =
        [
            ("置換（選択あり・修正後あり）", TestReplace),
            ("挿入（選択なし・修正後あり）", TestInsert),
            ("削除（選択あり・修正後が空）", TestDelete),
            ("選択なし・修正後が空は実行不可", TestNoSelectionNoCorrection),
            ("修正前と修正後が同一は実行不可", TestIdentical),
            ("実行ボタンの文言", TestButtonText),
            ("プレビュー整形（切り詰め・平坦化・中央省略）", TestPreviewFormatting),
        ];

        bool passed = true;
        foreach ((string name, Func<bool> test) in tests)
        {
            bool result = test();
            Console.WriteLine($"校正漏れ判定（{name}）: {(result ? "PASS" : "FAIL")}");
            passed &= result;
        }
        return passed;
    }

    private static bool TestReplace()
    {
        MissedCorrectionKind kind = MissedCorrectionAction.Determine(
            "負具合", "不具合", out bool allowed);
        return allowed && kind == MissedCorrectionKind.Replace;
    }

    private static bool TestInsert()
    {
        MissedCorrectionKind kind = MissedCorrectionAction.Determine(
            "", "追加分", out bool allowed);
        return allowed && kind == MissedCorrectionKind.Insert;
    }

    private static bool TestDelete()
    {
        MissedCorrectionKind kind = MissedCorrectionAction.Determine(
            "っ", "", out bool allowed);
        return allowed && kind == MissedCorrectionKind.Delete;
    }

    private static bool TestNoSelectionNoCorrection()
    {
        MissedCorrectionAction.Determine("", "", out bool allowed);
        return !allowed;
    }

    private static bool TestIdentical()
    {
        MissedCorrectionAction.Determine("不具合", "不具合", out bool allowed);
        return !allowed;
    }

    private static bool TestButtonText()
        => MissedCorrectionAction.ButtonText(MissedCorrectionKind.Replace) == "修正して校正漏れを記録" &&
           MissedCorrectionAction.ButtonText(MissedCorrectionKind.Insert) == "挿入して校正漏れを記録" &&
           MissedCorrectionAction.ButtonText(MissedCorrectionKind.Delete) == "削除して校正漏れを記録";

    /// <summary>
    /// プレビュー整形（§9）の純粋関数テスト。改行の平坦化・左/右文脈の切り詰め（省略記号）・
    /// 原文の中央省略を確認する。ダイアログ側は <see cref="JpScratch.Views.MissedCorrectionDialog"/> が
    /// これらの結果を <c>Run</c> で組み立てる。
    /// </summary>
    private static bool TestPreviewFormatting()
    {
        // 改行（CRLF・LF）は空白へ平坦化される。
        if (MissedCorrectionPreview.FlattenForPreview("前\n中\r\n後") != "前 中 後")
            return false;

        // 左文脈は末尾基準で切り詰め、省略側に「…」を付ける。
        string longContext = new string('あ', 30);
        string left = MissedCorrectionPreview.TruncateLeft(longContext, 24);
        if (left.Length != 25 || !left.StartsWith("…", StringComparison.Ordinal) ||
            left[1..] != longContext[^24..])
        {
            return false;
        }

        // 右文脈は先頭基準で切り詰め、省略側に「…」を付ける。
        string right = MissedCorrectionPreview.TruncateRight(longContext, 24);
        if (right.Length != 25 || !right.EndsWith("…", StringComparison.Ordinal) ||
            right[..^1] != longContext[..24])
        {
            return false;
        }

        // 収まる範囲はそのまま。
        if (MissedCorrectionPreview.TruncateLeft("短い", 24) != "短い" ||
            MissedCorrectionPreview.TruncateRight("短い", 24) != "短い")
        {
            return false;
        }

        // 原文（選択範囲）は前後を残して中央を「…」で省略する。
        string original = new string('か', 60);
        string truncated = MissedCorrectionPreview.TruncateOriginal(original);
        return truncated.Length == 26 && // 15 + 1 + 10
               truncated[15] == '…' &&
               truncated[..15] == original[..15] &&
               truncated[16..] == original[^10..];
    }
}
