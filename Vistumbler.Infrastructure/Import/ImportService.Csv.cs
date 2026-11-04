using System.Globalization;
using Vistumbler.Core.Models;

namespace Vistumbler.Infrastructure.Import;

/// <summary>
/// Vistumbler's own CSVs, a port of the original Vistumbler's _ImportCSV, which tells them apart by their column count:
/// <list type="bullet">
/// <item>Detailed v2 (26 columns, what the original, VistumblerMAUI and this app write): a row per reading, with its
/// signal, RSSI and GPS fix.</item>
/// <item>Detailed v1 (23 columns, older Vistumblers): the same without High Signal, RSSI and High RSSI.</item>
/// <item>Summary (16 or 18 columns): a row per AP, at its strongest position, with its first and last seen.</item>
/// </list>
/// Older versions of this app wrote a CSV of their own ("BSSID,SSID,Channel,…"), still read by its header.
/// </summary>
public partial class ImportService
{
    private const string OldCsvHeader = "BSSID,SSID,Channel";

    private List<AccessPoint> ReadVistumblerCsv(string[] lines)
    {
        var accessPoints = new List<AccessPoint>();
        bool oldCsv = lines[0].StartsWith(OldCsvHeader, StringComparison.OrdinalIgnoreCase);
        var byBssid = new Dictionary<string, AccessPoint>(StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var p = SplitCsvLine(lines[i]);
            if (oldCsv)
            {
                if (ParseDetailedCsvLine(p) is { } old) accessPoints.Add(old);
                continue;
            }
            switch (p.Length)
            {
                case 26: AddCsvReading(byBssid, accessPoints, p, v2: true); break;
                case 23: AddCsvReading(byBssid, accessPoints, p, v2: false); break;
                case 16 or 18: if (ParseCsvSummaryLine(p) is { } ap) accessPoints.Add(ap); break;
            }
        }

        foreach (var ap in byBssid.Values)
        {
            if (ap.SignalHistory.Count == 0) continue;
            ap.FirstSeen = ap.SignalHistory.Min(h => h.Timestamp);
            ap.LastSeen = ap.SignalHistory.Max(h => h.Timestamp);
            var last = ap.SignalHistory.MaxBy(h => h.Timestamp)!;
            ap.Signal = last.Signal;
            ap.Rssi = last.Rssi;
            // where it was strongest
            if (ap.SignalHistory.Where(h => h.GpsData is not null).MaxBy(h => h.Signal) is { GpsData: { } g })
            {
                ap.Latitude = g.Latitude;
                ap.Longitude = g.Longitude;
            }
        }
        return accessPoints;
    }

    // SSID,BSSID,MANUFACTURER,SIGNAL,[High Signal,RSSI,High RSSI,]AUTHENTICATION,ENCRYPTION,RADIO TYPE,CHANNEL,BTX,OTX,
    // NETWORK TYPE,LABEL,LATITUDE,LONGITUDE,SATELLITES,HDOP,ALTITUDE,HEIGHT OF GEOID,SPEED(km/h),SPEED(MPH),TRACK ANGLE,
    // DATE(UTC),TIME(UTC)
    private void AddCsvReading(Dictionary<string, AccessPoint> byBssid, List<AccessPoint> accessPoints, string[] p, bool v2)
    {
        var bssid = p[1].Trim();
        if (!IsMacAddress(bssid)) return;   // the header row, or a broken one
        int o = v2 ? 3 : 0;                  // the three columns v2 added after SIGNAL

        int signal = ParseInt(p[3]);
        int rssi = v2 ? ParseInt(p[5]) : SignalToRssi(signal);
        if (!byBssid.TryGetValue(bssid, out var ap))
        {
            ap = new AccessPoint
            {
                Bssid = bssid,
                Ssid = p[0],
                Manufacturer = p[2],
                Authentication = ParseAuthentication(p[4 + o]),
                Encryption = ParseEncryption(p[5 + o]),
                RadioType = p[6 + o],
                Channel = ParseInt(p[7 + o]),
                BasicTransferRates = p[8 + o],
                OtherTransferRates = p[9 + o],
                NetworkType = ParseCsvNetworkType(p[10 + o]),
                Label = p[11 + o],
            };
            byBssid[bssid] = ap;
            accessPoints.Add(ap);
        }
        ap.HighestSignal = Math.Max(ap.HighestSignal ?? 0, v2 ? ParseInt(p[4]) : signal);
        ap.HighestRssi = Math.Max(ap.HighestRssi ?? -100, v2 ? ParseInt(p[6]) : rssi);

        var when = ParseCsvTime(p[21 + o], p[22 + o]) ?? DateTime.UtcNow;
        var gps = CsvGps(p[12 + o], p[13 + o], when);
        if (gps is not null)
        {
            var inv = CultureInfo.InvariantCulture;
            gps.NumberOfSatellites = ParseInt(p[14 + o]);
            if (double.TryParse(p[15 + o], NumberStyles.Float, inv, out var hdop)) gps.HorizontalDilution = hdop;
            if (double.TryParse(p[16 + o], NumberStyles.Float, inv, out var alt)) gps.Altitude = alt;
            if (double.TryParse(p[18 + o], NumberStyles.Float, inv, out var kmh)) gps.SpeedKnots = kmh / 1.852;
            if (double.TryParse(p[20 + o], NumberStyles.Float, inv, out var track)) gps.TrackAngle = track;
        }
        ap.SignalHistory.Add(new SignalHistory { Signal = signal, Rssi = rssi, Timestamp = when, GpsData = gps });
    }

