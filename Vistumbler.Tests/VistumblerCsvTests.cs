using System.Globalization;
using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Import;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>Vistumbler's detailed and summary CSVs, as the original Vistumbler writes and reads them.</summary>
public class VistumblerCsvTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vistcsv").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // as the original's _ExportToCSV writes them (Detailed = 1, and the summary)
    private const string Detailed =
        "SSID,BSSID,MANUFACTURER,SIGNAL,High Signal,RSSI,High RSSI,AUTHENTICATION,ENCRYPTION,RADIO TYPE,CHANNEL,BTX,OTX,NETWORK TYPE,LABEL,LATITUDE,LONGITUDE,SATELLITES,HDOP,ALTITUDE,HEIGHT OF GEOID,SPEED(km/h),SPEED(MPH),TRACK ANGLE,DATE(UTC),TIME(UTC)\r\n" +
        "\"Home, sweet\",00:11:22:33:44:55,\"Acme\",60,80,-70,-60,WPA2-Personal,CCMP,802.11n,6,\"1 2 5.5 11\",\"6 9 12\",Infrastructure,\"\",42.3600000,-71.0589000,7,1.2,30.5,0,36.0,22.4,90.0,2026-11-03,14:00:01.000\r\n" +
        "\"Home, sweet\",00:11:22:33:44:55,\"Acme\",80,80,-60,-60,WPA2-Personal,CCMP,802.11n,6,\"1 2 5.5 11\",\"6 9 12\",Infrastructure,\"\",42.3610000,-71.0590000,8,1.0,31.0,0,0.0,0.0,0.0,2026-11-03,14:00:05.000\r\n" +
        "\"Cafe\",66:77:88:99:AA:BB,\"\",40,40,-80,-80,Open,None,802.11g,11,\"\",\"\",Ad Hoc,\"Lobby\",0,0,0,0,0,0,0.0,0.0,0.0,2026-11-03,14:00:03.000\r\n";

    private const string Summary =
        "SSID,BSSID,MANUFACTURER,HIGHEST SIGNAL W/GPS,AUTHENTICATION,ENCRYPTION,RADIO TYPE,CHANNEL,LATITUDE,LONGITUDE,BTX,OTX,FIRST SEEN(UTC),LAST SEEN(UTC),NETWORK TYPE,LABEL, HIGHEST SIGNAL, HIGHEST RSSI\r\n" +
        "\"Home\",00:11:22:33:44:55,\"Acme\",75,WPA2-Personal,CCMP,802.11n,6,42.36,-71.0589,\"1 2\",\"6 9\",2026-11-03 14:00:01.000,2026-11-03 14:10:00.000,Infrastructure,\"\",80,-60\r\n";

    private async Task<List<AccessPoint>> ImportAsync(string text)
    {
        var file = Path.Combine(_dir, Guid.NewGuid() + ".csv");
        File.WriteAllText(file, text);
        return await new ImportService().ImportFromCsvAsync(file);
    }

    [Fact]
    public async Task ADetailedCsvComesInWithItsReadingsAndFixes()
    {
        var aps = await ImportAsync(Detailed);
        Assert.Equal(2, aps.Count);
        var home = aps[0];
        Assert.Equal(("Home, sweet", "Acme", 6, "1 2 5.5 11"), (home.Ssid, home.Manufacturer, home.Channel, home.BasicTransferRates));
        Assert.Equal(new[] { 60, 80 }, home.SignalHistory.Select(h => h.Signal));
        Assert.Equal(new[] { -70, -60 }, home.SignalHistory.Select(h => h.Rssi));
        Assert.Equal((80, -60), (home.HighestSignal, home.HighestRssi));
        Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 1, DateTimeKind.Utc), home.FirstSeen);
        Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 5, DateTimeKind.Utc), home.LastSeen);
        var g = home.SignalHistory[0].GpsData!;
        Assert.Equal((42.36, -71.0589, 7, 1.2, 30.5), (g.Latitude, g.Longitude, g.NumberOfSatellites, g.HorizontalDilution, g.Altitude));
        Assert.Equal(36.0, g.SpeedKmh, 3);
        Assert.Equal(42.361, home.Latitude);   // where it was strongest

        var cafe = aps[1];
        Assert.Equal((NetworkType.Adhoc, "Lobby"), (cafe.NetworkType, cafe.Label));
        Assert.Null(Assert.Single(cafe.SignalHistory).GpsData);   // 0,0 is no fix
    }

    [Fact]
    public async Task ASummaryCsvComesInAsOneReadingAtItsBestPosition()
    {
        var ap = Assert.Single(await ImportAsync(Summary));
        Assert.Equal((80, -60), (ap.HighestSignal, ap.HighestRssi));
        Assert.Equal((42.36, -71.0589), (ap.Latitude, ap.Longitude));
        Assert.Equal(new DateTime(2026, 11, 3, 14, 10, 0, DateTimeKind.Utc), ap.LastSeen);
        Assert.Equal(75, Assert.Single(ap.SignalHistory).Signal);
    }

    [Fact]
    public async Task TheDetailedExportIsWhatTheOriginalAndWifiDbRead()
    {
        var aps = await ImportAsync(Detailed);
        var outFile = Path.Combine(_dir, "out.csv");
        await new ExportService().ExportToCsvAsync(outFile, aps, detailed: true);

        var lines = File.ReadAllLines(outFile);
        Assert.Equal(Detailed.Split("\r\n")[0], lines[0]);
        Assert.Equal(4, lines.Length);
        // WifiDB's import_vistumblercsv takes a row only with 26 fields (as PHP's str_getcsv splits it)
        Assert.All(lines.Skip(1), l => Assert.Equal(26, SplitQuoted(l).Count));
        Assert.StartsWith("\"Home, sweet\",00:11:22:33:44:55,\"Acme\",60,80,-70,-60,WPA2-Personal,CCMP,802.11n,6,", lines[1]);
        Assert.EndsWith(",2026-11-03,14:00:01.000", lines[1]);

        // and it reads back the same
        var again = await new ImportService().ImportFromCsvAsync(outFile);
        Assert.Equal(aps.Select(a => (a.Bssid, a.SignalHistory.Count)), again.Select(a => (a.Bssid, a.SignalHistory.Count)));
    }

    [Fact]
    public async Task TheSummaryExportIsTheOriginals()
    {
        var aps = await ImportAsync(Detailed);
        var outFile = Path.Combine(_dir, "sum.csv");
        await new ExportService().ExportToCsvAsync(outFile, aps, detailed: false);

        var lines = File.ReadAllLines(outFile);
        Assert.Equal(Summary.Split("\r\n")[0], lines[0]);
        Assert.All(lines.Skip(1), l => Assert.Equal(18, SplitQuoted(l).Count));
        var home = SplitQuoted(lines[1]);
        Assert.Equal(("80", "42.361", "-71.059"), (home[3], home[8], home[9]));   // the strongest reading with a fix

        var again = await new ImportService().ImportFromCsvAsync(outFile);
        Assert.Equal(new[] { "00:11:22:33:44:55", "66:77:88:99:AA:BB" }, again.Select(a => a.Bssid));
    }

    [Fact]
    public async Task NumbersAreWrittenTheSameInAnyLocale()
    {
        var aps = await ImportAsync(Detailed);
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var outFile = Path.Combine(_dir, "de.csv");
            await new ExportService().ExportToCsvAsync(outFile, aps, detailed: true);
            Assert.Contains(",42.36,-71.0589,7,1.2,30.5,", File.ReadAllLines(outFile)[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private static List<string> SplitQuoted(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
