using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace JpScratch.Proofreading;

internal sealed record ProofreadingParagraph(
    int Index,
    int Start,
    int Length,
    string Text,
    string ContentHash);

internal sealed record ProofreadingRequest(
    int SourceStart,
    int SourceLength,
    string SourceText,
    string? BeforeContext,
    string? AfterContext,
    string ContentHash,
    int ParagraphIndex,
    int PartIndex,
    int PartCount,
    /// <summary>
    /// 要件3.4.4の学習素材（スタイルガイド・カスタム指示・few-shot）を合成した
    /// システム指示。null なら <see cref="ProofreadingPrompt.SystemInstruction"/> を使う。
    /// プランナー自身は学習素材を知らないため既定 null とし、送信直前に呼び出し側が
    /// <c>with</c> 式で差し込む。
    /// </summary>
    string? SystemInstructionOverride = null);

internal sealed record ProofreadingPlan(
    string DocumentText,
    IReadOnlyList<ProofreadingParagraph> Paragraphs,
    IReadOnlyList<ProofreadingRequest> Requests);

/// <summary>
/// 文書を段落へ分け、前回送信済みのスナップショットとの差から校正対象を決める。
/// API が成功したときだけ <see cref="MarkSent"/> を呼ぶことで、失敗した対象を再試行できる。
/// </summary>
internal sealed class ParagraphProofreadingPlanner
{
    internal const int MaxTargetLength = 2000;

    private IReadOnlyList<string> _lastSentHashes = [];

    internal ProofreadingPlan CreateAutomaticPlan(string documentText)
    {
        ArgumentNullException.ThrowIfNull(documentText);

        IReadOnlyList<ProofreadingParagraph> paragraphs = SplitParagraphs(documentText);
        HashSet<int> unchanged = FindUnchangedCurrentIndexes(
            _lastSentHashes,
            paragraphs.Select(paragraph => paragraph.ContentHash).ToArray());
        IReadOnlyList<ProofreadingRequest> requests = paragraphs
            .Where(paragraph => !unchanged.Contains(paragraph.Index))
            .SelectMany(paragraph => CreateParagraphRequests(paragraphs, paragraph))
            .ToArray();

        return new ProofreadingPlan(documentText, paragraphs, requests);
    }

