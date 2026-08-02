using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace JpScratch.Editor;

/// <summary>
/// 校正提案の範囲を「修正前（薄色＋赤い二重取り消し線）＋修正後（緑）」に差し替えて描く
/// （要件 3.3.5）。本文（TextDocument）は変更しない。表示だけの置き換えである。
/// </summary>
internal sealed class ProofreadingInlineDiffGenerator : VisualLineElementGenerator
{
    // TextFormatter は生成コストが高く、UI スレッドでしか使わないので使い回す。
    private static TextFormatter? _formatter;

    private Brush _strikeBrush = Brushes.Transparent;
    private TextDecorationCollection _strikeDecorations =
        CreateDoubleStrikethrough(Brushes.Transparent);

    /// <summary>描画対象。MainWindow が提案の更新のたびに差し替える。</summary>
    internal IReadOnlyList<ProofreadingInlineDiff> Diffs { get; set; } = [];

    internal Brush OriginalBrush { get; set; } = Brushes.Transparent;
    internal Brush SuggestionBrush { get; set; } = Brushes.Transparent;

    // Freeze 済みの TextDecorationCollection は後から色を変えられないので、
    // ブラシを差し替えるたびに作り直す。ここを自動プロパティにすると
    // テーマ切替で取り消し線の色だけ古いまま固まる。
    internal Brush StrikeBrush
    {
        get => _strikeBrush;
        set
        {
            _strikeBrush = value;
            _strikeDecorations = CreateDoubleStrikethrough(value);
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (Diffs.Count == 0)
            return -1;

        VisualLine line = CurrentContext.VisualLine;
        return ProofreadingInlineDiffLayout.FirstInterestedOffset(
            Diffs,
            startOffset,
            line.FirstDocumentLine.Offset,
            line.LastDocumentLine.EndOffset);
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        if (Diffs.Count == 0)
            return null;

        ITextRunConstructionContext context = CurrentContext;
        VisualLine line = context.VisualLine;

        // GetFirstInterestedOffset と同じ判定をここでも行う。呼ばれ方に依存しないため。
        if (!ProofreadingInlineDiffLayout.TryFindAt(
                Diffs,
                offset,
                line.FirstDocumentLine.Offset,
                line.LastDocumentLine.EndOffset,
                out ProofreadingInlineDiff diff))
        {
            return null;
        }

        // スナップショットが古いまま描かない。提案が失効した直後でも
        // 「今の本文と一致する範囲」だけを差分として見せる。
        if (!string.Equals(
                context.Document.GetText(diff.Start, diff.Length),
                diff.Original,
                StringComparison.Ordinal))
        {
            return null;
        }

        TextRunProperties global = context.GlobalTextRunProperties;

        var originalProps = new VisualLineElementTextRunProperties(global);
        originalProps.SetForegroundBrush(OriginalBrush);
        originalProps.SetTextDecorations(_strikeDecorations);

        List<(string Text, TextRunProperties Props)> runs = [(diff.Original, originalProps)];
        if (ProofreadingInlineDiffLayout.HasSuggestionText(diff))
        {
            var suggestionProps = new VisualLineElementTextRunProperties(global);
            suggestionProps.SetForegroundBrush(SuggestionBrush);
            runs.Add((diff.Suggestion, suggestionProps));
        }

        _formatter ??= TextFormatter.Create(TextOptions.GetTextFormattingMode(context.TextView));
        TextLine textLine = _formatter.FormatLine(
            new InlineDiffTextSource(runs),
            firstCharIndex: 0,
            paragraphWidth: 32000,
            new InlineDiffParagraphProperties(global),
            previousLineBreak: null);

        return new FormattedTextElement(textLine, diff.Length);
    }

    /// <summary>
    /// 赤い二重取り消し線。PenOffsetUnit を既定（FontRecommended）のままにすると、
    /// PenOffset がフォント推奨単位で解釈されて線が文字の外へ飛ぶ（実測で確認）。
    /// 必ずピクセル固定にすること。
    /// </summary>
    private static TextDecorationCollection CreateDoubleStrikethrough(Brush brush)
    {
        var pen = new Pen(brush, 1.0);
        pen.Freeze();

        var decorations = new TextDecorationCollection
        {
            CreateLine(pen, -1.2),
            CreateLine(pen, 1.2),
        };
        decorations.Freeze();
        return decorations;

        static TextDecoration CreateLine(Pen pen, double offset) => new()
        {
            Location = TextDecorationLocation.Strikethrough,
            Pen = pen,
            PenOffset = offset,
            PenOffsetUnit = TextDecorationUnit.Pixel,
            PenThicknessUnit = TextDecorationUnit.Pixel,
        };
    }
}

/// <summary>
/// 1つの要素の中で書式を切り替えるための TextSource。
/// TextFormatter に「この文字からこの文字までは薄色＋取り消し線、その後は緑」と伝える。
/// </summary>
file sealed class InlineDiffTextSource : TextSource
{
    private readonly IReadOnlyList<(string Text, TextRunProperties Props)> _runs;

    internal InlineDiffTextSource(IReadOnlyList<(string Text, TextRunProperties Props)> runs)
        => _runs = runs;

    public override TextRun GetTextRun(int textSourceCharacterIndex)
    {
        int position = 0;
        foreach ((string text, TextRunProperties props) in _runs)
        {
            if (text.Length == 0)
                continue;

            if (textSourceCharacterIndex < position + text.Length)
            {
                int offset = textSourceCharacterIndex - position;
                return new TextCharacters(text, offset, text.Length - offset, props);
            }

            position += text.Length;
        }

        return new TextEndOfParagraph(1);
    }

    public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(
        int textSourceCharacterIndexLimit)
        => new(0, new CultureSpecificCharacterBufferRange(null!, CharacterBufferRange.Empty));

    public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int index)
        => throw new NotSupportedException();
}

file sealed class InlineDiffParagraphProperties : TextParagraphProperties
{
    internal InlineDiffParagraphProperties(TextRunProperties defaults)
        => DefaultTextRunProperties = defaults;

    public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
    public override TextAlignment TextAlignment => TextAlignment.Left;
    public override double LineHeight => 0;
    public override bool FirstLineInParagraph => false;
    public override TextRunProperties DefaultTextRunProperties { get; }
    public override TextWrapping TextWrapping => TextWrapping.NoWrap;
    public override TextMarkerProperties TextMarkerProperties => null!;
    public override double Indent => 0;
}
