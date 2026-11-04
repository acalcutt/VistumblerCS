using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Import;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>WiGLE CSV's cell tower and Bluetooth rows: read apart from the Wi-Fi, and written back.</summary>
public class WigleCsvRadioNetworkTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wigleradio").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private const string Wigle16 =
        "WigleWifi-1.6,appRelease=2.80,model=Pixel,release=14,device=test,display=,board=,brand=,star=Sol,body=3,subBody=0\n" +
        "MAC,SSID,AuthMode,FirstSeen,Channel,Frequency,RSSI,CurrentLatitude,CurrentLongitude,AltitudeMeters,AccuracyMeters,RCOIs,MfgrId,Type\n" +
        "00:11:22:33:44:55,Home,[WPA2-PSK-CCMP][ESS],2026-11-03 14:00:01,6,2437,-60,42.36,-71.0589,30,5,,,WIFI\n" +
        "aa:bb:cc:dd:ee:ff,Buds,Headphones,2026-11-03 14:00:02,,1048,-62,42.3601,-71.059,30,5,,76,BLE\n" +
        "aa:bb:cc:dd:ee:ff,Buds,Headphones,2026-11-03 14:00:06,,1048,-55,42.3605,-71.0591,30,5,,76,BLE\n" +
        "11:22:33:44:55:66,Car,Car Audio;12,2026-11-03 14:00:03,,1032,-70,42.3602,-71.0592,30,5,,,BT\n" +
        "310260_12345_678901,T-Mobile,LTE;310260,2026-11-03 14:00:04,5110,5110,-88,42.3603,-71.0593,30,5,,,LTE\n";

    [Fact]
    public async Task TheyAreReadApartFromTheWifi()
    {
        var file = Path.Combine(_dir, "in.csv");
        File.WriteAllText(file, Wigle16);
        var import = new ImportService();
        var aps = await import.ImportFromCsvAsync(file);
        var radios = import.TakeRadioNetworks();

        Assert.Equal("00:11:22:33:44:55", Assert.Single(aps).Bssid);   // no Bluetooth device taken for an AP
        Assert.Equal(new[] { "AA:BB:CC:DD:EE:FF", "11:22:33:44:55:66", "310260_12345_678901" }, radios.Select(r => r.Key));
        var buds = radios[0];
        Assert.Equal((RadioNetworkType.BLE, "Buds", 76, 1048), (buds.Type, buds.Name, buds.MfgrId, buds.Frequency));
        Assert.Equal(new[] { -62, -55 }, buds.History.Select(r => r.Rssi));
        Assert.Equal(-55, buds.HighestRssi);
        Assert.Equal(42.3605, buds.Latitude);
        Assert.Equal((RadioNetworkType.LTE, 5110), (radios[2].Type, radios[2].Channel));
    }

    [Fact]
    public async Task TheyGoOutAgainAsWigleRows()
    {
        var file = Path.Combine(_dir, "in.csv");
        File.WriteAllText(file, Wigle16);
        var import = new ImportService();
        var aps = await import.ImportFromCsvAsync(file);
        var radios = import.TakeRadioNetworks();

        var outFile = Path.Combine(_dir, "out.csv");
        await new ExportService().ExportToWigleCsvAsync(outFile, aps, radios);
        var rows = File.ReadAllLines(outFile).Skip(2).Select(l => l.Split(',')).ToList();
        Assert.All(rows, r => Assert.Equal(14, r.Length));
        Assert.Equal(new[] { "WIFI", "BLE", "BLE", "BT", "LTE" }, rows.Select(r => r[13]));
        Assert.Equal(new[] { "aa:bb:cc:dd:ee:ff".ToUpperInvariant(), "Buds", "Headphones", "2026-11-03 14:00:02", "", "1048", "-62" }, rows[1][..7]);
        Assert.Equal("76", rows[1][12]);

        var again = new ImportService();
        await again.ImportFromCsvAsync(outFile);
        var back = again.TakeRadioNetworks();
        Assert.Equal(radios.Select(r => (r.Key, r.Type, r.History.Count)), back.Select(r => (r.Key, r.Type, r.History.Count)));
    }
}
