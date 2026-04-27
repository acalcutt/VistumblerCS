using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Vistumbler.Core.Enums;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.Controls;

/// <summary>
/// A WPF OnRender-based signal graph that mirrors the GDI+ graph in Vistumbler.au3 _GraphDraw().
///
/// Graph1 (Line) – plots up to 50 most-recent history points connected with lines.
/// Graph2 (Bar)  – one vertical bar per pixel-column of available width.
///
/// Signal history is passed in via the SignalHistory DependencyProperty whenever
/// the selected AP changes.  The control redraws whenever Mode, UseRssi, or
/// SignalHistory changes.
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

    // ── Drawing resources (created once) ─────────────────────────────────────

    private static readonly Brush BackBrush    = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xAA));
    private static readonly Pen   GridPen      = new Pen(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0x88)), 1);
    private static readonly Pen   SignalPen    = new Pen(Brushes.Red, 1.5);
    private static readonly Pen   DeadPen      = new Pen(Brushes.Red, 1.0);
    private static readonly Typeface LabelFont = new Typeface("Segoe UI");

    private const double LeftBorder = 35;
    private const double TopBorder  = 4;
    private const double RightBorder  = 4;
    private const double BottomBorder = 4;

    static SignalGraphControl()
    {
        BackBrush.Freeze();
        GridPen.Freeze();
        SignalPen.Freeze();
        DeadPen.Freeze();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Background
        dc.DrawRectangle(BackBrush, null, new Rect(0, 0, w, h));

        double graphW = w - LeftBorder - RightBorder;
        double graphH = h - TopBorder  - BottomBorder;

        DrawGrid(dc, graphW, graphH);

        if (Mode == GraphMode.Hidden) return;

        var history = SignalHistory;
        if (history == null || history.Count == 0) return;

        if (Mode == GraphMode.Line)
            DrawLineGraph(dc, history, graphW, graphH);
        else
            DrawBarGraph(dc, history, graphW, graphH);
    }

    private void DrawGrid(DrawingContext dc, double graphW, double graphH)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int i = 0; i <= 10; i++)
        {
            double yFrac = i / 10.0;
            double y;
            string label;

            if (UseRssi)
            {
                // dBm: 0 at top → -100 at bottom
                y     = TopBorder + graphH * yFrac;
                label = (i * -10).ToString();
            }
            else
            {
                // %: 100% at top → 0% at bottom
                y     = TopBorder + graphH * (1.0 - yFrac);
                label = (i * 10) + "%";
            }

            dc.DrawLine(GridPen,
                new Point(LeftBorder, y),
                new Point(LeftBorder + graphW, y));

            var ft = new FormattedText(label, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, LabelFont, 9,
                Brushes.Black, pixelsPerDip);
            dc.DrawText(ft, new Point(0, y - ft.Height / 2));
        }
    }

    /// <summary>
    /// Graph1: line chart over the last 50 signal points (newest on the right).
    /// Dead time gaps are drawn at the bottom of the chart in red.
    /// </summary>
    private void DrawLineGraph(DrawingContext dc, IReadOnlyList<SignalHistory> history,
                               double graphW, double graphH)
    {
        const int MaxPoints = 50;

        // Take the most recent MaxPoints entries (already ordered newest-first from the VM)
        var points = history.Take(MaxPoints).ToList();
        if (points.Count < 1) return;

        double spacing = graphW / Math.Max(MaxPoints - 1, 1);

        Point? prev = null;
        DateTime? prevTime = null;

        for (int i = 0; i < points.Count; i++)
        {
            var entry = points[i];
            // x: index 0 = rightmost, index n-1 = leftmost
            double x = LeftBorder + graphW - spacing * i;
            double y = SignalToY(entry, graphH);

            // Dead-time gap between consecutive points
            if (i > 0 && GraphDeadTime && prevTime.HasValue)
            {
                var gap = (prevTime.Value - entry.Timestamp).TotalSeconds;
                if (gap >= 2)
                {
                    // Draw a dead-point at the bottom then connect
                    double deadY = TopBorder + graphH;
                    var deadPt   = new Point(x, deadY);
                    if (prev.HasValue)
                        dc.DrawLine(DeadPen, prev.Value, deadPt);
                    prev = deadPt;
                }
            }

            var pt = new Point(x, y);
            if (prev.HasValue)
                dc.DrawLine(SignalPen, prev.Value, pt);

            // Dot
            dc.DrawRectangle(Brushes.Red, null, new Rect(x - 1.5, y - 1.5, 3, 3));

            prev     = pt;
            prevTime = entry.Timestamp;
        }
    }

    /// <summary>
    /// Graph2: bar chart – one 1-px-wide bar per history entry from right to left.
    /// </summary>
    private void DrawBarGraph(DrawingContext dc, IReadOnlyList<SignalHistory> history,
                              double graphW, double graphH)
    {
        int maxBars = (int)graphW;
        var points  = history.Take(maxBars).ToList();

        double barW = graphW / Math.Max(maxBars, 1);
        DateTime? prevTime = null;
        int gapOffset = 0;

        for (int i = 0; i < points.Count; i++)
        {
            var entry = points[i];

            // Skip columns for dead time
            if (i > 0 && GraphDeadTime && prevTime.HasValue)
            {
                var gap = (prevTime.Value - entry.Timestamp).TotalSeconds;
                if (gap >= 2)
                    gapOffset += (int)gap;
            }

            int col = i + gapOffset;
            if (col >= maxBars) break;

            double x    = LeftBorder + graphW - barW * (col + 1);
            double sigY = SignalToY(entry, graphH);
            double botY = TopBorder + graphH;

            dc.DrawLine(SignalPen, new Point(x, sigY), new Point(x, botY));

            prevTime = entry.Timestamp;
        }
    }

    private double SignalToY(SignalHistory entry, double graphH)
    {
        if (UseRssi)
        {
            // RSSI range -100…0 → map 0 to top, -100 to bottom
            double fraction = (entry.Rssi + 100.0) / 100.0;
            return TopBorder + graphH * (1.0 - Math.Clamp(fraction, 0, 1));
        }
        else
        {
            return TopBorder + graphH * (1.0 - Math.Clamp(entry.Signal / 100.0, 0, 1));
        }
    }
}
