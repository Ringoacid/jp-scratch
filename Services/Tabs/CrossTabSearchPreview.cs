using System.Globalization;

namespace JpScratch.Services;

/// <summary>
/// 全タブ横断検索（要件 3.2.3）のヒット表示に使う純粋関数。
///
/// <see cref="Views.CrossTabSearchWindow"/> は WPF の Window なので自己テストへ取り込めない。
/// 行番号の算出とプレビュー生成は「改行の無い巨大な行」で計算量が壊れやすく、
/// 壊れても見た目には気づきにくいため、ここへ切り出して検証する
/// （<c>ProofreadingInlineDiffLayout</c> / <c>TrayIconStateResolver</c> と同じ方針）。
/// </summary>
internal static class CrossTabSearchPreview
{
    /// <summary>プレビューに載せる最大の書記素クラスタ数。</summary>
    internal const int MaxElements = 120;

    /// <summary>
    /// プレビュー生成のために行頭から走査してよい最大文字数。行頭の空白を読み飛ばす処理は
    /// 「空白だけの巨大な 1 行」に対してヒットごとに行末まで走ってしまうため、上限を設ける。
    /// 上限に当たった場合は空白のまま切り出すことになるが、表示が少し無意味になるだけで済む。
    /// </summary>
    internal const int ScanLimit = 4096;

    /// <summary>各行の開始オフセット（先頭行は 0）。offset → 行番号の二分探索に使う。</summary>
    internal static int[] BuildLineStarts(string text)
    {
        List<int> starts = [0];
        for (int i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
            starts.Add(i + 1);
        return [.. starts];
    }

    /// <summary>
    /// ヒット位置が何行目か、その行の範囲（改行文字を含まない）はどこかを求める。
    ///
    /// 行頭・行末のどちらも索引から O(log n) / O(1) で求める。1 ヒットごとに先頭から数え直したり
    /// <c>text.IndexOf('\n', offset)</c> で行末まで走査したりすると、改行の無い巨大な行
    /// （1 行 1MB のログ等）を 1 文字で検索したときに実質 O(n²) になり UI スレッドが固まる。
    /// </summary>
    internal static (int LineNumber, int Start, int End) LocateLine(
        string text, int[] lineStarts, int offset)
    {
        // Array.BinarySearch は完全一致なら index、無ければ ~挿入位置を返す。
        // offset を含む行は「offset 以下で最大の行頭」なので、挿入位置の 1 つ前。
        int index = Array.BinarySearch(lineStarts, offset);
        if (index < 0) index = ~index - 1;

        int start = lineStarts[index];

        // lineStarts[index + 1] は '\n' の次の位置なので、その 1 つ前が '\n' そのもの。
        int end = index + 1 < lineStarts.Length ? lineStarts[index + 1] - 1 : text.Length;
        if (end > start && text[end - 1] == '\r') end--;

        return (index + 1, start, end);
    }

    /// <summary>
    /// 行の先頭 <see cref="MaxElements"/> 書記素をプレビューとして切り出す。
    ///
    /// 行全体を <c>text[lineStart..lineEnd]</c> で複製してから切り詰めてはいけない。
    /// 改行の無い巨大な行をヒットの数だけ複製することになり、時間も割り当ても O(n²) になる。
    /// 走査も複製も上限ぶんに閉じ込める。
    /// 書記素クラスタ単位で数えるのは、サロゲートペア（絵文字・一部の漢字）や結合文字の
    /// 途中で切ると豆腐表示になるため。
    /// </summary>
    internal static string Build(string text, int lineStart, int lineEnd)
    {
        // 行頭の空白読み飛ばし（元の実装の .Trim() 相当）。走査幅に上限を設ける。
        int scanEnd = (int)Math.Min((long)lineEnd, (long)lineStart + ScanLimit);
        int start = lineStart;
        while (start < scanEnd && char.IsWhiteSpace(text[start])) start++;

        int cursor = start;
        for (int elements = 0; cursor < lineEnd && elements < MaxElements; elements++)
        {
            // GetNextTextElementLength は文字列を作らずにクラスタ長だけを返す。
            // 行末（'\r' / '\n' の手前）を跨がないよう念のため丸める。
            cursor = Math.Min(
                cursor + StringInfo.GetNextTextElementLength(text, cursor),
                lineEnd);
        }

        bool truncated = cursor < lineEnd;

        // 末尾の空白落とし。走査幅は最大 MaxElements 書記素ぶんなので行長に依存しない。
        int end = cursor;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;

        return truncated ? text[start..end] + "…" : text[start..end];
    }
}
