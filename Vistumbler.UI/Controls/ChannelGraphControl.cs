using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Vistumbler.UI.Controls;

public enum GraphBand { TwoPointFourGHz, FiveGHz, SixGHz }

/// <summary>
/// One AP entry to be rendered on the channel graph.
/// FreqMhz is the center frequency; HalfWidthMhz is half the channel width.
/// Fill/Stroke are pre-assigned by the window based on BSSID color hash.
/// </summary>
public record ChannelEntry(
    string Ssid,
    int FreqMhz,
    int HalfWidthMhz,
    int Signal,      // 0–100 %
    int Rssi,        // dBm (typically -100 to 0)
    Brush Fill,
    Pen Stroke);

/// <summary>
/// WPF OnRender channel graph showing AP bell curves plotted by center frequency.
///
/// • 2.4 GHz – channels 1-14, non-overlapping channels 1/6/11 highlighted.
/// • 5 GHz   – channels 36-177 (5150-5925 MHz).
/// • 6 GHz   – channels 1-233 (5925-7125 MHz, Wi-Fi 6E / Wi-Fi 7).
///
/// Each AP is drawn as a smooth filled bell curve whose width reflects the
/// channel's occupied bandwidth (inferred from RadioType by the window).
/// Stronger signals are drawn on top of weaker ones.
/// </summary>
public class ChannelGraphControl : FrameworkElement
{
    // ── Dependency Properties ─────────────────────────────────────────────────

