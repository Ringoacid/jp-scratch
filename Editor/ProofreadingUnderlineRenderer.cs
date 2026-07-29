using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using JpScratch.Proofreading;

namespace JpScratch.Editor;

/// <summary>
/// 校正提案の範囲へ波線を描く。本文や選択範囲は変更しない。
/// </summary>
internal sealed class ProofreadingUnderlineRenderer : IBackgroundRenderer
{
    internal IReadOnlyList<ProofreadingProposal> Proposals { get; set; } = [];
    internal ProofreadingProposal? Selected { get; set; }
    internal Brush UnderlineBrush { get; set; } = Brushes.Transparent;
    internal Brush SelectedBackgroundBrush { get; set; } = Brushes.Transparent;

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Proposals.Count == 0)
            return;

        textView.EnsureVisualLines();
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;

        int visibleStart = textView.VisualLines[0].FirstDocumentLine.Offset;
        int visibleEnd = textView.VisualLines[^1].LastDocumentLine.EndOffset;
        var pen = new Pen(UnderlineBrush, 1.4);
        pen.Freeze();

        foreach (ProofreadingProposal proposal in Proposals)
        {
            if (!proposal.IsActive)
                continue;

            int start = proposal.Start;
            int length = proposal.Length;
            if (start + length < visibleStart || start > visibleEnd)
                continue;

            var segment = new TextSegment
            {
                StartOffset = start,
                Length = length,
            };

            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(
                         textView,
                         segment,
                         extendToFullWidthAtLineEnd: false))
            {
                if (ReferenceEquals(proposal, Selected))
                    drawingContext.DrawRectangle(SelectedBackgroundBrush, null, rect);

                DrawWave(drawingContext, pen, rect);
            }
        }
    }

    private static void DrawWave(DrawingContext drawingContext, Pen pen, Rect rect)
    {
        if (rect.Width <= 0)
            return;

        double baseline = Math.Max(rect.Top + 1, rect.Bottom - 1.5);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(
                new Point(rect.Left, baseline),
                isFilled: false,
                isClosed: false);

            bool upward = true;
            for (double x = rect.Left + 2; x < rect.Right; x += 2)
            {
                context.LineTo(
                    new Point(x, baseline + (upward ? -1 : 1)),
                    isStroked: true,
                    isSmoothJoin: false);
                upward = !upward;
            }

            context.LineTo(
                new Point(rect.Right, baseline),
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
