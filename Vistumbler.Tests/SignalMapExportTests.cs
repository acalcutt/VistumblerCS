using System.Text.Json;
using System.Xml.Linq;
using Vistumbler.Core.Models;
using Vistumbler.Infrastructure.Export;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>GeoJSON (the APs, or every reading) and the KML signal map, with WifiDB's property names.</summary>
public class SignalMapExportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("signalmap").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static List<AccessPoint> Aps()
    {
        var t = new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
        return new()
        {
            new()
            {
                Bssid = "00:11:22:33:44:55", Ssid = "Home", Channel = 6, Encryption = EncryptionType.CCMP,
                Authentication = AuthenticationType.WPA2_PSK, HighestSignal = 90, HighestRssi = -55,
                Latitude = 42.361, Longitude = -71.059, FirstSeen = t, LastSeen = t.AddSeconds(5),
                SignalHistory =
                {
                    new() { Signal = 70, Rssi = -65, Timestamp = t, GpsData = new GpsData { Latitude = 42.36, Longitude = -71.0589, Timestamp = t } },
                    new() { Signal = 90, Rssi = 0, Timestamp = t.AddSeconds(5), GpsData = new GpsData { Latitude = 42.361, Longitude = -71.059, Timestamp = t.AddSeconds(5) } },
                    new() { Signal = 50, Rssi = -75, Timestamp = t.AddSeconds(9) },   // no fix
                },
            },
            new() { Bssid = "66:77:88:99:AA:BB", Ssid = "NoFix", Encryption = EncryptionType.None },
        };
    }

    [Fact]
    public async Task GeoJsonHasAPointPerApWithAPosition()
    {
        var file = Path.Combine(_dir, "aps.geojson");
        await new ExportService().ExportToGeoJsonAsync(file, Aps(), signalMap: false);

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var feature = Assert.Single(doc.RootElement.GetProperty("features").EnumerateArray());
        var props = feature.GetProperty("properties");
        Assert.Equal(("00:11:22:33:44:55", 3, "WPA2-Personal", -55),
            (props.GetProperty("mac").GetString(), props.GetProperty("sectype").GetInt32(), props.GetProperty("auth").GetString(), props.GetProperty("high_gps_rssi").GetInt32()));
        Assert.Equal(new[] { -71.059, 42.361 }, feature.GetProperty("geometry").GetProperty("coordinates").EnumerateArray().Select(c => c.GetDouble()));
    }

    [Fact]
    public async Task TheGeoJsonSignalMapHasAPointPerReadingWithAFix()
    {
        var file = Path.Combine(_dir, "map.geojson");
        await new ExportService().ExportToGeoJsonAsync(file, Aps(), signalMap: true);

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var props = doc.RootElement.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("properties")).ToList();
        // the second reading had no RSSI: estimated from its percentage
        Assert.Equal(new[] { -65, -55 }, props.Select(p => p.GetProperty("rssi").GetInt32()));
        Assert.Equal("2026-11-03 14:00:05", props[1].GetProperty("hist_date").GetString());
    }

    [Fact]
    public async Task TheKmlSignalMapHasADotPerReadingInItsBand()
    {
        var file = Path.Combine(_dir, "map.kml");
        await new ExportService().ExportToKmlSignalMapAsync(file, Aps());

        XNamespace kml = "http://www.opengis.net/kml/2.2";
        var folder = Assert.Single(XDocument.Load(file).Descendants(kml + "Folder"));
        Assert.Equal("Home (00:11:22:33:44:55)", folder.Element(kml + "name")!.Value);
        Assert.Equal(new[] { "#r_69_60", "#r_59_50" }, folder.Elements(kml + "Placemark").Select(p => p.Element(kml + "styleUrl")!.Value));
    }
}