    // SSID,BSSID,MANUFACTURER,HIGHEST SIGNAL W/GPS,AUTHENTICATION,ENCRYPTION,RADIO TYPE,CHANNEL,LATITUDE,LONGITUDE,BTX,
    // OTX,FIRST SEEN(UTC),LAST SEEN(UTC),NETWORK TYPE,LABEL[,HIGHEST SIGNAL,HIGHEST RSSI]
    private AccessPoint? ParseCsvSummaryLine(string[] p)
    {
        var bssid = p[1].Trim();
        if (!IsMacAddress(bssid)) return null;
        int gpsSignal = ParseInt(p[3]);
        var first = ParseCsvDateTime(p[12]) ?? DateTime.UtcNow;
        var last = ParseCsvDateTime(p[13]) ?? first;
        var ap = new AccessPoint
        {
            Bssid = bssid,
            Ssid = p[0],
            Manufacturer = p[2],
            Authentication = ParseAuthentication(p[4]),
            Encryption = ParseEncryption(p[5]),
            RadioType = p[6],
            Channel = ParseInt(p[7]),
            BasicTransferRates = p[10],
            OtherTransferRates = p[11],
            FirstSeen = first,
            LastSeen = last,
            NetworkType = ParseCsvNetworkType(p[14]),
            Label = p[15],
            HighestSignal = p.Length == 18 ? ParseInt(p[16]) : gpsSignal,
            HighestRssi = p.Length == 18 ? ParseInt(p[17]) : SignalToRssi(gpsSignal),
        };
        ap.Signal = ap.HighestSignal;
        ap.Rssi = ap.HighestRssi;
        // the strongest reading with a position, as the original keeps it: one reading at the first-seen time
        var gps = CsvGps(p[8], p[9], first);
        if (gps is not null)
        {
            ap.Latitude = gps.Latitude;
            ap.Longitude = gps.Longitude;
        }
        ap.SignalHistory.Add(new SignalHistory { Signal = gpsSignal, Rssi = SignalToRssi(gpsSignal), Timestamp = first, GpsData = gps });
        return ap;
    }

    private static GpsData? CsvGps(string lat, string lon, DateTime when)
    {
        var inv = CultureInfo.InvariantCulture;
        if (!double.TryParse(lat, NumberStyles.Float, inv, out var la) || !double.TryParse(lon, NumberStyles.Float, inv, out var lo)) return null;
        if (la == 0 && lo == 0) return null;   // no fix
        return new GpsData { Latitude = la, Longitude = lo, Timestamp = when, Quality = GpsQuality.GpsFix };
    }

    private static NetworkType ParseCsvNetworkType(string text)
        => text.Contains("Ad", StringComparison.OrdinalIgnoreCase) ? NetworkType.Adhoc : NetworkType.Infrastructure;

    // the original's percentage-to-dBm estimate (_SignalPercentToDb)
    private static int SignalToRssi(int signal) => signal / 2 - 100;

    // the GPS date and time as VS1 has them (ParseVs1Time)
    private static DateTime? ParseCsvTime(string date, string time) => ParseVs1Time(date, time);

    // "2024-05-01 12:34:56.789", a summary row's first and last seen
    private static DateTime? ParseCsvDateTime(string text)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? ParseVs1Time(parts[0], parts[1]) : null;
    }
}
