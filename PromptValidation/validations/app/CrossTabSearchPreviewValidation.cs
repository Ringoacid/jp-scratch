using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// 全タブ横断検索のヒット表示（<see cref="CrossTabSearchPreview"/>）の検証。
/// 行末を索引から O(1) で求める形へ書き換えたため、行番号・行範囲・プレビューが
/// 従来と同じ結果になることを確認する（review-result-2026-08-04 P2-3）。
/// </summary>
internal static class CrossTabSearchPreviewValidation
{
    internal static bool RunSelfTests()
    {
        (string Name, Func<bool> Test)[] tests =
        [
            ("行番号と行範囲（LF / CRLF / 末尾行）", TestLocateLine),
            ("プレビュー（前後空白・切り詰め）", TestPreviewTrim),
            ("プレビュー（サロゲートペア・結合文字を割らない）", TestPreviewGraphemeBoundary),
            ("プレビュー（改行なしの巨大な行）", TestPreviewSingleHugeLine),
        ];

        bool passed = true;
        foreach ((string name, Func<bool> test) in tests)
        {
            bool result = test();
            Console.WriteLine($"横断検索プレビュー（{name}）: {(result ? "PASS" : "FAIL")}");
            passed &= result;
        }
        return passed;
    }

    /// <summary>
    /// 行番号は 1 始まり。行範囲には改行文字（CRLF の '\r' を含む）を入れない。
    /// 末尾に改行が無い最終行は文末までが行範囲になる。
    /// </summary>
    private static bool TestLocateLine()
    {
        const string text = "いち\r\nに\nさん";
        int[] starts = CrossTabSearchPreview.BuildLineStarts(text);

        // "いち"(0-1) "\r"(2) "\n"(3) "に"(4) "\n"(5) "さん"(6-7)
        if (!starts.SequenceEqual([0, 4, 6]))
            return false;

        var first = CrossTabSearchPreview.LocateLine(text, starts, 0);
        var firstEnd = CrossTabSearchPreview.LocateLine(text, starts, 1);
        var second = CrossTabSearchPreview.LocateLine(text, starts, 4);
        var third = CrossTabSearchPreview.LocateLine(text, starts, 7);

        return first == (1, 0, 2) &&
               firstEnd == (1, 0, 2) &&
               second == (2, 4, 5) &&
               third == (3, 6, 8) &&
               text[first.Start..first.End] == "いち" &&
               text[second.Start..second.End] == "に" &&
               text[third.Start..third.End] == "さん";
    }

    /// <summary>
    /// 前後の空白は落とし、上限を超えたら「…」を付ける。上限ちょうどでは付けない。
    /// </summary>
    private static bool TestPreviewTrim()
    {
        const string padded = "  \t前後に空白  \t";
        if (CrossTabSearchPreview.Build(padded, 0, padded.Length) != "前後に空白")
            return false;

        string exact = new('あ', CrossTabSearchPreview.MaxElements);
        if (CrossTabSearchPreview.Build(exact, 0, exact.Length) != exact)
            return false;

        string over = new('あ', CrossTabSearchPreview.MaxElements + 1);
        return CrossTabSearchPreview.Build(over, 0, over.Length) == exact + "…";
    }

    /// <summary>
    /// 上限の直前がサロゲートペア・結合文字でも、その途中では切らない（豆腐表示にしない）。
    /// </summary>
    private static bool TestPreviewGraphemeBoundary()
    {
        // 119 文字 + 絵文字（サロゲートペア 2 文字）+ 余り。120 書記素目は絵文字まるごと。
        string withEmoji = new string('あ', CrossTabSearchPreview.MaxElements - 1) + "🙂" + "後続";
        string emojiPreview = CrossTabSearchPreview.Build(withEmoji, 0, withEmoji.Length);
        if (emojiPreview != new string('あ', CrossTabSearchPreview.MaxElements - 1) + "🙂" + "…")
            return false;

        // 120 書記素目が「か」＋結合濁点。分解して「か」だけで切ってはいけない。
        string withCombining = new string('あ', CrossTabSearchPreview.MaxElements - 1) + "が" + "後続";
        string combiningPreview = CrossTabSearchPreview.Build(withCombining, 0, withCombining.Length);
        return combiningPreview ==
               new string('あ', CrossTabSearchPreview.MaxElements - 1) + "が" + "…";
    }

    /// <summary>
    /// 改行の無い巨大な行でも、切り出す長さは上限ぶんに収まる（行全体を複製しない）。
    /// 空白だけの巨大な行でも走査幅の上限で止まり、返る文字列が行長に比例しない。
    /// </summary>
    private static bool TestPreviewSingleHugeLine()
    {
        string huge = new('x', 500_000);
        string preview = CrossTabSearchPreview.Build(huge, 0, huge.Length);
        if (preview != new string('x', CrossTabSearchPreview.MaxElements) + "…")
            return false;

        // 全角空白だけの巨大な行。走査上限に当たった時点で読み飛ばしを止めるため、
        // 返るのは（末尾空白を落とした結果の）短い文字列で、行長には比例しない。
        string blanks = new('　', 500_000);
        return CrossTabSearchPreview.Build(blanks, 0, blanks.Length).Length
               <= CrossTabSearchPreview.MaxElements + 1;
    }
}
