using System.Globalization;
using System.Xml.Linq;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using Vistumbler.Infrastructure.Export;
using Xunit;

namespace Vistumbler.Tests;

/// <summary>KML and GPX: the APs where they are, and the GPS track.</summary>
public class KmlGpxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kmlgpx").FullName;
    private static readonly XNamespace Kml = "http://www.opengis.net/kml/2.2";
    private static readonly XNamespace Gpx = "http://www.topografix.com/GPX/1/1";

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static List<AccessPoint> Aps() => new()
    {
        new() { Bssid = "00:11:22:33:44:55", Ssid = "Home\u0001", Encryption = EncryptionType.CCMP, HighestSignal = 90, Latitude = 42.36, Longitude = -71.0589 },
        new() { Bssid = "66:77:88:99:AA:BB", Ssid = "Cafe", Encryption = EncryptionType.None, HighestSignal = 40, Latitude = 42.37, Longitude = -71.06 },
        new() { Bssid = "CC:DD:EE:FF:00:11", Ssid = "Old", Encryption = EncryptionType.WEP, HighestSignal = 30, Latitude = 42.38, Longitude = -71.07 },
        new() { Bssid = "22:33:44:55:66:77", Ssid = "NoFix", Encryption = EncryptionType.None },
    };

    // two stretches, 10 minutes apart
    private static List<GpsData> Fixes()
    {
        var t = new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
        return new()
        {
            new() { Latitude = 42.36, Longitude = -71.0589, Altitude = 30, NumberOfSatellites = 7, Timestamp = t },
            new() { Latitude = 42.361, Longitude = -71.059, Timestamp = t.AddSeconds(5) },
            new() { Latitude = 42.37, Longitude = -71.06, Timestamp = t.AddMinutes(10) },
            new() { Latitude = 42.371, Longitude = -71.061, Timestamp = t.AddMinutes(10).AddSeconds(5) },
        };
    }

    [Fact]
    public async Task KmlHasThePlacemarksAndTheTrackInAnyLocale()
    {
        var file = Path.Combine(_dir, "out.kml");
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            await new ExportService().ExportToKmlAsync(file, Aps(), new ExportOptions(), Fixes());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }

        var doc = XDocument.Load(file);
        var placemarks = doc.Descendants(Kml + "Placemark").Where(p => p.Element(Kml + "Point") is not null).ToList();
        Assert.Equal(new[] { "Home", "Cafe", "Old" }, placemarks.Select(p => p.Element(Kml + "name")!.Value));
        Assert.Equal("-71.0589,42.36,0", placemarks[0].Descendants(Kml + "coordinates").Single().Value);
        var lines = doc.Descendants(Kml + "LineString").ToList();
        Assert.Equal(2, lines.Count);   // broken at the 10-minute gap
        Assert.Equal("-71.0589,42.36,0\n-71.059,42.361,0", lines[0].Element(Kml + "coordinates")!.Value);
    }

    [Fact]
    public async Task KmlLeavesOutWhatsUnticked()
    {
        var file = Path.Combine(_dir, "secure.kml");
        await new ExportService().ExportToKmlAsync(file, Aps(), new ExportOptions { IncludeOpenNetworks = false, IncludeWepNetworks = false, ShowTrack = false });
        var names = XDocument.Load(file).Descendants(Kml + "Placemark").Select(p => p.Element(Kml + "name")!.Value);
        Assert.Equal(new[] { "Home" }, names);   // CCMP is secure
    }

    [Fact]
    public async Task GpxHasTheWaypointsAndATrackSegmentPerStretch()
    {
        var file = Path.Combine(_dir, "out.gpx");
        await new ExportService().ExportToGpxAsync(file, Aps(), Fixes());

        var doc = XDocument.Load(file);
        Assert.Equal(3, doc.Descendants(Gpx + "wpt").Count());
        var segs = doc.Descendants(Gpx + "trkseg").ToList();
        Assert.Equal(new[] { 2, 2 }, segs.Select(s => s.Elements(Gpx + "trkpt").Count()));
        var first = segs[0].Elements(Gpx + "trkpt").First();
        Assert.Equal(("42.3600000", "30.0", "2026-11-03T14:00:00Z", "7"),
            (first.Attribute("lat")!.Value, first.Element(Gpx + "ele")!.Value, first.Element(Gpx + "time")!.Value, first.Element(Gpx + "sat")!.Value));
    }
}
