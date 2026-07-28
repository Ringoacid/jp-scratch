using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace JpScratch.Editor;

/// <summary>
/// 検索ヒットの背景を描く（要件 3.2.3 の「全ヒット箇所のハイライト」）。
/// AvalonEdit の装飾層に乗るので、本文を書き換えずに色を敷ける。
/// </summary>
internal sealed class SearchMatchRenderer : IBackgroundRenderer
{
    private readonly List<(int Offset, int Length)> _matches = [];

    public Brush MatchBrush { get; set; } = Brushes.Transparent;
    public Brush CurrentMatchBrush { get; set; } = Brushes.Transparent;

    /// <summary>今フォーカスしているヒットの番号。-1 なら特別扱いなし。</summary>
    public int CurrentIndex { get; set; } = -1;

    public IReadOnlyList<(int Offset, int Length)> Matches => _matches;

    /// <summary>選択レイヤに描くことで、文字そのものは隠さずに背景だけを塗る。</summary>
    public KnownLayer Layer => KnownLayer.Selection;

    public void SetMatches(IEnumerable<(int Offset, int Length)> matches)
    {
        _matches.Clear();
        _matches.AddRange(matches);
    }

    public void Clear()
    {
        _matches.Clear();
        CurrentIndex = -1;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_matches.Count == 0) return;

        textView.EnsureVisualLines();
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0) return;

        // 画面外のヒットまでジオメトリを作ると、巨大な文書で描画が落ちる
        var first = textView.VisualLines[0].FirstDocumentLine.Offset;
        var last = textView.VisualLines[^1].LastDocumentLine.EndOffset;

        for (var i = 0; i < _matches.Count; i++)
        {
            var (offset, length) = _matches[i];
            if (offset + length < first || offset > last) continue;

            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = offset, Length = length });

            var geometry = builder.CreateGeometry();
            if (geometry is null) continue;

            drawingContext.DrawGeometry(i == CurrentIndex ? CurrentMatchBrush : MatchBrush, null, geometry);
        }
    }
}
