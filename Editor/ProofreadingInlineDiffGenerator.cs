using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using ICSharpCode.AvalonEdit.Rendering;

namespace JpScratch.Editor;

/// <summary>
/// 校正提案の範囲を「修正前（薄色＋赤い二重取り消し線）＋修正後（緑）」に差し替えて描く
/// （要件 3.3.5 / docs/proofreading-ux-fixes-plan.md §4・§5）。本文（TextDocument）は変更しない。
///
/// - 選択中の提案は、修正前と修正後を含む変更単位全体をアクセントカラーの背景と枠線で囲む
///   （色だけに頼らず、視認できる枠線を必ず描く。§4.1）。
/// - 削除対象の空白（半角・全角・タブ・連続空白）にも二重取り消し線を確実に描く。WPF の
///   TextDecoration は空白上で線を描かないことがあるため、取り消し線は <see cref="DrawingContext"/>
///   で明示的に描く（§5.1）。本文データは描画のために変更しない。
/// </summary>
internal sealed class ProofreadingInlineDiffGenerator : VisualLineElementGenerator
{
    // TextFormatter は生成コストが高く、UI スレッドでしか使わないので使い回す。
    private static TextFormatter? _formatter;

    /// <summary>描画対象。MainWindow が提案の更新のたびに差し替える。</summary>
    internal IReadOnlyList<ProofreadingInlineDiff> Diffs { get; set; } = [];

    /// <summary>選択中の提案。null なら選択中の提案は無い（アクセント枠も描かない）。</summary>
    internal ProofreadingInlineDiff? Selected { get; set; }

    internal Brush OriginalBrush { get; set; } = Brushes.Transparent;
    internal Brush SuggestionBrush { get; set; } = Brushes.Transparent;
    internal Brush StrikeBrush { get; set; } = Brushes.Transparent;
    internal Brush SelectedBackgroundBrush { get; set; } = Brushes.Transparent;
    internal Brush SelectedBorderBrush { get; set; } = Brushes.Transparent;

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
        // 取り消し線は TextDecoration に頼らない（空白上で線を描けないため）。
        // 要素の Draw で全幅へ手描きするので、ここでは設定しない。

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

        // 取り消し線は「修正前」の描画幅にだけ引く（修正後＝緑のテキストには引かない）。
        // 背景と枠線は変更単位全体（修正前＋修正後）へ描くため、全幅のまま。
        double originalWidth = MeasureTextWidth(diff.Original, originalProps, context.TextView);

        bool isSelected = Selected is { } selected && selected == diff;
        return new ProofreadingInlineDiffElement(
            textLine,
            diff.Length,
            isSelected,
            SelectedBackgroundBrush,
            SelectedBorderBrush,
            StrikeBrush,
            originalWidth);
    }

    /// <summary>
    /// 原文（修正前）の描画幅を、末尾の空白も含めて求める。
    /// 差分要素のテキストソースは「原文 → 修正後」の順で並ぶため、TextLine の先頭計測では
    /// 末尾空白が入りにくい（TextBounds.Rectangle は末尾空白を除外しうる）。ここでは原文を
    /// 同じフォントで <see cref="FormattedText"/> として整形し、
    /// <see cref="FormattedText.WidthIncludingTrailingWhitespace"/> を取ることで、
    /// 半角・全角スペースやタブの削除提案にも確実に取り消し線を引けるようにする。
    /// </summary>
    private static double MeasureTextWidth(
        string text,
        TextRunProperties properties,
        System.Windows.Media.Visual visual)
    {
        if (text.Length == 0)
            return 0;

        var formatted = new System.Windows.Media.FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            properties.Typeface,
            properties.FontRenderingEmSize,
            Brushes.Black,
            null,
            TextOptions.GetTextFormattingMode(visual),
            System.Windows.Media.VisualTreeHelper.GetDpi(visual).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }
}

/// <summary>
/// 差分1件を描く要素。選択状態の背景・枠線と取り消し線のブラシを保持し、
/// <see cref="ProofreadingInlineDiffRun"/> へ渡す。
/// </summary>
internal sealed class ProofreadingInlineDiffElement : FormattedTextElement
{
    private readonly bool _isSelected;
    private readonly Brush _selectionBackground;
    private readonly Brush _selectionBorder;
    private readonly Brush _strikeBrush;
    private readonly double _originalWidth;

    /// <summary>描画するテキスト行（修正前＋修正後）。</summary>
    internal TextLine Line { get; }

