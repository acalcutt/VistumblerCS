using Microsoft.Data.Sqlite;
using Vistumbler.Core.Models;

namespace Vistumbler.Infrastructure.Import;

/// <summary>
/// Wardrive-Android's database (a .db3), a port of the original Vistumbler's _ImportWardriveDb3: its <c>networks</c>
/// table has a row per network, at the place and time it was logged, with its signal in dBm and its capabilities as
/// Android writes them ("[WPA2-PSK-CCMP][ESS]", as WiGLE's AuthMode is).
/// </summary>
public partial class ImportService
{
    public async Task<List<AccessPoint>> ImportFromWardriveDb3Async(string filePath)
    {
        _radios.Clear();
        var accessPoints = new List<AccessPoint>();
        if (!File.Exists(filePath)) return accessPoints;

        // read-only, and not pooled, so the file isn't left open after the import
        var cs = new SqliteConnectionStringBuilder { DataSource = filePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        await using var conn = new SqliteConnection(cs.ToString());
        await conn.OpenAsync();

        await using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='networks'";
            if (await check.ExecuteScalarAsync() is null) return accessPoints;   // not a Wardrive database
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT bssid, ssid, capabilities, level, frequency, lat, lon, alt, timestamp FROM networks";
        await using var reader = await cmd.ExecuteReaderAsync();

        var byBssid = new Dictionary<string, AccessPoint>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            string bssid = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim().ToUpperInvariant();
            if (bssid.Length == 0) continue;
            string ssid = reader.IsDBNull(1) ? "" : reader.GetString(1);
            string caps = reader.IsDBNull(2) ? "" : reader.GetString(2);
            int rssi = reader.IsDBNull(3) ? -100 : reader.GetInt32(3);
            int freq = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            double lat = reader.IsDBNull(5) ? 0 : reader.GetDouble(5);
            double lon = reader.IsDBNull(6) ? 0 : reader.GetDouble(6);
            double? alt = reader.IsDBNull(7) ? null : reader.GetDouble(7);
            long ms = reader.IsDBNull(8) ? 0 : reader.GetInt64(8);

            // Unix time in milliseconds
            var when = ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : DateTime.UtcNow;
            int signal = Math.Clamp((rssi + 100) * 2, 0, 100);   // the original's _DbToSignalPercent

            if (!byBssid.TryGetValue(bssid, out var ap))
            {
                ParseWigleAuthMode(caps, out var auth, out var encr, out var netType);
                ap = new AccessPoint
                {
                    Bssid = bssid,
                    Ssid = ssid,
                    Authentication = auth,
                    Encryption = encr,
                    NetworkType = netType,
                    FrequencyMhz = freq,
                    Channel = GetChannelFromFreq(freq),
                    RadioType = freq >= 5925 ? "802.11ax" : freq >= 5000 ? "802.11a" : "802.11g",
                    FirstSeen = when,
                    LastSeen = when,
                };
                byBssid[bssid] = ap;
                accessPoints.Add(ap);
            }

            GpsData? gps = lat != 0 || lon != 0
                ? new GpsData { Latitude = lat, Longitude = lon, Altitude = alt, Timestamp = when, Quality = GpsQuality.GpsFix }
                : null;
            ap.SignalHistory.Add(new SignalHistory { Signal = signal, Rssi = rssi, Timestamp = when, GpsData = gps });

            if (when < ap.FirstSeen) ap.FirstSeen = when;
            if (when >= ap.LastSeen) { ap.LastSeen = when; ap.Signal = signal; ap.Rssi = rssi; }
            if (signal >= (ap.HighestSignal ?? -1))
            {
                ap.HighestSignal = signal;
                ap.HighestRssi = rssi;
                if (gps is not null) { ap.Latitude = lat; ap.Longitude = lon; }
            }
            else if (ap.Latitude is null && gps is not null)
            {
                ap.Latitude = lat;
                ap.Longitude = lon;
            }
        }
        return accessPoints;
    }
}
