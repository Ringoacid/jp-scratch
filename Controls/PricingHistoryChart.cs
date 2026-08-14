using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace JpScratch.Controls;

internal sealed record PricingChartPoint(
    DateOnly EffectiveFrom,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion);

/// <summary>
/// モデル単価の実効履歴を、日付に比例した横軸と階段線で描画する軽量チャート。
/// 正確な値と編集操作は隣接する履歴表が担うため、この要素はフォーカスを持たない。
/// </summary>
public sealed class PricingHistoryChart : FrameworkElement
{
    private const double LeftMargin = 68;
    private const double RightMargin = 18;
    // 凡例と「今日」を別の行に置き、線やラベルが重ならない高さを確保する。
    private const double TopMargin = 42;
    private const double BottomMargin = 34;

    private IReadOnlyList<PricingChartPoint> _points = [];
    private string _currency = "USD";

    public PricingHistoryChart()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        ToolTip = "実効単価の推移です。正確な値は下の履歴表で確認できます。";
    }

    internal void SetData(IEnumerable<PricingChartPoint> points, string currency)
    {
        _points = points
            .OrderBy(point => point.EffectiveFrom)
            .ToArray();
        _currency = string.IsNullOrWhiteSpace(currency) ? "USD" : currency;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width;
        return new Size(Math.Max(320, width), 220);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        Brush textBrush = ResourceBrush("TextBrush", Brushes.Black);
        Brush subtleBrush = ResourceBrush("SubtleTextBrush", Brushes.DimGray);
        Brush borderBrush = ResourceBrush("BorderBrush", Brushes.Gray);
        Brush inputBrush = ResourceBrush("AccentBrush", Brushes.RoyalBlue);
        Brush outputBrush = ResourceBrush("UsageProgressWarningBrush", Brushes.DarkOrange);

        Rect plot = new(
            LeftMargin,
            TopMargin,
            Math.Max(1, ActualWidth - LeftMargin - RightMargin),
            Math.Max(1, ActualHeight - TopMargin - BottomMargin));

        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_points.Count == 0)
        {
            DrawText(drawingContext, "表示できる料金履歴がありません。", 12, subtleBrush,
                new Point(LeftMargin, TopMargin));
            return;
        }

        decimal maxPrice = _points.Max(point =>
            Math.Max(point.InputPricePerMillion, point.OutputPricePerMillion));
        double yMax = maxPrice <= 0 ? 1 : NiceMaximum((double)maxPrice);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly first = _points[0].EffectiveFrom;
        DateOnly last = _points[^1].EffectiveFrom;
        DateOnly dataMin = first < today ? first : today;
        DateOnly dataMax = last > today ? last : today;
        int dataSpanDays = dataMax.DayNumber - dataMin.DayNumber;
        int horizontalPaddingDays = dataSpanDays == 0
            ? 15
            : Math.Clamp((int)Math.Ceiling(dataSpanDays * 0.1), 7, 45);
        DateOnly minDate = AddDaysClamped(dataMin, -horizontalPaddingDays);
        DateOnly maxDate = AddDaysClamped(dataMax, horizontalPaddingDays);

        double totalDays = Math.Max(1, maxDate.DayNumber - minDate.DayNumber);
        double X(DateOnly date) => plot.Left +
            (date.DayNumber - minDate.DayNumber) / totalDays * plot.Width;
        double Y(decimal price) => plot.Bottom - (double)price / yMax * plot.Height;

        var gridPen = new Pen(borderBrush, 1);
        gridPen.Freeze();
        for (int index = 0; index <= 4; index++)
        {
            double y = plot.Bottom - plot.Height * index / 4d;
            drawingContext.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            decimal value = (decimal)(yMax * index / 4d);
            string label = FormatPrice(value);
            FormattedText formatted = CreateText(label, 10, subtleBrush);
            drawingContext.DrawText(formatted, new Point(plot.Left - formatted.Width - 7, y - formatted.Height / 2));
        }

        if (today >= minDate && today <= maxDate)
        {
            var todayPen = new Pen(subtleBrush, 1) { DashStyle = DashStyles.Dash };
            todayPen.Freeze();
            double todayX = X(today);
            drawingContext.DrawLine(todayPen, new Point(todayX, plot.Top), new Point(todayX, plot.Bottom));
            DrawCenteredText(drawingContext, "今日", 10, subtleBrush, todayX, 24);
        }

        DrawStepSeries(drawingContext, plot, minDate, maxDate, X, Y,
            point => point.InputPricePerMillion, inputBrush, dashed: false);
        DrawStepSeries(drawingContext, plot, minDate, maxDate, X, Y,
            point => point.OutputPricePerMillion, outputBrush, dashed: true);

        DrawDateLabel(drawingContext, minDate, plot.Left, subtleBrush, TextAlignment.Left);
        DrawDateLabel(drawingContext, maxDate, plot.Right, subtleBrush, TextAlignment.Right);

        DrawLegendItem(drawingContext, "入力単価（実線）", inputBrush, plot.Left, 5, dashed: false);
        DrawLegendItem(drawingContext, "出力単価（破線）", outputBrush, plot.Left + 132, 5, dashed: true);
        DrawText(drawingContext, $"{_currency} / 1M tokens", 10, subtleBrush,
            new Point(Math.Max(plot.Left, plot.Right - 104), 5));
    }

    private static DateOnly AddDaysClamped(DateOnly date, int days)
    {
        int dayNumber = Math.Clamp(
            date.DayNumber + days,
            DateOnly.MinValue.DayNumber,
            DateOnly.MaxValue.DayNumber);
        return DateOnly.FromDayNumber(dayNumber);
    }

    private void DrawStepSeries(
        DrawingContext drawingContext,
        Rect plot,
        DateOnly minDate,
        DateOnly maxDate,
        Func<DateOnly, double> x,
        Func<decimal, double> y,
        Func<PricingChartPoint, decimal> value,
        Brush brush,
        bool dashed)
    {
        PricingChartPoint? active = _points.LastOrDefault(point => point.EffectiveFrom <= minDate);
        PricingChartPoint[] visible = _points
            .Where(point => point.EffectiveFrom > minDate && point.EffectiveFrom <= maxDate)
            .ToArray();
        var pen = new Pen(brush, 2) { DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid };
        pen.Freeze();

        double currentX;
        if (active is null)
        {
            active = visible.FirstOrDefault();
            if (active is null) return;
            currentX = x(active.EffectiveFrom);
            visible = visible.Skip(1).ToArray();
        }
        else
        {
            currentX = plot.Left;
        }
        double currentY = y(value(active));
        foreach (PricingChartPoint point in visible)
        {
            double nextX = x(point.EffectiveFrom);
            double nextY = y(value(point));
            drawingContext.DrawLine(pen, new Point(currentX, currentY), new Point(nextX, currentY));
            drawingContext.DrawLine(pen, new Point(nextX, currentY), new Point(nextX, nextY));
            drawingContext.DrawEllipse(brush, null, new Point(nextX, nextY), 3, 3);
            currentX = nextX;
            currentY = nextY;
        }

        drawingContext.DrawLine(pen, new Point(currentX, currentY), new Point(plot.Right, currentY));
        if (active.EffectiveFrom >= minDate)
            drawingContext.DrawEllipse(brush, null, new Point(x(active.EffectiveFrom), y(value(active))), 3, 3);
    }

    private void DrawLegendItem(
        DrawingContext drawingContext,
        string text,
        Brush brush,
        double x,
        double y,
        bool dashed)
    {
        var pen = new Pen(brush, 2) { DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid };
        pen.Freeze();
        drawingContext.DrawLine(pen, new Point(x, y + 7), new Point(x + 22, y + 7));
        DrawText(drawingContext, text, 10, ResourceBrush("TextBrush", Brushes.Black),
            new Point(x + 28, y));
    }

    private void DrawDateLabel(
        DrawingContext drawingContext,
        DateOnly date,
        double x,
        Brush brush,
        TextAlignment alignment)
    {
        FormattedText text = CreateText(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 10, brush);
        double left = alignment == TextAlignment.Right ? x - text.Width : x;
        drawingContext.DrawText(text, new Point(left, ActualHeight - BottomMargin + 8));
    }

    private void DrawCenteredText(
        DrawingContext drawingContext,
        string text,
        double size,
        Brush brush,
        double centerX,
        double y)
    {
        FormattedText formatted = CreateText(text, size, brush);
        drawingContext.DrawText(formatted, new Point(centerX - formatted.Width / 2, y));
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        double size,
        Brush brush,
        Point origin)
        => drawingContext.DrawText(CreateText(text, size, brush), origin);

    private FormattedText CreateText(string text, double size, Brush brush)
        => new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush ResourceBrush(string key, Brush fallback)
        => TryFindResource(key) as Brush ?? fallback;

    private string FormatPrice(decimal value)
    {
        string symbol = string.Equals(_currency, "JPY", StringComparison.Ordinal) ? "¥" : "$";
        return symbol + value.ToString(value >= 100 ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    private static double NiceMaximum(double value)
    {
        double exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double normalized = value / exponent;
        double rounded = normalized <= 1 ? 1
            : normalized <= 2 ? 2
            : normalized <= 5 ? 5
            : 10;
        return rounded * exponent;
    }
}
