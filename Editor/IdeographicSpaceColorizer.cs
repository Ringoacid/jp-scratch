using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace JpScratch.Editor;

/// <summary>
/// 全角スペース（U+3000）を可視化する（要件 3.2.2）。
/// AvalonEdit の ShowSpaces は半角スペースしか描かないため、日本語では自前で塗る必要がある。
/// </summary>
internal sealed class IdeographicSpaceColorizer : DocumentColorizingTransformer
{
    private const char IdeographicSpace = '　';

    public bool Enabled { get; set; }
    public Brush Background { get; set; } = Brushes.Transparent;

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!Enabled || line.Length == 0) return;

        var text = CurrentContext.Document.GetText(line.Offset, line.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != IdeographicSpace) continue;

            // 連続する全角スペースはひとまとめに塗る
            var start = i;
            while (i + 1 < text.Length && text[i + 1] == IdeographicSpace) i++;

            ChangeLinePart(line.Offset + start, line.Offset + i + 1,
                element => element.TextRunProperties.SetBackgroundBrush(Background));
        }
    }
}
