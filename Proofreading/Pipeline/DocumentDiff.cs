using System.Globalization;
using System.Text;

namespace JpScratch.Proofreading;

internal sealed record DocumentChange(
    int Start,
    int Length,
    string Original,
    string Suggestion,
    string LeftContext,
    string RightContext);

internal sealed record DocumentDiffResult(
    bool Accepted,
    string? RejectionReason,
    IReadOnlyList<DocumentChange> Changes,
    int ChangedTextElements,
    double ChangedRatio);

/// <summary>
/// 修正前後の全文から、互いに重ならない局所的な置換を生成する。
/// Start/Length は AvalonEdit と同じ UTF-16 オフセットである。
/// </summary>
internal static class DocumentDiff
{
    private const int ContextElements = 20;
    private const int MaxChanges = 50;
    private const int MaxSingleChangeElements = 200;
    private const int MaxEditDistance = 500;
    // 段落単位で検証済みの差分を全文へ統合した再検証（relaxedGlobalLimits=true）では、段落ごとの
    // 編集距離（最大 MaxEditDistance）が変更段落数ぶん合算されることが正当にあり得る。ただし無制限
    // （n+m）にすると Myers の trace が d ごとに積まれる O(D²) メモリとなり、長文で UI スレッドの
    // フリーズ／OOM に直結する。緩和モードでもこの倍数を上限にし、超えたら従来どおり
    // 「安全検査に失敗」として穏やかに破棄する。
    private const int RelaxedEditDistanceMultiplier = 4;
    private const int RatioAllowanceElements = 20;
    private const double MaxChangedRatio = 0.20;

    internal static DocumentDiffResult Create(
        string source,
        string corrected,
        bool relaxedGlobalLimits = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(corrected);

        // モデルが付け外ししやすい末尾改行だけは校正提案にしない。
        string sourceForDiff = TrimTerminalLineBreaks(source);
        string correctedForDiff = TrimTerminalLineBreaks(corrected);

        List<TextElement> sourceElements = Tokenize(sourceForDiff);
        List<TextElement> correctedElements = Tokenize(correctedForDiff);
        IReadOnlyList<DiffOperation>? operations = MyersDiff(
            sourceElements, correctedElements, relaxedGlobalLimits);
        if (operations is null)
        {
            return new DocumentDiffResult(
                false,
                $"差分の編集距離が大きすぎます（上限 {MaxEditDistance}）",
                [],
                MaxEditDistance + 1,
                1);
        }
        List<RawHunk> hunks = MergeNearbyWordHunks(
            sourceElements,
            BuildHunks(operations));

        List<DocumentChange> changes = [];
        // 直前の変更が消費した最後の書記素の index。純粋な挿入は TextAnchor で監視できるよう
        // 隣接する 1 書記素を借りて置換へ変換するが、借りる方向を調停しないと
        // 「文頭への挿入」と「その直後への挿入」が同じ書記素を奪い合って範囲が重複し、
        // 正しい校正結果が丸ごと破棄されて課金だけが残る。
        int consumedThroughElement = -1;
        foreach (RawHunk hunk in hunks)
        {
            DocumentChange? change = ToAnchoredChange(
                sourceForDiff,
                sourceElements,
                hunk,
                consumedThroughElement,
                out int lastConsumedElement);
            if (change is not null)
            {
                changes.Add(change);
                consumedThroughElement = lastConsumedElement;
            }
        }

        int changedElements = hunks.Sum(
            hunk => Math.Max(hunk.Deleted.Count, hunk.Inserted.Count));
        int denominator = Math.Max(sourceElements.Count, correctedElements.Count);
        double changedRatio = denominator == 0 ? 0 : (double)changedElements / denominator;

        string? rejection = ValidateSafety(
            sourceForDiff,
            correctedForDiff,
            changes,
            hunks,
            changedElements,
            changedRatio,
            relaxedGlobalLimits);

        return new DocumentDiffResult(
            rejection is null,
            rejection,
            rejection is null ? changes : [],
            changedElements,
            changedRatio);
    }

    internal static string Apply(string source, IReadOnlyList<DocumentChange> changes)
    {
        string result = source;
        foreach (DocumentChange change in changes.OrderByDescending(change => change.Start))
        {
            if (change.Start < 0 ||
                change.Length < 0 ||
                change.Start + change.Length > result.Length ||
                !string.Equals(
                    result.Substring(change.Start, change.Length),
                    change.Original,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"提案位置 {change.Start}:{change.Length} が原文と一致しません。");
            }

            result = result.Remove(change.Start, change.Length)
                .Insert(change.Start, change.Suggestion);
        }

        return result;
    }