    internal ProofreadingInlineDiffElement(
        TextLine textLine,
        int documentLength,
        bool isSelected,
        Brush selectionBackground,
        Brush selectionBorder,
        Brush strikeBrush,
        double originalWidth)
        : base(textLine, documentLength)
    {
        Line = textLine;
        _isSelected = isSelected;
        _selectionBackground = selectionBackground;
        _selectionBorder = selectionBorder;
        _strikeBrush = strikeBrush;
        _originalWidth = originalWidth;
    }

    public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        => new ProofreadingInlineDiffRun(
            this,
            TextRunProperties,
            _isSelected,
            _selectionBackground,
            _selectionBorder,
            _strikeBrush,
            _originalWidth);
}

/// <summary>
/// 差分要素の TextRun。テキスト行の描画の前後に、選択背景・二重取り消し線・枠線を重ねる。
/// 空白（半角・全角・タブ・連続空白）も含む全幅へ線を引くため、WPF の TextDecoration に依存しない。
/// </summary>
internal sealed class ProofreadingInlineDiffRun : FormattedTextRun
{
    private readonly bool _isSelected;
    private readonly Brush _selectionBackground;
    private readonly Brush _selectionBorder;
    private readonly Brush _strikeBrush;
    private readonly double _originalWidth;

    internal ProofreadingInlineDiffRun(
        ProofreadingInlineDiffElement element,
        TextRunProperties properties,
        bool isSelected,
        Brush selectionBackground,
        Brush selectionBorder,
        Brush strikeBrush,
        double originalWidth)
        : base(element, properties)
    {
        _isSelected = isSelected;
        _selectionBackground = selectionBackground;
        _selectionBorder = selectionBorder;
        _strikeBrush = strikeBrush;
        _originalWidth = originalWidth;
    }

    public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
    {
        TextLine textLine = ((ProofreadingInlineDiffElement)Element).Line;
        double baseline = textLine.Baseline;
        double width = textLine.WidthIncludingTrailingWhitespace;
        double height = textLine.Height;
        double top = origin.Y - baseline;

        // 選択中の提案: 修正前と修正後を含む変更単位全体を背景で覆う（原文範囲だけでは足りない）。
        if (_isSelected)
        {
            drawingContext.DrawRectangle(
                _selectionBackground,
                null,
                new Rect(origin.X, top, width, height));
        }

        base.Draw(drawingContext, origin, rightToLeft, sideways);

        // 二重取り消し線: ベースラインからの位置をフォントメトリクスで求め、空白を含む**修正前の
        // 描画幅**へ手描きで引く。TextDecoration は空白上で線を描かないため、これに依存しない。
        // 修正後（緑）の文字へは線を引かない（レビュー指摘 P1）。
        // 二重線の間隔 ±1.2px は従来の TextDecoration 実装と同じ実測値。
        double strikeY = origin.Y - StrikethroughPosition() * Properties.FontRenderingEmSize;
        var pen = new Pen(_strikeBrush, 1.0);
        pen.Freeze();
        double strikeEndX = origin.X + Math.Max(0, _originalWidth);
        drawingContext.DrawLine(pen, new Point(origin.X, strikeY - 1.2), new Point(strikeEndX, strikeY - 1.2));
        drawingContext.DrawLine(pen, new Point(origin.X, strikeY + 1.2), new Point(strikeEndX, strikeY + 1.2));

        // 選択中の提案: 色だけに頼らず、視認できる枠線を必ず描く。
        if (_isSelected)
        {
            var borderPen = new Pen(_selectionBorder, 1.0);
            borderPen.Freeze();
            drawingContext.DrawRectangle(
                null,
                borderPen,
                new Rect(
                    origin.X + 0.5,
                    top + 0.5,
                    Math.Max(0, width - 1),
                    Math.Max(0, height - 1)));
        }
    }

    /// <summary>
    /// 取り消し線の垂直位置（ベースラインからの割合、em 単位）。フォントメトリクスから取得し、
    /// 取得できない場合は 0.3（およそ文字の中央）へ倒す。
    /// </summary>
    private double StrikethroughPosition()
        => Properties.Typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface)
            ? glyphTypeface.StrikethroughPosition
            : 0.3;
}

/// <summary>
/// 1つの要素の中で書式を切り替えるための TextSource。
/// TextFormatter に「この文字からこの文字までは薄色、その後は緑」と伝える。
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
