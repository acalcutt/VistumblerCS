using Microsoft.Data.Sqlite;
using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Import;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>A Wardrive-Android database: a row per network, logged where and when it was heard.</summary>
public class WardriveImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wardrive").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string MakeDb()
    {
        var file = Path.Combine(_dir, "wardrive.db3");
        using var conn = new SqliteConnection($"Data Source={file};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE networks (bssid TEXT, ssid TEXT, capabilities TEXT, level INTEGER, frequency INTEGER,
                                   lat REAL, lon REAL, alt REAL, timestamp INTEGER);
            INSERT INTO networks VALUES ('00:11:22:aa:bb:cc', 'Home', '[WPA2-PSK-CCMP][ESS]', -70, 2437, 42.36, -71.0589, 30, 1793714401000);
            INSERT INTO networks VALUES ('00:11:22:aa:bb:cc', 'Home', '[WPA2-PSK-CCMP][ESS]', -55, 2437, 42.361, -71.059, 31, 1793714405000);
            INSERT INTO networks VALUES ('66:77:88:99:aa:bb', 'Mesh', '[IBSS]', -80, 5180, 0, 0, 0, 1793714403000);
            """;
        cmd.ExecuteNonQuery();
        return file;
    }

    [Fact]
    public async Task ItsNetworksComeInWithEachLoggedReading()
    {
        var aps = await new ImportService().ImportFromWardriveDb3Async(MakeDb());

        Assert.Equal(new[] { "00:11:22:AA:BB:CC", "66:77:88:99:AA:BB" }, aps.Select(a => a.Bssid));
        var home = aps[0];
        Assert.Equal((AuthenticationType.WPA2_PSK, 6, 2437), (home.Authentication, home.Channel, home.FrequencyMhz));
        Assert.Equal(new[] { -70, -55 }, home.SignalHistory.Select(h => h.Rssi));
        Assert.Equal((90, -55), (home.HighestSignal, home.HighestRssi));
        Assert.Equal((42.361, -71.059), (home.Latitude, home.Longitude));   // where it was strongest
        Assert.Equal(31, home.SignalHistory[1].GpsData!.Altitude);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793714401000).UtcDateTime, home.FirstSeen);

        var mesh = aps[1];
        Assert.Equal((NetworkType.Adhoc, 36), (mesh.NetworkType, mesh.Channel));
        Assert.Null(Assert.Single(mesh.SignalHistory).GpsData);   // 0,0 is no fix
    }

    [Fact]
    public async Task AnotherSqliteFileIsNotAWardriveDatabase()
    {
        var file = Path.Combine(_dir, "other.db3");
        using (var conn = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE something (x INTEGER)";
            cmd.ExecuteNonQuery();
        }
        Assert.Empty(await new ImportService().ImportFromWardriveDb3Async(file));
    }
}