    internal ProofreadingPlan CreateSelectionPlan(
        string documentText,
        int selectionStart,
        int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        if (selectionStart < 0 ||
            selectionLength <= 0 ||
            selectionStart > documentText.Length - selectionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionLength),
                "校正対象の選択範囲が文書外です。");
        }

        IReadOnlyList<ProofreadingParagraph> paragraphs = SplitParagraphs(documentText);
        int selectionEnd = selectionStart + selectionLength;
        int firstIntersecting = FindFirstIntersecting(
            paragraphs,
            selectionStart,
            selectionEnd);
        int lastIntersecting = FindLastIntersecting(
            paragraphs,
            selectionStart,
            selectionEnd);

        string? before = BuildSelectionBeforeContext(
            documentText,
            paragraphs,
            firstIntersecting,
            selectionStart);
        string? after = BuildSelectionAfterContext(
            documentText,
            paragraphs,
            lastIntersecting,
            selectionEnd);
        IReadOnlyList<TextPart> parts = SplitTarget(
            documentText.Substring(selectionStart, selectionLength));
        IReadOnlyList<ProofreadingRequest> requests = parts
            .Select((part, index) => new ProofreadingRequest(
                selectionStart + part.Start,
                part.Length,
                part.Text,
                index == 0 ? before : parts[index - 1].Text,
                index == parts.Count - 1 ? after : parts[index + 1].Text,
                Hash(part.Text),
                firstIntersecting,
                index,
                parts.Count))
            .ToArray();

        return new ProofreadingPlan(documentText, paragraphs, requests);
    }

    internal void MarkSent(ProofreadingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _lastSentHashes = plan.Paragraphs
            .Select(paragraph => paragraph.ContentHash)
            .ToArray();
    }

    /// <summary>
    /// 校正ループが途中で中断されたとき、送信が完了していない段落だけを未送信として残し、
    /// それ以外（完了済み・変更なしと判定された段落）は送信済みのまま記録する。
    /// 本文変更・タブ切替で中断しても、完了済みで内容が変わっていない段落の再送（二重課金）を防ぎ、
    /// 未送信の段落は次回の自動プランで再試行できる。
    /// <paramref name="completedRequestCount"/> は完了済みリクエスト数（＝ループの現在 index）。
    /// <see cref="ProofreadingRequest"/> は段落→パートの順で並ぶため、最後のパートが完了していれば
    /// その段落の全パートが送信済みである。
    /// 注意: <see cref="_lastSentHashes"/> は過去の実行ぶんも含む累積状態なので、丸ごと置き換えては
    /// いけない。「今回完了した段落だけを入れる」方式だと、前回送信済みで今回のプランに現れない
    /// （＝変更なしと判定された）段落が未送信に戻り、次回再送＝二重課金になる。
    /// </summary>
    internal void MarkSent(ProofreadingPlan plan, int completedRequestCount)
    {
        ArgumentNullException.ThrowIfNull(plan);

        HashSet<int> incomplete = plan.Requests
            .Skip(completedRequestCount)
            .Select(request => request.ParagraphIndex)
            .ToHashSet();

        _lastSentHashes = plan.Paragraphs
            .Where(paragraph => !incomplete.Contains(paragraph.Index))
            .Select(paragraph => paragraph.ContentHash)
            .ToArray();
    }

    internal static IReadOnlyList<ProofreadingParagraph> SplitParagraphs(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            return [];

        IReadOnlyList<TextLine> lines = SplitLines(text);
        bool hasBlankLine = lines.Any(line => line.IsBlank);
        List<(int Start, int End)> ranges = hasBlankLine
            ? CreateBlankLineSeparatedRanges(lines)
            : lines.Where(line => !line.IsBlank)
                .Select(line => (line.Start, line.ContentEnd))
                .ToList();

        List<ProofreadingParagraph> paragraphs = [];
        foreach ((int start, int end) in ranges.Where(range => range.End > range.Start))
        {
            string paragraphText = text.Substring(start, end - start);
            paragraphs.Add(new ProofreadingParagraph(
                paragraphs.Count,
                start,
                end - start,
                paragraphText,
                Hash(paragraphText)));
        }

        return paragraphs;
    }

    private static IEnumerable<ProofreadingRequest> CreateParagraphRequests(
        IReadOnlyList<ProofreadingParagraph> paragraphs,
        ProofreadingParagraph paragraph)
    {
        IReadOnlyList<TextPart> parts = SplitTarget(paragraph.Text);
        for (int index = 0; index < parts.Count; index++)
        {
            TextPart part = parts[index];
            string? before = index > 0
                ? parts[index - 1].Text
                : paragraph.Index > 0
                    ? paragraphs[paragraph.Index - 1].Text
                    : null;
            string? after = index + 1 < parts.Count
                ? parts[index + 1].Text
                : paragraph.Index + 1 < paragraphs.Count
                    ? paragraphs[paragraph.Index + 1].Text
                    : null;

            yield return new ProofreadingRequest(
                paragraph.Start + part.Start,
                part.Length,
                part.Text,
                before,
                after,
                paragraph.ContentHash,
                paragraph.Index,
                index,
                parts.Count);
        }
    }

    private static IReadOnlyList<TextPart> SplitTarget(string text)
    {
        if (text.Length <= MaxTargetLength)
            return [new TextPart(0, text.Length, text)];

        List<TextPart> parts = [];
        int partStart = 0;
        int partLength = 0;
        for (int index = 0; index < text.Length;)
        {
            string element = StringInfo.GetNextTextElement(text, index);
            if (partLength > 0 && partLength + element.Length > MaxTargetLength)
            {
                parts.Add(new TextPart(
                    partStart,
                    partLength,
                    text.Substring(partStart, partLength)));
                partStart = index;
                partLength = 0;
            }

            partLength += element.Length;
            index += element.Length;
        }

        if (partLength > 0)
        {
            parts.Add(new TextPart(
                partStart,
                partLength,
                text.Substring(partStart, partLength)));
        }

        return parts;
    }

    private static IReadOnlyList<TextLine> SplitLines(string text)
    {
        List<TextLine> lines = [];
        for (int start = 0; start < text.Length;)
        {
            int cursor = start;
            while (cursor < text.Length &&
                   text[cursor] is not ('\r' or '\n'))
            {
                cursor++;
            }

            int contentEnd = cursor;
            if (cursor < text.Length)
            {
                if (text[cursor] == '\r' &&
                    cursor + 1 < text.Length &&
                    text[cursor + 1] == '\n')
                {
                    cursor += 2;
                }
                else
                {
                    cursor++;
                }
            }

            string content = text.Substring(start, contentEnd - start);
            lines.Add(new TextLine(
                start,
                contentEnd,
                cursor,
                string.IsNullOrWhiteSpace(content)));
            start = cursor;
        }

        return lines;
    }

    private static List<(int Start, int End)> CreateBlankLineSeparatedRanges(
        IReadOnlyList<TextLine> lines)
    {
        List<(int Start, int End)> ranges = [];
        int? blockStart = null;
        int blockEnd = 0;
        foreach (TextLine line in lines)
        {
            if (line.IsBlank)
            {
                if (blockStart is not null)
                {
                    ranges.Add((blockStart.Value, blockEnd));
                    blockStart = null;
                }
                continue;
            }

            blockStart ??= line.Start;
            blockEnd = line.ContentEnd;
        }

        if (blockStart is not null)
            ranges.Add((blockStart.Value, blockEnd));
        return ranges;
    }

    private static HashSet<int> FindUnchangedCurrentIndexes(
        IReadOnlyList<string> previous,
        IReadOnlyList<string> current)
    {
        Dictionary<string, int> remaining = new(StringComparer.Ordinal);
        foreach (string hash in previous)
        {
            remaining[hash] = remaining.GetValueOrDefault(hash) + 1;
        }

        HashSet<int> unchanged = [];
        for (int index = 0; index < current.Count; index++)
        {
            string hash = current[index];
            int count = remaining.GetValueOrDefault(hash);
            if (count == 0)
                continue;

            unchanged.Add(index);
            if (count == 1)
                remaining.Remove(hash);
            else
                remaining[hash] = count - 1;
        }

        return unchanged;
    }

    private static int FindFirstIntersecting(
        IReadOnlyList<ProofreadingParagraph> paragraphs,
        int start,
        int end)
        => paragraphs
            .FirstOrDefault(paragraph =>
                paragraph.Start < end &&
                paragraph.Start + paragraph.Length > start)
            ?.Index ?? -1;

    private static int FindLastIntersecting(
        IReadOnlyList<ProofreadingParagraph> paragraphs,
        int start,
        int end)
        => paragraphs
            .LastOrDefault(paragraph =>
                paragraph.Start < end &&
                paragraph.Start + paragraph.Length > start)
            ?.Index ?? -1;

    private static string? BuildSelectionBeforeContext(
        string document,
        IReadOnlyList<ProofreadingParagraph> paragraphs,
        int firstIntersecting,
        int selectionStart)
    {
        int contextStart;
        if (firstIntersecting >= 0)
        {
            contextStart = firstIntersecting > 0
                ? paragraphs[firstIntersecting - 1].Start
                : paragraphs[firstIntersecting].Start;
        }
        else
        {
            contextStart = selectionStart;
        }

        return contextStart < selectionStart
            ? document.Substring(contextStart, selectionStart - contextStart)
                .TrimEnd('\r', '\n')
            : null;
    }

    private static string? BuildSelectionAfterContext(
        string document,
        IReadOnlyList<ProofreadingParagraph> paragraphs,
        int lastIntersecting,
        int selectionEnd)
    {
        int contextEnd;
        if (lastIntersecting >= 0)
        {
            contextEnd = lastIntersecting + 1 < paragraphs.Count
                ? paragraphs[lastIntersecting + 1].Start +
                  paragraphs[lastIntersecting + 1].Length
                : paragraphs[lastIntersecting].Start +
                  paragraphs[lastIntersecting].Length;
        }
        else
        {
            contextEnd = selectionEnd;
        }

        return contextEnd > selectionEnd
            ? document.Substring(selectionEnd, contextEnd - selectionEnd)
                .TrimStart('\r', '\n')
            : null;
    }

    private static string Hash(string text)
    {
        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private sealed record TextLine(
        int Start,
        int ContentEnd,
        int End,
        bool IsBlank);

    private sealed record TextPart(int Start, int Length, string Text);
}
