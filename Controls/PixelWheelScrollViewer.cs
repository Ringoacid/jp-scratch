using System.Windows.Controls;
using System.Windows.Input;

namespace JpScratch.Controls;

/// <summary>ドロップダウンのホイールを一定のピクセル幅で移動させる。</summary>
public sealed class PixelWheelScrollViewer : ScrollViewer
{
    private const double PixelsPerNotch = 48;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (ScrollableHeight <= 0)
        {
            base.OnMouseWheel(e);
            return;
        }

        ScrollToVerticalOffset(Math.Clamp(
            VerticalOffset - e.Delta / 120.0 * PixelsPerNotch,
            0,
            ScrollableHeight));
        e.Handled = true;
    }
}