    private static string? ValidateSafety(
        string source,
        string corrected,
        IReadOnlyList<DocumentChange> changes,
        IReadOnlyList<RawHunk> hunks,
        int changedElements,
        double changedRatio,
        bool relaxedGlobalLimits)
    {
        // 段落・分割単位で検証済みの差分を全文へ統合した結果の再検証（relaxedGlobalLimits=true）では、
        // 段落ごとの上限（MaxChanges / 編集距離）を合計が超えることが正当にあり得る
        // （20段落 × 各3修正 = 60箇所など）。各リクエストは送信時に既に上限を通過しているため、
        // ここで同じ上限を掛け直すと有効な結果が丸ごと破棄される。統合の再検証で見るべきは
        // 「1箇所の変更が過大でない」「範囲が重複しない」「適用して修正版を再現できる」だけ。
        // 緩和するのは件数上限と編集距離の2つだけで、変更比率のガードは外さない: 各段落が個別に
        // 20%以下を通っているなら全文の合計も（わずかな誤差は別として）20%以下に収まり、
        // 正当な結果を落とさない。統合がおかしくなったケース（段落外への変更が混入した等）だけを
        // 拾える無料の保険になる。
        if (!relaxedGlobalLimits && hunks.Count > MaxChanges)
            return $"変更箇所が多すぎます（{hunks.Count} > {MaxChanges}）";

        if (hunks.Any(
                hunk => Math.Max(hunk.Deleted.Count, hunk.Inserted.Count) >
                    MaxSingleChangeElements))
        {
            return $"1箇所の変更が大きすぎます（上限 {MaxSingleChangeElements} 書記素）";
        }

        if (changedElements > RatioAllowanceElements && changedRatio > MaxChangedRatio)
        {
            return
                $"全文に対する変更量が大きすぎます" +
                $"（{changedElements} 書記素、{changedRatio:P1}）";
        }

        for (int index = 1; index < changes.Count; index++)
        {
            DocumentChange previous = changes[index - 1];
            DocumentChange current = changes[index];
            if (previous.Start + previous.Length > current.Start)
                return "変更範囲が重複しています";
        }

        string applied;
        try
        {
            applied = Apply(source, changes);
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        if (!string.Equals(
                NormalizeLineEndings(applied),
                NormalizeLineEndings(corrected),
                StringComparison.Ordinal))
        {
            return "提案をすべて適用しても修正版全文を再現できません";
        }

        return null;
    }

    /// <param name="consumedThroughElement">
    /// 直前の変更が消費した最後の書記素 index（無ければ -1）。純粋な挿入が「直前の書記素」を
    /// 借りようとしたとき、そこが既に使われていれば代わりに「直後の書記素」を借りる。
    /// </param>
    /// <param name="lastConsumedElement">この変更が消費した最後の書記素 index。</param>
    private static DocumentChange? ToAnchoredChange(
        string source,
        IReadOnlyList<TextElement> sourceElements,
        RawHunk hunk,
        int consumedThroughElement,
        out int lastConsumedElement)
    {
        lastConsumedElement = consumedThroughElement;

        int startElement = hunk.SourceStart;
        int sourceElementCount = hunk.Deleted.Count;
        string original = Join(hunk.Deleted);
        string suggestion = Join(hunk.Inserted);

        // 挿入だけでは TextAnchor の有効範囲を監視できないため、隣接する
        // 1書記素を両側へ含めた置換に変換する。
        if (sourceElementCount == 0)
        {
            if (sourceElements.Count == 0)
                return null;

            if (startElement > 0 && startElement - 1 > consumedThroughElement)
            {
                // 直前の書記素を借りる（既定）。「その書記素＋挿入文字列」への置換になる。
                TextElement borrowed = sourceElements[startElement - 1];
                startElement--;
                sourceElementCount = 1;
                original = borrowed.Value;
                suggestion = borrowed.Value + suggestion;
            }
            else if (startElement < sourceElements.Count)
            {
                // 文頭、または直前の書記素を先行する変更が既に消費している。直後の書記素を借りて
                // 「挿入文字列＋その書記素」への置換にする。挿入 hunk の直後は必ず Equal 要素
                // （BuildHunks が Equal で hunk を閉じる）なので、次の hunk とは重複しない。
                TextElement borrowed = sourceElements[startElement];
                sourceElementCount = 1;
                original = borrowed.Value;
                suggestion += borrowed.Value;
            }
            else
            {
                // 文末への挿入で、直前の書記素も先行する変更に取られている。借りる先が無い。
                // ここで null を返すと Apply による再現検査が落ち、この実行の結果は
                // 「安全検査に失敗」として穏やかに破棄される（本文は変更しない）。
                return null;
            }
        }

        lastConsumedElement = startElement + sourceElementCount - 1;

        int start = sourceElements[startElement].Start;
        TextElement last = sourceElements[startElement + sourceElementCount - 1];
        int length = last.Start + last.Length - start;

        string leftContext = Join(
            sourceElements.Skip(Math.Max(0, startElement - ContextElements))
                .Take(Math.Min(ContextElements, startElement)));
        int rightStart = startElement + sourceElementCount;
        string rightContext = Join(
            sourceElements.Skip(rightStart).Take(ContextElements));

        return new DocumentChange(
            start,
            length,
            original,
            suggestion,
            leftContext,
            rightContext);
    }

    private static List<RawHunk> BuildHunks(IReadOnlyList<DiffOperation> operations)
    {
        List<RawHunk> hunks = [];
        int sourceIndex = 0;
        RawHunk? current = null;

        foreach (DiffOperation operation in operations)
        {
            if (operation.Kind == DiffKind.Equal)
            {
                if (current is not null)
                {
                    hunks.Add(current);
                    current = null;
                }

                sourceIndex++;
                continue;
            }

            current ??= new RawHunk(sourceIndex, [], []);
            if (operation.Kind == DiffKind.Delete)
            {
                current.Deleted.Add(operation.Element);
                sourceIndex++;
            }
            else
            {
                current.Inserted.Add(operation.Element);
            }
        }

        if (current is not null)
            hunks.Add(current);
        return hunks;
    }

    private static List<RawHunk> MergeNearbyWordHunks(
        IReadOnlyList<TextElement> sourceElements,
        IReadOnlyList<RawHunk> hunks)
    {
        if (hunks.Count < 2)
            return hunks.ToList();

        List<RawHunk> merged = [];
        RawHunk current = Clone(hunks[0]);
        for (int index = 1; index < hunks.Count; index++)
        {
            RawHunk next = hunks[index];
            int currentEnd = current.SourceStart + current.Deleted.Count;
            int gap = next.SourceStart - currentEnd;
            IReadOnlyList<TextElement> gapElements = sourceElements
                .Skip(currentEnd)
                .Take(Math.Max(0, gap))
                .ToArray();

            // 1語の中で複数文字が変わった場合、機械的な最小差分を
            // バラバラの提案にせず、最大2書記素の共通部分ごとまとめる。
            // ただし連結の結果が1箇所の上限（MaxSingleChangeElements）を超える場合は
            // 連結しない。連結しっぱなしだと、日本語（全文字が語要素）で誤字の多い段落が
            // 「1箇所の変更が大きすぎます」として丸ごと拒否されるため。
            bool merge = gap is >= 0 and <= 2 &&
                gapElements.All(IsWordElement) &&
                (current.Deleted.Count > 0 || current.Inserted.Count > 0) &&
                (next.Deleted.Count > 0 || next.Inserted.Count > 0) &&
                Math.Max(
                    current.Deleted.Count + gapElements.Count + next.Deleted.Count,
                    current.Inserted.Count + gapElements.Count + next.Inserted.Count) <=
                    MaxSingleChangeElements;

            if (merge)
            {
                current.Deleted.AddRange(gapElements);
                current.Deleted.AddRange(next.Deleted);
                current.Inserted.AddRange(gapElements);
                current.Inserted.AddRange(next.Inserted);
            }
            else
            {
                merged.Add(current);
                current = Clone(next);
            }
        }

        merged.Add(current);
        return merged;
    }

    private static bool IsWordElement(TextElement element)
    {
        if (element.Value.Length == 0)
            return false;

        UnicodeCategory category =
            CharUnicodeInfo.GetUnicodeCategory(element.Value, 0);
        return category is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.LetterNumber or
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark;
    }

    private static RawHunk Clone(RawHunk hunk) =>
        new(hunk.SourceStart, [.. hunk.Deleted], [.. hunk.Inserted]);

    private static IReadOnlyList<DiffOperation>? MyersDiff(
        IReadOnlyList<TextElement> source,
        IReadOnlyList<TextElement> corrected,
        bool unlimited)
    {
        int n = source.Count;
        int m = corrected.Count;
        // 通常は変更量の上限（MaxEditDistance）で打ち切る。段落単位で検証済みの差分を全文へ
        // 戻した統合結果の再検証（unlimited=true）では、段落ごとの上限を合計が超えることが
        // 正当にあり得るため、MaxEditDistance の倍数まで許す。無制限（n+m）にはしない
        // （O(D²) メモリの Myers で長文がフリーズ／OOM するため）。
        int max = Math.Min(n + m, unlimited
            ? MaxEditDistance * RelaxedEditDistanceMultiplier
            : MaxEditDistance);
        Dictionary<int, int> v = new() { [1] = 0 };
        List<Dictionary<int, int>> trace = [];

        for (int d = 0; d <= max; d++)
        {
            trace.Add(new Dictionary<int, int>(v));
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d ||
                    (k != d && Get(v, k - 1) < Get(v, k + 1))
                        ? Get(v, k + 1)
                        : Get(v, k - 1) + 1;
                int y = x - k;

                while (x < n && y < m &&
                       string.Equals(
                           source[x].ComparisonValue,
                           corrected[y].ComparisonValue,
                           StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[k] = x;
                if (x >= n && y >= m)
                    return Backtrack(source, corrected, trace, d);
            }
        }

        return null;
    }

    private static IReadOnlyList<DiffOperation> Backtrack(
        IReadOnlyList<TextElement> source,
        IReadOnlyList<TextElement> corrected,
        IReadOnlyList<Dictionary<int, int>> trace,
        int distance)
    {
        int x = source.Count;
        int y = corrected.Count;
        List<DiffOperation> reversed = [];

        for (int d = distance; d >= 0; d--)
        {
            Dictionary<int, int> v = trace[d];
            int k = x - y;
            int previousK = k == -d ||
                (k != d && Get(v, k - 1) < Get(v, k + 1))
                    ? k + 1
                    : k - 1;
            int previousX = Get(v, previousK);
            int previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                reversed.Add(new DiffOperation(DiffKind.Equal, source[x - 1]));
                x--;
                y--;
            }

            if (d == 0)
                break;

            if (x == previousX)
            {
                reversed.Add(new DiffOperation(DiffKind.Insert, corrected[y - 1]));
                y--;
            }
            else
            {
                reversed.Add(new DiffOperation(DiffKind.Delete, source[x - 1]));
                x--;
            }
        }

        reversed.Reverse();
        return reversed;
    }

    private static int Get(IReadOnlyDictionary<int, int> values, int key) =>
        values.TryGetValue(key, out int value) ? value : 0;

    private static List<TextElement> Tokenize(string text)
    {
        List<TextElement> elements = [];
        for (int index = 0; index < text.Length;)
        {
            if (text[index] == '\r' || text[index] == '\n')
            {
                int length = text[index] == '\r' &&
                    index + 1 < text.Length &&
                    text[index + 1] == '\n'
                        ? 2
                        : 1;
                elements.Add(new TextElement(text.Substring(index, length), "\n", index, length));
                index += length;
                continue;
            }

            string value = StringInfo.GetNextTextElement(text, index);
            elements.Add(new TextElement(value, value, index, value.Length));
            index += value.Length;
        }

        return elements;
    }

    private static string Join(IEnumerable<TextElement> elements)
    {
        StringBuilder builder = new();
        foreach (TextElement element in elements)
            builder.Append(element.Value);
        return builder.ToString();
    }

    private static string TrimTerminalLineBreaks(string value) =>
        value.TrimEnd('\r', '\n');

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private sealed record TextElement(
        string Value,
        string ComparisonValue,
        int Start,
        int Length);

    private enum DiffKind
    {
        Equal,
        Delete,
        Insert
    }

    private sealed record DiffOperation(DiffKind Kind, TextElement Element);

    private sealed record RawHunk(
        int SourceStart,
        List<TextElement> Deleted,
        List<TextElement> Inserted);
}
