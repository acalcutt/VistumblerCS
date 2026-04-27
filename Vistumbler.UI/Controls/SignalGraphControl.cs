using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Vistumbler.Core.Enums;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.Controls;

/// <summary>
/// WPF OnRender signal graph with proper Y-axis scale labels, tick marks, X-axis
/// time labels, plot-area border, and adaptive label density.
///
/// Graph1 (Line) – up to 50 most-recent history points connected with lines, newest on right.
/// Graph2 (Bar)  – one filled column per pixel-column of available width, newest on right.
///
/// Layout margins:
///   LeftBorder   = 46 px  (Y-axis labels + ticks)
///   TopBorder    =  6 px
///   RightBorder  =  8 px
///   BottomBorder = 22 px  (X-axis time labels + ticks)
/// </summary>
public class SignalGraphControl : FrameworkElement
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(GraphMode), typeof(SignalGraphControl),
            new FrameworkPropertyMetadata(GraphMode.Hidden, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UseRssiProperty =
        DependencyProperty.Register(nameof(UseRssi), typeof(bool), typeof(SignalGraphControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GraphDeadTimeProperty =
        DependencyProperty.Register(nameof(GraphDeadTime), typeof(bool), typeof(SignalGraphControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SignalHistoryProperty =
        DependencyProperty.Register(nameof(SignalHistory), typeof(IReadOnlyList<SignalHistory>),
            typeof(SignalGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public GraphMode Mode
    {
        get => (GraphMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }
    public bool UseRssi
    {
        get => (bool)GetValue(UseRssiProperty);
        set => SetValue(UseRssiProperty, value);
    }
    public bool GraphDeadTime
    {
        get => (bool)GetValue(GraphDeadTimeProperty);
        set => SetValue(GraphDeadTimeProperty, value);
    }
    public IReadOnlyList<SignalHistory>? SignalHistory
    {
        get => (IReadOnlyList<SignalHistory>?)GetValue(SignalHistoryProperty);
        set => SetValue(SignalHistoryProperty, value);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private const double LeftBorder   = 46;   // Y-axis labels + ticks
    private const double TopBorder    =  6;
    private const double RightBorder  =  8;
    private const double BottomBorder = 22;   // X-axis time labels + ticks

    // ── Drawing resources ─────────────────────────────────────────────────────

    private static readonly Brush OuterBrush  = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xAA));
    private static readonly Brush PlotBrush   = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF0));
    private static readonly Pen   BorderPen   = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x44)), 1);
    private static readonly Pen   TickPen     = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x44)), 1);
    private static readonly Pen   GridMajor   = new Pen(new SolidColorBrush(Color.FromArgb(170, 0xAA, 0xAA, 0x77)), 1);
    private static readonly Pen   GridMinor   = new Pen(new SolidColorBrush(Color.FromArgb( 70, 0xAA, 0xAA, 0x77)), 1);
    private static readonly Pen   SignalLine  = new Pen(new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)), 2.0);
    private static readonly Pen   DeadLine    = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x00, 0x00)), 1.0);
    private static readonly Brush DotFill     = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00));
    private static readonly Brush BarFill     = new SolidColorBrush(Color.FromArgb(200, 0xCC, 0x00, 0x00));
    private static readonly Typeface AxisFont = new Typeface("Segoe UI");

    static SignalGraphControl()
    {
        OuterBrush.Freeze(); PlotBrush.Freeze(); BorderPen.Freeze(); TickPen.Freeze();
        GridMajor.Freeze();  GridMinor.Freeze();
        SignalLine.Freeze(); DeadLine.Freeze();
        DotFill.Freeze();    BarFill.Freeze();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double plotX = LeftBorder;
        double plotY = TopBorder;
        double plotW = w - LeftBorder - RightBorder;
        double plotH = h - TopBorder  - BottomBorder;
        if (plotW < 4 || plotH < 4) return;

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Outer background
        dc.DrawRectangle(OuterBrush, null, new Rect(0, 0, w, h));

        // Plot area background
        dc.DrawRectangle(PlotBrush, null, new Rect(plotX, plotY, plotW, plotH));

        // Y-axis grid lines (span full plot width, no clipping needed)
        DrawYGrid(dc, plotX, plotY, plotW, plotH);

        // Clip data to the plot interior
        dc.PushClip(new RectangleGeometry(new Rect(plotX + 1, plotY + 1, plotW - 2, plotH - 2)));

        var history = SignalHistory;
        bool hasData = Mode != GraphMode.Hidden && history != null && history.Count > 0;
        if (hasData)
        {
            if (Mode == GraphMode.Line)
                DrawLineGraph(dc, history!, plotX, plotY, plotW, plotH);
            else
                DrawBarGraph(dc, history!, plotX, plotY, plotW, plotH);
        }

        dc.Pop(); // pop clip

        // Y-axis labels + tick marks (left margin, drawn after clip pop)
        DrawYAxisLabels(dc, plotX, plotY, plotH, ppd);

        // X-axis time labels + tick marks (bottom margin)
        if (hasData)
            DrawXAxisLabels(dc, history!, plotX, plotY, plotW, plotH, ppd);

        // Border drawn last so it sits on top of everything
        dc.DrawRectangle(null, BorderPen, new Rect(plotX, plotY, plotW, plotH));
    }

    // ── Y-axis grid ───────────────────────────────────────────────────────────

    private void DrawYGrid(DrawingContext dc, double plotX, double plotY, double plotW, double plotH)
    {
        for (int i = 0; i <= 10; i++)
        {
            double y    = plotY + plotH * (i / 10.0);
            bool   major = (i % 2 == 0);
            dc.DrawLine(major ? GridMajor : GridMinor,
                new Point(plotX, y), new Point(plotX + plotW, y));
        }
    }

    // ── Y-axis labels ─────────────────────────────────────────────────────────

    private void DrawYAxisLabels(DrawingContext dc, double plotX, double plotY, double plotH, double ppd)
    {
        // Adaptive density: label every 1st, 2nd, or 5th tick based on available height
        int every = plotH > 160 ? 1 : plotH > 90 ? 2 : 5;

        for (int i = 0; i <= 10; i++)
        {
            double y = plotY + plotH * (i / 10.0);

            // Tick mark outside the plot border
            dc.DrawLine(TickPen, new Point(plotX - 4, y), new Point(plotX, y));

            if (i % every != 0) continue;

            // Top of chart is 0 dBm / 100%; bottom is -100 dBm / 0%
            string label = UseRssi
                ? (i * -10).ToString()      // 0, -10, …, -100
                : ((10 - i) * 10) + "%";    // 100%, 90%, …, 0%

            var ft = MakeText(label, 9, Brushes.Black, ppd);
            // Right-align in the left margin
            dc.DrawText(ft, new Point(plotX - 6 - ft.Width, y - ft.Height / 2));
        }
    }

    // ── X-axis time labels ────────────────────────────────────────────────────

    private void DrawXAxisLabels(DrawingContext dc, IReadOnlyList<SignalHistory> history,
        double plotX, double plotY, double plotW, double plotH, double ppd)
    {
        int shown   = Math.Min(history.Count, Mode == GraphMode.Line ? 50 : (int)plotW);
        if (shown < 2) return;

        int    labelCount = Math.Min(5, shown);
        double spacing    = plotW / Math.Max(shown - 1, 1);
        double labelY     = plotY + plotH + 4;

        for (int li = 0; li < labelCount; li++)
        {
            // dataIdx 0 = newest = rightmost; dataIdx shown-1 = oldest = leftmost
            int    dataIdx = (int)Math.Round(li * (shown - 1.0) / (labelCount - 1));
            double x       = plotX + plotW - spacing * dataIdx;

            var    ts    = history[dataIdx].Timestamp;
            string label = ts == default ? "" : ts.ToString("HH:mm:ss");
            var    ft    = MakeText(label, 8, Brushes.Black, ppd);

            // Tick below the plot border
            dc.DrawLine(TickPen, new Point(x, plotY + plotH), new Point(x, plotY + plotH + 3));

            // Center text under tick, clamped to stay within visible width
            double tx = Math.Clamp(x - ft.Width / 2, 0, plotX + plotW - ft.Width);
            dc.DrawText(ft, new Point(tx, labelY));
        }
    }

    // ── Line graph ────────────────────────────────────────────────────────────

    private void DrawLineGraph(DrawingContext dc, IReadOnlyList<SignalHistory> history,
        double plotX, double plotY, double plotW, double plotH)
    {
        const int MaxPoints = 50;
        var    pts     = history.Take(MaxPoints).ToList();
        double spacing = plotW / Math.Max(MaxPoints - 1, 1);

        Point?    prev     = null;
        DateTime? prevTime = null;

        for (int i = 0; i < pts.Count; i++)
        {
            var    entry = pts[i];
            double x     = plotX + plotW - spacing * i;
            double y     = SignalToY(entry, plotY, plotH);

            if (i > 0 && GraphDeadTime && prevTime.HasValue)
            {
                double gap = (prevTime.Value - entry.Timestamp).TotalSeconds;
                if (gap >= 2)
                {
                    double deadY = plotY + plotH;
                    if (prev.HasValue)
                        dc.DrawLine(DeadLine, prev.Value, new Point(x, deadY));
                    prev = new Point(x, deadY);
                }
            }

            var pt = new Point(x, y);
            if (prev.HasValue)
                dc.DrawLine(SignalLine, prev.Value, pt);

            // Dot — larger for the most-recent (newest) point
            double r = i == 0 ? 3.5 : 2.0;
            dc.DrawEllipse(DotFill, null, pt, r, r);

            prev     = pt;
            prevTime = entry.Timestamp;
        }
    }

    // ── Bar graph ─────────────────────────────────────────────────────────────

    private void DrawBarGraph(DrawingContext dc, IReadOnlyList<SignalHistory> history,
        double plotX, double plotY, double plotW, double plotH)
    {
        int      maxBars   = (int)plotW;
        var      pts       = history.Take(maxBars).ToList();
        double   barW      = plotW / Math.Max(maxBars, 1);
        DateTime? prevTime = null;
        int      gapOffset = 0;

        for (int i = 0; i < pts.Count; i++)
        {
            var entry = pts[i];

            if (i > 0 && GraphDeadTime && prevTime.HasValue)
            {
                double gap = (prevTime.Value - entry.Timestamp).TotalSeconds;
                if (gap >= 2) gapOffset += (int)gap;
            }

            int col = i + gapOffset;
            if (col >= maxBars) break;

            double x    = plotX + plotW - barW * (col + 1);
            double sigY = SignalToY(entry, plotY, plotH);
            double botY = plotY + plotH;

            if (botY > sigY)
                dc.DrawRectangle(BarFill, null,
                    new Rect(x, sigY, Math.Max(barW - 0.5, 0.5), botY - sigY));

            prevTime = entry.Timestamp;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private double SignalToY(SignalHistory entry, double plotY, double plotH)
    {
        double fraction = UseRssi
            ? Math.Clamp((entry.Rssi + 100.0) / 100.0, 0, 1)
            : Math.Clamp(entry.Signal / 100.0, 0, 1);
        return plotY + plotH * (1.0 - fraction);
    }

    private static FormattedText MakeText(string text, double size, Brush fg, double ppd)
        => new FormattedText(text, CultureInfo.InvariantCulture,
               FlowDirection.LeftToRight, AxisFont, size, fg, ppd);
}
