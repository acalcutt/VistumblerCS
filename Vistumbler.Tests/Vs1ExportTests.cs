using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Export;
using Vistumbler.Infrastructure.Import;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>The VS1 export, as Vistumbler and WifiDB read it, and back through this app's own reader.</summary>
public class Vs1ExportTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"vs1test_{Guid.NewGuid():N}.vs1");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    private static AccessPoint Ap(string ssid, string bssid, params (GpsData gps, int signal, int rssi)[] readings)
    {
        var ap = new AccessPoint
        {
            Ssid = ssid, Bssid = bssid, Manufacturer = "Acme", Authentication = AuthenticationType.WPA2_PSK, Encryption = EncryptionType.CCMP,
            RadioType = "802.11n", Channel = 6, NetworkType = NetworkType.Infrastructure, HighestSignal = 80, HighestRssi = -60,
        };
        foreach (var (gps, signal, rssi) in readings)
            ap.SignalHistory.Add(new SignalHistory { Signal = signal, Rssi = rssi, Timestamp = gps.Timestamp, GpsId = gps.GpsId, GpsData = gps });
        return ap;
    }

    private static GpsData Fix(int id, double lat, double lon, int second) => new()
    {
        GpsId = id, Latitude = lat, Longitude = lon, NumberOfSatellites = 7, Altitude = 30,
        Timestamp = new DateTime(2026, 11, 3, 14, 0, second, DateTimeKind.Utc), Quality = GpsQuality.GpsFix,
    };

    [Fact]
    public async Task WritesTheLinesVistumblerAndWifiDbRead()
    {
        var g1 = Fix(11, 42.36, -71.0589, 1);
        var g2 = Fix(12, 42.361, -71.059, 5);
        await new ExportService().ExportToVs1Async(_file, new()
        {
            Ap("Cafe|Net", "00:11:22:33:44:55", (g1, 60, -70), (g2, 80, -60)),
            Ap("Other", "00:11:22:33:44:66", (g2, 40, -80)),
        });

        var lines = File.ReadAllText(_file).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("# Vistumbler VS1 - Detailed Export Version 4.0", lines[0]);
        var data = lines.Where(l => !l.StartsWith('#')).Select(l => l.Split('|')).ToList();
        Assert.Equal(new[] { 12, 12, 15, 15 }, data.Select(f => f.Length));   // GPS lines, then access points
        Assert.Equal(new[] { "11", "N 4221.6000", "W 7103.5340" }, data[0][..3]);
        Assert.Equal(new[] { "2026-11-03", "14:00:01.000" }, data[0][10..]);
        Assert.Equal("Cafe Net", data[2][0]);
        Assert.Equal("11,60,-70\\12,80,-60", data[2][14]);
        Assert.Equal("3", data[2][5]);
    }

    [Fact]
    public async Task ComesBackThroughTheReader()
    {
        var g1 = Fix(1, 42.36, -71.0589, 1);
        var g2 = Fix(2, 42.361, -71.059, 5);
        await new ExportService().ExportToVs1Async(_file, new() { Ap("Home", "00:11:22:33:44:55", (g1, 60, -70), (g2, 80, -60)) });

        var aps = await new ImportService().ImportFromVs1Async(_file);
        var ap = Assert.Single(aps);
        Assert.Equal("Home", ap.Ssid);
        Assert.Equal(new[] { 60, 80 }, ap.SignalHistory.Select(h => h.Signal));
        Assert.Equal(42.361, ap.SignalHistory[1].GpsData!.Latitude, 4);
        Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 5, DateTimeKind.Utc), ap.LastSeen);
    }
}
