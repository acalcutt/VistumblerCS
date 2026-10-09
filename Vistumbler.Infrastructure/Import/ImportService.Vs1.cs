using System.Globalization;
using Vistumbler.Core.Models;

namespace Vistumbler.Infrastructure.Import;

/// <summary>
/// The VS1 reader, a port of the original Vistumbler's _ImportVS1: GPS lines (12 fields, or 6 in old files) first,
/// then AP lines (15 fields in v4, 13 in v3) whose GID,SIGNAL,RSSI history points at those GPS lines, so each AP
/// comes in with its readings, positions and first/last seen times.
/// <para>
/// Detailed Export Version 4.1 (VistumblerMAUI) adds 10-field lines for cell towers and Bluetooth devices after the
/// APs. This app has nowhere to keep those, so they're recognised and counted (<see cref="SkippedRadioNetworks"/>)
/// but not imported; the file's APs and GPS import as usual.
/// </para>
/// </summary>
public partial class ImportService
{
    /// <summary>Cell towers and Bluetooth devices (VS1 4.1) in the last VS1 read, which this app doesn't keep.</summary>
    public int SkippedRadioNetworks { get; private set; }

    private static readonly HashSet<string> RadioTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BT", "BLE", "GSM", "CDMA", "WCDMA", "LTE", "NR",
    };

    private List<AccessPoint> ReadVs1(string[] lines)
    {
        SkippedRadioNetworks = 0;

        // Pass 1: the GPS lines, by their id in the file
        var gps = new Dictionary<int, GpsData>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if ((p.Length == 12 || p.Length == 6) && int.TryParse(p[0], out var gid) && ParseVs1Gps(p) is { } g)
                gps[gid] = g;
        }

        // Pass 2: the APs (and counting the cell towers and Bluetooth devices this app can't keep)
        var aps = new List<AccessPoint>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length == 10 && RadioTypes.Contains(p[0].Trim())) { SkippedRadioNetworks++; continue; }     // 4.1
            AccessPoint? ap = p.Length switch
            {
                15 when IsMacAddress(p[1].Trim()) => ParseVs1ApLine(p, gps, v4: true),
                13 when IsMacAddress(p[1].Trim()) => ParseVs1ApLine(p, gps, v4: false),
                >= 19 when p[0] == "AP" => ParseVs1Line(p),
                _ => null,
            };
            if (ap is not null) aps.Add(ap);
        }
        return aps;
    }

    private static GpsData? ParseVs1Gps(string[] p)
    {
        double? lat = DmmToDecimal(p[1]), lon = DmmToDecimal(p[2]);
        if (lat is null || lon is null || (lat == 0 && lon == 0)) return null;   // no fix
        bool full = p.Length == 12;
        if (ParseVs1Time(full ? p[10] : p[4], full ? p[11] : p[5]) is not { } when) return null;
        var inv = CultureInfo.InvariantCulture;
        var g = new GpsData
        {
            Latitude = lat.Value,
            Longitude = lon.Value,
            NumberOfSatellites = int.TryParse(p[3], out var sats) ? sats : 0,
            Timestamp = when,
            Quality = GpsQuality.GpsFix,
        };
        if (full)
        {
            if (double.TryParse(p[4], NumberStyles.Float, inv, out var hdop)) g.HorizontalDilution = hdop;
            if (double.TryParse(p[5], NumberStyles.Float, inv, out var alt)) g.Altitude = alt;
            if (double.TryParse(p[7], NumberStyles.Float, inv, out var kmh)) g.SpeedKnots = kmh / 1.852;
            if (double.TryParse(p[9], NumberStyles.Float, inv, out var track)) g.TrackAngle = track;
        }
        return g;
    }

    // "2024-05-01" or the old "05-01-2024", with "12:34:56.789" or "12:34:56", in UTC
    private static DateTime? ParseVs1Time(string date, string time)
    {
        var d = date.Trim().Split('-');
        if (d.Length != 3) return null;
        string iso = d[0].Length == 4 ? date.Trim() : $"{d[2]}-{d[0].PadLeft(2, '0')}-{d[1].PadLeft(2, '0')}";
        return DateTime.TryParse($"{iso} {time.Trim()}", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }

    // "N 4807.0380" (degrees and minutes, the original's _Format_GPS_DMM) → signed decimal degrees
    private static double? DmmToDecimal(string text)
    {
        text = text.Trim();
        if (text.Length < 2) return null;
        char hemi = char.ToUpperInvariant(text[0]);
        if (hemi is not ('N' or 'S' or 'E' or 'W')) return null;
        if (!double.TryParse(text[1..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dmm)) return null;
        double deg = Math.Floor(dmm / 100);
        double value = deg + (dmm - deg * 100) / 60;
        return hemi is 'S' or 'W' ? -value : value;
    }

    // v4: SSID|BSSID|MANUF|Auth|Encr|SecType|Radio|Chan|BasicRates|OtherRates|HighSig|HighRSSI|NetType|Label|GID,SIG,RSSI\…
    // v3: SSID|BSSID|MANUF|Auth|Encr|SecType|Radio|Chan|BasicRates|OtherRates|NetType|Label|GID,SIG-GID,SIG…
    private AccessPoint? ParseVs1ApLine(string[] p, Dictionary<int, GpsData> gps, bool v4)
    {
        var ap = new AccessPoint
        {
            Ssid = p[0].Trim(),
            Bssid = p[1].Trim(),
            Manufacturer = p[2].Trim(),
            Authentication = ParseAuthentication(p[3].Trim()),
            Encryption = ParseEncryption(p[4].Trim()),
            RadioType = p[6].Trim(),
            Channel = ParseInt(p[7].Trim()),
            BasicTransferRates = p[8].Trim(),
            OtherTransferRates = p[9].Trim(),
        };
        string netType = (v4 ? p[12] : p[10]).Trim();
        ap.NetworkType = netType.Contains("Ad", StringComparison.OrdinalIgnoreCase) ? NetworkType.Adhoc
            : Enum.TryParse<NetworkType>(netType, true, out var nt) ? nt : NetworkType.Infrastructure;
        string label = (v4 ? p[13] : p[11]).Trim();
        ap.Label = label == "Unknown" ? string.Empty : label;

        // The readings, each placed and timed by its GPS line (shared, so a fix several APs were heard at is saved once)
        foreach (var entry in (v4 ? p[14] : p[12]).Trim().Split(v4 ? '\\' : '-', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = entry.Split(',');
            if (f.Length < 2 || !int.TryParse(f[0], out var gid) || !gps.TryGetValue(gid, out var g)) continue;
            int signal = ParseInt(f[1].Replace("%", "").Trim());
            int rssi = f.Length >= 3 && int.TryParse(f[2], out var r) ? r : signal / 2 - 100;
            ap.SignalHistory.Add(new SignalHistory { Signal = signal, Rssi = rssi, Timestamp = g.Timestamp, GpsData = g });
        }

        var history = ap.SignalHistory;
        if (history.Count > 0)
        {
            ap.FirstSeen = history.Min(h => h.Timestamp);
            ap.LastSeen = history.Max(h => h.Timestamp);
            var last = history.MaxBy(h => h.Timestamp)!;
            ap.Signal = last.Signal;
            ap.Rssi = last.Rssi;
            var best = history.MaxBy(h => (h.Signal, h.Rssi))!;
            ap.Latitude = best.GpsData!.Latitude;
            ap.Longitude = best.GpsData.Longitude;
        }
        else
        {
            ap.FirstSeen = ap.LastSeen = DateTime.UtcNow;
        }
        ap.HighestSignal = v4 ? ParseInt(p[10].Trim()) : history.Select(h => h.Signal).DefaultIfEmpty(0).Max();
        ap.HighestRssi = v4 ? ParseInt(p[11].Trim()) : history.Select(h => h.Rssi).DefaultIfEmpty(-100).Max();
        ap.Signal ??= ap.HighestSignal;
        ap.Rssi ??= ap.HighestRssi;
        return ap;
    }
}