    public static readonly DependencyProperty BandProperty =
        DependencyProperty.Register(nameof(Band), typeof(GraphBand), typeof(ChannelGraphControl),
            new FrameworkPropertyMetadata(GraphBand.TwoPointFourGHz, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EntriesProperty =
        DependencyProperty.Register(nameof(Entries), typeof(IReadOnlyList<ChannelEntry>),
            typeof(ChannelGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UseRssiProperty =
        DependencyProperty.Register(nameof(UseRssi), typeof(bool), typeof(ChannelGraphControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public GraphBand Band
    {
        get => (GraphBand)GetValue(BandProperty);
        set => SetValue(BandProperty, value);
    }
    public IReadOnlyList<ChannelEntry>? Entries
    {
        get => (IReadOnlyList<ChannelEntry>?)GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }
    public bool UseRssi
    {
        get => (bool)GetValue(UseRssiProperty);
        set => SetValue(UseRssiProperty, value);
    }

    // ── Layout constants ──────────────────────────────────────────────────────

    private const double LeftBorder   = 46;
    private const double TopBorder    =  6;
    private const double RightBorder  =  8;
    private const double BottomBorder = 28;   // room for rotated channel labels

    // ── Band definitions ──────────────────────────────────────────────────────

    private record BandDef(
        int FreqMin, int FreqMax,
        (int Freq, string Label)[] Markers,
        int[] HighlightFreqs);   // center freqs of non-overlapping preferred channels

    private static readonly BandDef Def24 = new(
        2400, 2496,
        new[]
        {
            (2412, "1"),  (2417, "2"),  (2422, "3"),  (2427, "4"),
            (2432, "5"),  (2437, "6"),  (2442, "7"),  (2447, "8"),
            (2452, "9"),  (2457, "10"), (2462, "11"), (2467, "12"),
            (2472, "13"), (2484, "14")
        },
        new[] { 2412, 2437, 2462 }   // channels 1, 6, 11
    );

    private static readonly BandDef Def5 = new(
        5150, 5990,
        new[]
        {
            (5180,"36"),  (5200,"40"),  (5220,"44"),  (5240,"48"),
            (5260,"52"),  (5280,"56"),  (5300,"60"),  (5320,"64"),
            (5500,"100"), (5520,"104"), (5540,"108"), (5560,"112"),
            (5580,"116"), (5600,"120"), (5620,"124"), (5640,"128"),
            (5660,"132"), (5680,"136"), (5700,"140"), (5720,"144"),
            (5745,"149"), (5765,"153"), (5785,"157"), (5805,"161"),
            (5825,"165"), (5845,"169"), (5865,"173"), (5885,"177")
        },
        Array.Empty<int>()
    );

    private static readonly BandDef Def6 = new(
        5925, 7130,
        new[]
        {
            (5955,"1"),   (5975,"5"),   (5995,"9"),   (6015,"13"),
            (6035,"17"),  (6055,"21"),  (6075,"25"),  (6095,"29"),
            (6115,"33"),  (6135,"37"),  (6155,"41"),  (6175,"45"),
            (6195,"49"),  (6215,"53"),  (6235,"57"),  (6255,"61"),
            (6275,"65"),  (6295,"69"),  (6315,"73"),  (6335,"77"),
            (6355,"81"),  (6375,"85"),  (6395,"89"),  (6415,"93"),
            (6435,"97"),  (6455,"101"), (6475,"105"), (6495,"109"),
            (6515,"113"), (6535,"117"), (6555,"121"), (6575,"125"),
            (6595,"129"), (6615,"133"), (6635,"137"), (6655,"141"),
            (6675,"145"), (6695,"149"), (6715,"153"), (6735,"157"),
            (6755,"161"), (6775,"165"), (6795,"169"), (6815,"173"),
            (6835,"177"), (6855,"181"), (6875,"185"), (6895,"189"),
            (6915,"193"), (6935,"197"), (6955,"201"), (6975,"205"),
            (6995,"209"), (7015,"213"), (7035,"217"), (7055,"221"),
            (7075,"225"), (7095,"229"), (7115,"233")
        },
        Array.Empty<int>()
    );

    // ── Drawing resources ─────────────────────────────────────────────────────

    private static readonly Brush OuterBrush     = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xAA));
    private static readonly Brush PlotBrush      = new SolidColorBrush(Color.FromRgb(0xF8, 0xF8, 0xF0));
    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromArgb(30, 0, 180, 0));
    private static readonly Pen   BorderPen      = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x44)), 1);
    private static readonly Pen   TickPen        = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x44)), 1);
    private static readonly Pen   GridMajor      = new Pen(new SolidColorBrush(Color.FromArgb(150, 0xAA, 0xAA, 0x77)), 1);
    private static readonly Pen   GridMinor      = new Pen(new SolidColorBrush(Color.FromArgb( 60, 0xAA, 0xAA, 0x77)), 1);
    private static readonly Pen   ChanLinePen    = new Pen(new SolidColorBrush(Color.FromArgb( 80, 0x77, 0x77, 0x44)), 1);
    private static readonly Typeface AxisFont    = new Typeface("Segoe UI");
    private static readonly Brush LabelTextBrush = Brushes.DarkSlateBlue;
    private static readonly Brush LabelBgBrush   = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255));

    static ChannelGraphControl()
    {
        OuterBrush.Freeze(); PlotBrush.Freeze(); HighlightBrush.Freeze();
        BorderPen.Freeze();  TickPen.Freeze();   GridMajor.Freeze();
        GridMinor.Freeze();  ChanLinePen.Freeze();
        LabelBgBrush.Freeze();
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
        if (plotW < 10 || plotH < 10) return;

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var def = BandFor(Band);

        // Outer background
        dc.DrawRectangle(OuterBrush, null, new Rect(0, 0, w, h));

        // Plot area background
        dc.DrawRectangle(PlotBrush, null, new Rect(plotX, plotY, plotW, plotH));

        // 2.4 GHz — highlight non-overlapping channels (1, 6, 11)
        if (Band == GraphBand.TwoPointFourGHz)
            DrawNonOverlapHighlights(dc, def, plotX, plotY, plotW, plotH);

        // Horizontal Y grid
        DrawYGrid(dc, plotX, plotY, plotW, plotH);

        // Vertical channel marker lines
        DrawChannelLines(dc, def, plotX, plotY, plotW, plotH);

        // Clip data to plot interior
        dc.PushClip(new RectangleGeometry(new Rect(plotX + 1, plotY + 1, plotW - 2, plotH - 2)));

        var entries = Entries;
        if (entries != null && entries.Count > 0)
        {
            // Draw weakest first so strongest appears on top
            foreach (var e in entries.OrderBy(e => UseRssi ? e.Rssi : e.Signal))
                DrawBellCurve(dc, e, def, plotX, plotY, plotW, plotH);

            // Draw SSID labels in a second pass (always on top of all curves)
            foreach (var e in entries.OrderBy(e => UseRssi ? e.Rssi : e.Signal))
                DrawSsidLabel(dc, e, def, plotX, plotY, plotW, plotH, ppd);
        }

        dc.Pop(); // pop clip

        // Y-axis labels + ticks
        DrawYAxisLabels(dc, plotX, plotY, plotH, ppd);

        // X-axis channel labels + ticks
        DrawChannelLabels(dc, def, plotX, plotY, plotW, plotH, ppd);

        // Plot border (drawn last)
        dc.DrawRectangle(null, BorderPen, new Rect(plotX, plotY, plotW, plotH));
    }

    // ── Non-overlapping channel highlights ───────────────────────────────────

    private void DrawNonOverlapHighlights(DrawingContext dc, BandDef def,
        double plotX, double plotY, double plotW, double plotH)
    {
        double halfHz = 11.0;   // ±11 MHz for a 22 MHz 2.4 GHz channel band
        foreach (int cf in def.HighlightFreqs)
        {
            double x1 = FreqToX(cf - (int)halfHz, plotX, plotW, def.FreqMin, def.FreqMax);
            double x2 = FreqToX(cf + (int)halfHz, plotX, plotW, def.FreqMin, def.FreqMax);
            x1 = Math.Max(x1, plotX);
            x2 = Math.Min(x2, plotX + plotW);
            if (x2 > x1)
                dc.DrawRectangle(HighlightBrush, null, new Rect(x1, plotY, x2 - x1, plotH));
        }
    }

    // ── Y grid ────────────────────────────────────────────────────────────────

    private static void DrawYGrid(DrawingContext dc,
        double plotX, double plotY, double plotW, double plotH)
    {
        for (int i = 0; i <= 10; i++)
        {
            double y = plotY + plotH * (i / 10.0);
            dc.DrawLine(i % 2 == 0 ? GridMajor : GridMinor,
                new Point(plotX, y), new Point(plotX + plotW, y));
        }
    }

    // ── Vertical channel marker lines ─────────────────────────────────────────

    private void DrawChannelLines(DrawingContext dc, BandDef def,
        double plotX, double plotY, double plotW, double plotH)
    {
        foreach (var (freq, _) in def.Markers)
        {
            double x = FreqToX(freq, plotX, plotW, def.FreqMin, def.FreqMax);
            if (x < plotX || x > plotX + plotW) continue;
            dc.DrawLine(ChanLinePen, new Point(x, plotY), new Point(x, plotY + plotH));
        }
    }

    // ── Bell curve ────────────────────────────────────────────────────────────

    private void DrawBellCurve(DrawingContext dc, ChannelEntry e, BandDef def,
        double plotX, double plotY, double plotW, double plotH)
    {
        double xCenter = FreqToX(e.FreqMhz, plotX, plotW, def.FreqMin, def.FreqMax);
        double xLeft   = FreqToX(e.FreqMhz - e.HalfWidthMhz, plotX, plotW, def.FreqMin, def.FreqMax);
        double xRight  = FreqToX(e.FreqMhz + e.HalfWidthMhz, plotX, plotW, def.FreqMin, def.FreqMax);
        double yTop    = SignalToY(e, plotY, plotH);
        double yBottom = plotY + plotH;

        // Ensure minimum visible width
        double minHalf = 3;
        if (xCenter - xLeft < minHalf) { xLeft = xCenter - minHalf; xRight = xCenter + minHalf; }

        double halfW  = xCenter - xLeft;
        double height = yBottom - yTop;
        if (height < 1) return;

        // Cubic bezier control points for smooth bell arch:
        //   – sides are nearly vertical near the bottom, flatten toward the peak
        double cy1 = yTop + height * 0.25;   // near-top control for steepness
        double cx1L = xLeft  + halfW * 0.35; // horizontal spread of left shoulder
        double cx1R = xRight - halfW * 0.35; // horizontal spread of right shoulder

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(xLeft, yBottom), isFilled: true, isClosed: true);

            // Left side: bottom-left → peak
            ctx.BezierTo(
                new Point(xLeft,  cy1),   // pull straight up
                new Point(cx1L,   yTop),  // flatten near top
                new Point(xCenter, yTop),
                isStroked: true, isSmoothJoin: true);

            // Right side: peak → bottom-right
            ctx.BezierTo(
                new Point(cx1R,   yTop),  // flatten near top
                new Point(xRight, cy1),   // pull straight down
                new Point(xRight, yBottom),
                isStroked: true, isSmoothJoin: true);
            // Closing segment (xRight,yBottom) → (xLeft,yBottom) is added automatically by isClosed
        }
        geo.Freeze();

        dc.DrawGeometry(e.Fill, e.Stroke, geo);
    }

    // ── SSID label ────────────────────────────────────────────────────────────

    private void DrawSsidLabel(DrawingContext dc, ChannelEntry e, BandDef def,
        double plotX, double plotY, double plotW, double plotH, double ppd)
    {
        double xCenter = FreqToX(e.FreqMhz, plotX, plotW, def.FreqMin, def.FreqMax);
        double xLeft   = FreqToX(e.FreqMhz - e.HalfWidthMhz, plotX, plotW, def.FreqMin, def.FreqMax);
        double xRight  = FreqToX(e.FreqMhz + e.HalfWidthMhz, plotX, plotW, def.FreqMin, def.FreqMax);
        double yTop    = SignalToY(e, plotY, plotH);

        double curveW = xRight - xLeft;
        if (curveW < 10) return;  // too narrow to bother

        var ft = MakeText(e.Ssid, 8.5, LabelTextBrush, ppd);
        // Clip label to curve width
        double maxLabelW = Math.Max(curveW - 4, 10);
        if (ft.Width > maxLabelW) ft.MaxTextWidth = maxLabelW;

        double labelX = xCenter - ft.Width  / 2;
        double labelY = yTop - ft.Height - 2;

        // Keep within plot area vertically
        if (labelY < plotY) labelY = yTop + 2;

        // Semi-transparent background pill
        var bgRect = new Rect(labelX - 2, labelY - 1, ft.Width + 4, ft.Height + 2);
        dc.DrawRoundedRectangle(LabelBgBrush, null, bgRect, 2, 2);

        dc.DrawText(ft, new Point(labelX, labelY));
    }

    // ── Y-axis labels ─────────────────────────────────────────────────────────

    private void DrawYAxisLabels(DrawingContext dc,
        double plotX, double plotY, double plotH, double ppd)
    {
        int every = plotH > 160 ? 1 : plotH > 90 ? 2 : 5;

        for (int i = 0; i <= 10; i++)
        {
            double y = plotY + plotH * (i / 10.0);
            dc.DrawLine(TickPen, new Point(plotX - 4, y), new Point(plotX, y));

            if (i % every != 0) continue;

            string label = UseRssi
                ? (i * -10).ToString()
                : ((10 - i) * 10) + "%";

            var ft = MakeText(label, 9, Brushes.Black, ppd);
            dc.DrawText(ft, new Point(plotX - 6 - ft.Width, y - ft.Height / 2));
        }
    }

    // ── X-axis channel labels ─────────────────────────────────────────────────

    private void DrawChannelLabels(DrawingContext dc, BandDef def,
        double plotX, double plotY, double plotW, double plotH, double ppd)
    {
        bool rotate = Band != GraphBand.TwoPointFourGHz;
        double minSpacing = rotate ? 22 : 28;
        double labelBaseY = plotY + plotH;
        double prevX = double.NegativeInfinity;

        foreach (var (freq, label) in def.Markers)
        {
            double x = FreqToX(freq, plotX, plotW, def.FreqMin, def.FreqMax);
            if (x < plotX || x > plotX + plotW) continue;
            if (x - prevX < minSpacing) continue;

            // Tick mark
            dc.DrawLine(TickPen,
                new Point(x, labelBaseY),
                new Point(x, labelBaseY + 3));

            var ft = MakeText(label, 8, Brushes.Black, ppd);
            if (rotate)
                DrawRotatedLabel(dc, ft, x, labelBaseY + 4);
            else
                dc.DrawText(ft, new Point(x - ft.Width / 2, labelBaseY + 4));

            prevX = x;
        }
    }

    /// <summary>Draws text rotated -90° (bottom-to-top) with its baseline at xTick.</summary>
    private static void DrawRotatedLabel(DrawingContext dc, FormattedText ft,
        double xTick, double yBase)
    {
        // Rotate -90° around the baseline origin so text reads upward
        double cx = xTick;
        double cy = yBase + ft.Width / 2;
        dc.PushTransform(new RotateTransform(-90, cx, cy));
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
        dc.Pop();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private double SignalToY(ChannelEntry e, double plotY, double plotH)
    {
        double frac = UseRssi
            ? Math.Clamp((e.Rssi + 100.0) / 100.0, 0, 1)
            : Math.Clamp(e.Signal / 100.0, 0, 1);
        return plotY + plotH * (1.0 - frac);
    }

    private static double FreqToX(int freqMhz, double plotX, double plotW, int freqMin, int freqMax)
        => plotX + plotW * (freqMhz - freqMin) / (double)(freqMax - freqMin);

    private static BandDef BandFor(GraphBand band) => band switch
    {
        GraphBand.FiveGHz    => Def5,
        GraphBand.SixGHz     => Def6,
        _                    => Def24
    };

    private static FormattedText MakeText(string text, double size, Brush fg, double ppd)
        => new FormattedText(text, CultureInfo.InvariantCulture,
               FlowDirection.LeftToRight, AxisFont, size, fg, ppd);
}
