using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Vistumbler.UI.Controls;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class ChannelGraphWindow : Window
{
    // ── Color palette (fill/stroke per AP, assigned by BSSID hash) ───────────

    private static readonly (Color Fill, Color Stroke)[] Palette =
    {
        (Color.FromArgb( 90, 200,  30,  30), Color.FromRgb(170,  0,  0)),  // red
        (Color.FromArgb( 90,  30,  80, 200), Color.FromRgb(  0, 60,180)),  // blue
        (Color.FromArgb( 90,  30, 160,  30), Color.FromRgb(  0,130,  0)),  // green
        (Color.FromArgb( 90, 200, 110,   0), Color.FromRgb(170, 80,  0)),  // orange
        (Color.FromArgb( 90, 140,   0, 180), Color.FromRgb(110,  0,150)),  // purple
        (Color.FromArgb( 90,   0, 160, 160), Color.FromRgb(  0,120,120)),  // teal
        (Color.FromArgb( 90, 200,   0, 130), Color.FromRgb(170,  0,110)),  // magenta
        (Color.FromArgb( 90, 150, 140,   0), Color.FromRgb(120,110,  0)),  // olive
        (Color.FromArgb( 90,   0, 170, 100), Color.FromRgb(  0,140, 70)),  // sea-green
        (Color.FromArgb( 90,  80,  80, 200), Color.FromRgb( 50, 50,160)),  // indigo
    };

    // Pre-built Brush/Pen arrays so we only freeze once
    private static readonly (Brush Fill, Pen Stroke)[] BrushPalette =
        Palette.Select(p =>
        {
            var fill   = new SolidColorBrush(p.Fill);
            var stroke = new Pen(new SolidColorBrush(p.Stroke), 1.5);
            fill.Freeze();
            stroke.Brush.Freeze();
            stroke.Freeze();
            return (fill as Brush, stroke);
        }).ToArray();

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<AccessPointViewModel> _aps;
    private readonly GraphBand _band;
    private readonly DispatcherTimer _timer;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ChannelGraphWindow(
        ObservableCollection<AccessPointViewModel> aps,
        bool useRssi,
        GraphBand band)
    {
        InitializeComponent();

        _aps  = aps;
        _band = band;

        GraphControl.Band    = band;
        GraphControl.UseRssi = useRssi;
        UseRssiToggle.IsChecked = useRssi;

        Title = band switch
        {
            GraphBand.TwoPointFourGHz => "2.4 GHz Channel Graph",
            GraphBand.FiveGHz         => "5 GHz Channel Graph",
            _                         => "6 GHz Channel Graph"
        };

        BandLabel.Text = Title;
        HintLabel.Visibility = band == GraphBand.TwoPointFourGHz
            ? Visibility.Visible : Visibility.Collapsed;

        // Refresh on collection add/remove
        _aps.CollectionChanged += (_, _) => Refresh();

        // Periodic refresh to pick up signal-level changes on existing APs
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Refresh();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    // ── UseRssi toggle ────────────────────────────────────────────────────────

    private void UseRssiToggle_Changed(object sender, RoutedEventArgs e)
    {
        GraphControl.UseRssi = UseRssiToggle.IsChecked == true;
    }

    // ── Entry building ────────────────────────────────────────────────────────

    private void Refresh()
    {
        var entries = BuildEntries();
        GraphControl.Entries = entries;

        int apCount = entries.Count;
        StatusText.Text = apCount == 0
            ? $"No active APs on {Title} band — {DateTime.Now:HH:mm:ss}"
            : $"{apCount} active AP{(apCount == 1 ? "" : "s")} | Updated {DateTime.Now:HH:mm:ss}";
    }

    private List<ChannelEntry> BuildEntries()
    {
        var list = new List<ChannelEntry>();

        foreach (var ap in _aps)
        {
            if (!ap.IsActive) continue;

            int freq = ResolveFreq(ap);
            if (freq == 0) continue;
            if (!IsInBand(freq)) continue;

            int halfWidth = InferHalfWidthMhz(ap.RadioType, freq);
            var (fill, stroke) = ColorForBssid(ap.Bssid);

            list.Add(new ChannelEntry(
                Ssid:         ap.Ssid,
                FreqMhz:      freq,
                HalfWidthMhz: halfWidth,
                Signal:       ap.Signal    ?? 0,
                Rssi:         ap.Rssi      ?? -100,
                Fill:         fill,
                Stroke:       stroke));
        }

        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the center frequency in MHz. Falls back to channel arithmetic if not stored.</summary>
    private static int ResolveFreq(AccessPointViewModel ap)
    {
        if (ap.FrequencyMhz > 0) return ap.FrequencyMhz;

        // Fallback: derive from channel
        if (ap.Channel == 14) return 2484;
        if (ap.Channel >= 1 && ap.Channel <= 13) return 2407 + ap.Channel * 5;
        if (ap.Channel >= 32 && ap.Channel <= 177) return 5000 + ap.Channel * 5;
        if (ap.Channel >= 1 && ap.Channel <= 233) return 5950 + ap.Channel * 5; // ambiguous — assume 6 GHz
        return 0;
    }

    private bool IsInBand(int freqMhz) => _band switch
    {
        GraphBand.TwoPointFourGHz => freqMhz is >= 2400 and <= 2500,
        GraphBand.FiveGHz         => freqMhz is >= 5150 and < 5925,
        _                         => freqMhz is >= 5925 and <= 7200,
    };

    /// <summary>
    /// Infers the half channel-width in MHz from RadioType and band.
    /// This is necessarily approximate — Windows doesn't expose negotiated bandwidth
    /// via the public WLAN API without driver-specific extensions.
    /// </summary>
    private static int InferHalfWidthMhz(string radioType, int freqMhz)
    {
        bool is6   = freqMhz >= 5925;
        bool is5   = freqMhz is >= 5150 and < 5925;

        // Wi-Fi 7 (802.11be) — 160 MHz channels typical; use 80 MHz half-width
        if (radioType.Contains("be", StringComparison.OrdinalIgnoreCase))
            return 80;

        // Wi-Fi 6/6E (802.11ax)
        if (radioType.Contains("ax", StringComparison.OrdinalIgnoreCase))
            return is6 ? 40 : is5 ? 40 : 20;  // 80 / 80 / 40 MHz

        // Wi-Fi 5 (802.11ac) — 80 MHz typical
        if (radioType.Contains("ac", StringComparison.OrdinalIgnoreCase))
            return 40;

        // Wi-Fi 4 (802.11n) — 40 MHz typical
        if (radioType.Contains("n", StringComparison.OrdinalIgnoreCase))
            return 20;

        // 802.11a/b/g — 20 MHz (DSSS has 22 MHz, use 11)
        return 11;
    }

    private static (Brush fill, Pen stroke) ColorForBssid(string bssid)
    {
        int hash = 0;
        foreach (char c in bssid) hash = hash * 31 + c;
        return BrushPalette[Math.Abs(hash) % BrushPalette.Length];
    }
}
