using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Import;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>NetStumbler files: the binary .ns1 (signal in dBm) and the text export, wi-scan.</summary>
public class Ns1Tests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ns1").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private const string Columns = "# Latitude\tLongitude\t( SSID )\tType\t( BSSID )\tTime (GMT)\t[ SNR Sig Noise ]\t# ( Name )\tFlags\tChannelbits\tBcnIntvl\tDataRate\tLastChannel\r\n";

    // NetStumbler's own: SNR over a -95 dBm floor
    private const string NetStumblerText =
        "# $Creator: Network Stumbler Version 0.4.0\r\n# $Format: wi-scan with extensions\r\n" + Columns +
        "# $DateGMT: 2026-11-03\r\n" +
        "N 42.3600000\tW 71.0589000\t( Home )\tBSS\t( 00:11:22:33:44:55 )\t14:00:01 (GMT)\t[ 30 82 52 ]\t# ( Unknown )\t0011\t0040\t100\t540\t6\r\n" +
        "N 42.3610000\tW 71.0590000\t( Home )\tBSS\t( 00:11:22:33:44:55 )\t14:00:05 (GMT)\t[ 40 92 52 ]\t# ( Unknown )\t0011\t0040\t100\t540\t6\r\n" +
        "N 360.0000000\tE 720.0000000\t( Mesh )\tad-hoc\t( 66:77:88:99:aa:bb )\t14:00:03 (GMT)\t[ 15 67 52 ]\t# ( Unknown )\t0002\t0800\t100\t110\t11\r\n";

    // Vistumbler's: Sig = dBm + 50, Noise = 50
    private const string VistumblerText =
        "# $Creator: Vistumbler v10.8\r\n# $Format: wi-scan with extensions\r\n" + Columns +
        "# $DateGMT: 2026-11-03\r\n" +
        "N 42.3600000\tW 71.0589000\t( Home )\tBSS\t( 00:11:22:33:44:55 )\t14:00:01 (GMT)\t[ -20 -20 50 ]\t# ( Unknown )\t0017\t0040\t1000\t540\t6\r\n";

    private async Task<List<AccessPoint>> ImportAsync(string text)
    {
        var file = Path.Combine(_dir, Guid.NewGuid() + ".ns1");
        File.WriteAllText(file, text);
        return await new ImportService().ImportFromNs1Async(file);
    }

    [Fact]
    public async Task NetStumblersTextExportComesInWithItsReadings()
    {
        var aps = await ImportAsync(NetStumblerText);
        Assert.Equal(new[] { "00:11:22:33:44:55", "66:77:88:99:AA:BB" }, aps.Select(a => a.Bssid));
        var home = aps[0];
        Assert.Equal(new[] { -65, -55 }, home.SignalHistory.Select(h => h.Rssi));
        Assert.Equal((-55, 90), (home.HighestRssi, home.HighestSignal));
        Assert.Equal((EncryptionType.WEP, 6), (home.Encryption, home.Channel));
        Assert.Equal((42.361, -71.059), (home.Latitude, home.Longitude));
        Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 1, DateTimeKind.Utc), home.FirstSeen);

        var mesh = aps[1];
        Assert.Equal(NetworkType.Adhoc, mesh.NetworkType);
        Assert.Null(Assert.Single(mesh.SignalHistory).GpsData);   // 360/720 is no fix
    }

    [Fact]
    public async Task VistumblersTextExportIsReadByItsOwnScale()
    {
        var ap = Assert.Single(await ImportAsync(VistumblerText));
        Assert.Equal(-70, Assert.Single(ap.SignalHistory).Rssi);
    }

    [Fact]
    public async Task TheBinaryExportKeepsSignalsInDbm()
    {
        var ap = new AccessPoint
        {
            Bssid = "00:11:22:33:44:55", Ssid = "Home", Channel = 6, NetworkType = NetworkType.Infrastructure,
            Authentication = AuthenticationType.WPA2_PSK, Encryption = EncryptionType.CCMP,
            FirstSeen = new DateTime(2026, 11, 3, 14, 0, 1, DateTimeKind.Utc), LastSeen = new DateTime(2026, 11, 3, 14, 0, 5, DateTimeKind.Utc),
            SignalHistory =
            {
                new SignalHistory { Signal = 70, Rssi = -65, Timestamp = new DateTime(2026, 11, 3, 14, 0, 1, DateTimeKind.Utc),
                    GpsData = new GpsData { Latitude = 42.36, Longitude = -71.0589, Altitude = 30, NumberOfSatellites = 7 } },
                new SignalHistory { Signal = 90, Rssi = 0, Timestamp = new DateTime(2026, 11, 3, 14, 0, 5, DateTimeKind.Utc) },
            },
        };
        var file = Path.Combine(_dir, "out.ns1");
        await new ExportService().ExportToNs1Async(file, new List<AccessPoint> { ap });

        var back = Assert.Single(await new ImportService().ImportFromNs1Async(file));
        // the second reading had no RSSI: estimated from its percentage, as the original does
        Assert.Equal(new[] { -65, -55 }, back.SignalHistory.Select(h => h.Rssi));
        Assert.Equal(-55, back.HighestRssi);
        Assert.Equal((AuthenticationType.WPA2_PSK, EncryptionType.CCMP), (back.Authentication, back.Encryption));
        Assert.Equal((42.36, 30.0, 7), (back.SignalHistory[0].GpsData!.Latitude, back.SignalHistory[0].GpsData!.Altitude, back.SignalHistory[0].GpsData!.NumberOfSatellites));
    }
}
