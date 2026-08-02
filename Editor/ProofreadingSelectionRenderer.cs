using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace JpScratch.Editor;

/// <summary>
/// 選択中の校正提案に薄い背景を敷く。差分そのものは
/// <see cref="ProofreadingInlineDiffGenerator"/> が描くため、ここでは波線を引かない。
/// </summary>
internal sealed class ProofreadingSelectionRenderer : IBackgroundRenderer
{
    internal ProofreadingInlineDiff? Selected { get; set; }
    internal Brush SelectedBackgroundBrush { get; set; } = Brushes.Transparent;

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Selected is not { } diff || diff.Length <= 0)
            return;

        textView.EnsureVisualLines();
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;

        var segment = new TextSegment
        {
            StartOffset = diff.Start,
            Length = diff.Length,
        };

        foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(
                     textView,
                     segment,
                     extendToFullWidthAtLineEnd: false))
        {
            drawingContext.DrawRectangle(SelectedBackgroundBrush, null, rect);
        }
    }
}
