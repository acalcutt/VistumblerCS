using System.Globalization;
using Vistumbler.Core.Models;

namespace Vistumbler.Infrastructure.Import;

/// <summary>
/// NetStumbler's text export ("wi-scan"), a port of the original Vistumbler's _ImportNS1: one tab-separated line per
/// sighting, "Latitude  Longitude  ( SSID )  Type  ( BSSID )  Time (GMT)  [ SNR Sig Noise ]  # ( Name )  Flags
/// Channelbits  BcnIntvl  DataRate  LastChannel", with the date in a "# $DateGMT:" comment. Only WEP is recorded as
/// security. Sightings of the same BSSID become one AP with a reading each. It's told apart from the binary NS1 by
/// its content, as the original's _ImportNS1Auto does, since both are often named .ns1 or .txt.
/// </summary>
public partial class ImportService
{
    private static List<AccessPoint> ReadNs1Text(string filePath)
    {
        var byBssid = new Dictionary<string, AccessPoint>(StringComparer.OrdinalIgnoreCase);
        var date = DateTime.UtcNow.Date;
        bool writtenByVistumbler = false;

        foreach (var line in File.ReadLines(filePath))
        {
            if (line.StartsWith("# $Creator:", StringComparison.Ordinal))
                writtenByVistumbler = line.Contains("Vistumbler", StringComparison.OrdinalIgnoreCase);
            if (line.StartsWith("# $DateGMT:", StringComparison.Ordinal) &&
                DateTime.TryParse(line[11..].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
                date = DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
            if (line.Length == 0 || line[0] == '#') continue;

            var p = line.Split('\t');
            if (p.Length != 13) continue;

            // "[ SNR Sig Noise ]"; -32618 means no reading. NetStumbler's SNR is over a -95 dBm noise floor, so
            // dBm = SNR - 95 (the original's import). Vistumbler's own export writes Sig = dBm + 50 and Noise = 50
            // instead (wiki: Wi-Scan-Format), so its files are read back with dBm = Sig - 50.
            var nums = p[6].Trim('[', ']', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (nums.Length == 0 || !int.TryParse(nums[0], out var snr) || snr == -32618) continue;
            int rssi = writtenByVistumbler && nums.Length > 1 && int.TryParse(nums[1], out var sig) ? sig - 50 : snr - 95;

            var bssid = p[4].Trim().TrimStart('(').TrimEnd(')').Trim().ToUpperInvariant();
            if (bssid.Length == 0) continue;
            var ssid = p[2].Trim();
            if (ssid.StartsWith("( ") && ssid.EndsWith(" )")) ssid = ssid[2..^2];

            var time = p[5].Replace("(GMT)", "").Trim();
            var when = TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var t) ? date + t : date;
            uint flags = uint.TryParse(p[8].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var f) ? f : 0;
            int channel = int.TryParse(p[12].Trim(), out var ch) ? ch : 0;

            var hist = new SignalHistory { Timestamp = when, Rssi = rssi, Signal = DbmToPercent(rssi) };
            // "N 360.0000000" / "E 720.0000000" is NetStumbler's "no fix"
            if (ParseNs1Coordinate(p[0]) is { } lat && ParseNs1Coordinate(p[1]) is { } lon && (lat != 0 || lon != 0))
                hist.GpsData = new GpsData { Latitude = lat, Longitude = lon, Timestamp = when, Quality = GpsQuality.GpsFix };

            if (!byBssid.TryGetValue(bssid, out var ap))
            {
                ap = new AccessPoint
                {
                    Bssid = bssid,
                    Ssid = ssid,
                    FirstSeen = when,
                    LastSeen = when,
                    NetworkType = (flags & 0x2) != 0 ? NetworkType.Adhoc : NetworkType.Infrastructure,
                    Authentication = AuthenticationType.Open,
                    Encryption = (flags & 0x10) != 0 ? EncryptionType.WEP : EncryptionType.None,
                };
                byBssid[bssid] = ap;
            }
            ap.SignalHistory.Add(hist);
            if (when < ap.FirstSeen) ap.FirstSeen = when;
            if (when >= ap.LastSeen)
            {
                ap.LastSeen = when;
                ap.Signal = hist.Signal;
                ap.Rssi = rssi;
                if (channel > 0) ap.Channel = channel;
            }
            if (rssi > (ap.HighestRssi ?? int.MinValue))
            {
                ap.HighestRssi = rssi;
                ap.HighestSignal = hist.Signal;
                // placed where its signal was strongest, as Vistumbler does
                if (hist.GpsData is { } g) { ap.Latitude = g.Latitude; ap.Longitude = g.Longitude; }
            }
            if (ap.Channel == 0 && channel > 0) ap.Channel = channel;
        }
        return byBssid.Values.ToList();
    }

    // "N 48.1173000" / "W 11.5166667" → signed decimal degrees; null when unreadable
    private static double? ParseNs1Coordinate(string text)
    {
        text = text.Trim();
        if (text.Length < 3) return null;
        if (!double.TryParse(text[1..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        if (v >= 360) return 0;   // no fix
        return text[0] is 'S' or 's' or 'W' or 'w' ? -v : v;
    }

    // dBm → signal percentage, the original's _DbToSignalPercent: 0% at -100 dBm, 100% at -50
    private static int DbmToPercent(int dbm) => Math.Clamp((dbm + 100) * 2, 0, 100);
}
