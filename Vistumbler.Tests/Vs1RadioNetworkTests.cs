using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using Vistumbler.Infrastructure.Data;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Import;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>
/// VS1 4.1's cell towers and Bluetooth devices: read from a file, kept in the database with their readings, and
/// written back out.
/// </summary>
public class Vs1RadioNetworkTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vs1radio").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }   // the database service keeps its connection open; the temp folder can wait
    }

    // As the VS1 format page's example has it, with an access point and three GPS lines
    private const string File41 =
        "# Vistumbler VS1 - Detailed Export Version 4.1\r\n" +
        "# Created By: VistumblerMAUI\r\n" +
        "1|N 4221.6000|W 7103.5340|7|1.0|30.0|0|0.0|0.0|0.0|2026-11-03|14:00:01.000\r\n" +
        "2|N 4221.6600|W 7103.5400|7|1.0|30.0|0|0.0|0.0|0.0|2026-11-03|14:00:05.000\r\n" +
        "3|N 4221.7200|W 7103.5460|7|1.0|30.0|0|0.0|0.0|0.0|2026-11-03|14:00:09.000\r\n" +
        "Home|00:11:22:33:44:55|Acme|WPA2-Personal|CCMP|3|802.11n|6|||80|-60|Infrastructure||1,60,-70\\2,80,-60\r\n" +
        "# Type|Key|Name|Capabilities|Channel|Frequency|MfgrId|Manufacturer|High RSSI|GID,RSSI\r\n" +
        "LTE|310260_12345_678901|T-Mobile|LTE;310260|5110|5110|||-88|1,-95\\3,-88\r\n" +
        "BLE|AA:BB:CC:DD:EE:FF|Buds|Headphones||1048|76|Apple|-60|2,-60\r\n" +
        "BT|11:22:33:44:55:66|Car|Car Audio;12||1032|||-70|\r\n";

    private async Task<SQLiteDatabaseService> ImportAsync(params string[] files)
    {
        var db = new SQLiteDatabaseService();
        await db.InitializeAsync(Path.Combine(_dir, "vistumbler.db"));
        var import = new ImportService();
        foreach (var f in files)
        {
            var aps = await import.ImportFromVs1Async(f);
            await ImportSaver.SaveAsync(db, aps, import.TakeRadioNetworks());
        }
        return db;
    }

    [Fact]
    public async Task TheReaderKeepsThemWithTheirReadings()
    {
        var file = Path.Combine(_dir, "in.vs1");
        File.WriteAllText(file, File41);
        var import = new ImportService();
        var aps = await import.ImportFromVs1Async(file);
        var radios = import.TakeRadioNetworks();

        Assert.Single(aps);
        Assert.Equal(new[] { "310260_12345_678901", "AA:BB:CC:DD:EE:FF", "11:22:33:44:55:66" }, radios.Select(r => r.Key));
        var lte = radios[0];
        Assert.Equal((RadioNetworkType.LTE, "T-Mobile", "LTE;310260", 5110, 5110), (lte.Type, lte.Name, lte.Capabilities, lte.Channel, lte.Frequency));
        Assert.Equal(new[] { -95, -88 }, lte.History.Select(r => r.Rssi));
        Assert.Equal(-88, lte.HighestRssi);
        Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 9, DateTimeKind.Utc), lte.LastSeen);
        Assert.Same(aps[0].SignalHistory[1].GpsData, radios[1].History[0].GpsData);   // the same GPS line, shared
        Assert.Equal((76, "Apple"), (radios[1].MfgrId, radios[1].Manufacturer));
        Assert.Empty(radios[2].History);
        Assert.Equal(-70, radios[2].HighestRssi);   // heard only without a fix: its strongest RSSI still kept
        Assert.Empty(import.TakeRadioNetworks());
    }

    [Fact]
    public async Task TheyAreStoredAndGoOutAgainAs41()
    {
        var file = Path.Combine(_dir, "in.vs1");
        File.WriteAllText(file, File41);
        var db = await ImportAsync(file, file);   // twice: merged, not doubled

        var stored = await db.GetAllRadioNetworksAsync();
        Assert.Equal(3, stored.Count);
        Assert.Equal(6, (await ExportLoader.LoadRadioNetworksAsync(db)).Sum(n => n.History.Count));   // each reading of both imports

        var aps = await db.GetAllAccessPointsAsync();
        await ExportLoader.LoadHistoryAsync(db, aps);
        var radios = await ExportLoader.LoadRadioNetworksAsync(db);
        var outFile = Path.Combine(_dir, "out.vs1");
        await new ExportService().ExportToVs1Async(outFile, aps, radios);

        var lines = File.ReadAllText(outFile).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("# Vistumbler VS1 - Detailed Export Version 4.1", lines[0]);
        var radioLines = lines.Where(l => !l.StartsWith('#')).Select(l => l.Split('|')).Where(f => f.Length == 10).ToList();
        Assert.Equal(new[] { "LTE", "BLE", "BT" }, radioLines.Select(f => f[0]));
        Assert.Equal(new[] { "310260_12345_678901", "T-Mobile", "LTE;310260", "5110", "5110", "", "", "-88" }, radioLines[0][1..9]);

        // and the file reads back the same, its readings at the same places
        var again = new ImportService();
        await again.ImportFromVs1Async(outFile);
        var back = again.TakeRadioNetworks();
        Assert.Equal(new[] { -95, -95, -88, -88 }, back[0].History.Select(r => r.Rssi));   // both imports' readings, in time order
        Assert.Equal(42.360, back[0].History[0].GpsData!.Latitude, 3);
        Assert.Equal(42.362, back[0].History[2].GpsData!.Latitude, 3);
        Assert.Equal((76, "Apple"), (back[1].MfgrId, back[1].Manufacturer));
    }

    [Fact]
    public async Task WithoutAnyTheFileStays40()
    {
        var outFile = Path.Combine(_dir, "out.vs1");
        await new ExportService().ExportToVs1Async(outFile, new(), new List<RadioNetwork>());
        Assert.StartsWith("# Vistumbler VS1 - Detailed Export Version 4.0", File.ReadAllText(outFile));
    }
}
