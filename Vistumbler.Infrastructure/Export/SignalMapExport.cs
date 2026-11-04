using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml;
using Vistumbler.Core.Extensions;
using Vistumbler.Core.Models;

namespace Vistumbler.Infrastructure.Export;

/// <summary>
/// GeoJSON and KML for maps elsewhere, as VistumblerMAUI writes them: the APs as points, or a signal map of every
/// reading, the site survey export of WiGLE WiFi Wardriving and the signal map WifiDB draws. GeoJSON properties use
/// WifiDB's names (mac, ssid, signal, rssi, sectype, chan, hist_date…), so WifiDB's map styles work on the files as
/// they are: APs coloured by sectype, readings by rssi. Each AP's readings come from
/// <see cref="Vistumbler.Core.Services.ExportLoader.LoadHistoryAsync"/>.
/// </summary>
public static class SignalMapExport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>One AP's readings with a GPS fix.</summary>
    public static IEnumerable<SignalHistory> Positioned(AccessPoint ap) =>
        ap.SignalHistory.Where(h => h.GpsData is { } g && (g.Latitude != 0 || g.Longitude != 0));

    // A reading's RSSI, or an estimate from its percentage when there's none (0 % = -100 dBm, 100 % = -50 dBm)
    public static int RssiOf(SignalHistory h) => h.Rssi != 0 ? h.Rssi : h.Signal / 2 - 100;

    // WifiDB's sectype: 1 open, 2 WEP, 3 secure
    private static int SecurityType(AccessPoint ap) => ap.Encryption switch
    {
        EncryptionType.None => 1,
        EncryptionType.WEP => 2,
        _ => 3,
    };

    private static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;

    // ── GeoJSON ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A FeatureCollection: one point per AP at its best-signal position, or with <paramref name="signalMap"/> one
    /// point per reading.
    /// </summary>
    public static async Task WriteGeoJsonAsync(string path, IEnumerable<AccessPoint> aps, bool signalMap)
    {
        await using var file = File.Create(path);
        await using var json = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = false });
        json.WriteStartObject();
        json.WriteString("type", "FeatureCollection");
        var version = (Assembly.GetEntryAssembly() ?? typeof(SignalMapExport).Assembly).GetName().Version;
        json.WriteString("generator", $"VistumblerCS {version?.ToString(3)}");
        json.WriteStartArray("features");
        foreach (var ap in aps)
        {
            if (signalMap)
            {
                foreach (var h in Positioned(ap))
                {
                    var g = h.GpsData!;
                    json.WriteStartObject();
                    json.WriteString("type", "Feature");
                    json.WriteStartObject("properties");
                    WriteApProperties(json, ap);
                    json.WriteNumber("signal", h.Signal);
                    json.WriteNumber("rssi", RssiOf(h));
                    json.WriteString("hist_date", Utc(g.Timestamp).ToString("yyyy-MM-dd HH:mm:ss", Inv));
                    json.WriteNumber("lat", Math.Round(g.Latitude, 7));
                    json.WriteNumber("lon", Math.Round(g.Longitude, 7));
                    json.WriteEndObject();
                    WritePoint(json, g.Latitude, g.Longitude);
                    json.WriteEndObject();
                }
            }
            else if (ap.Latitude is { } lat && ap.Longitude is { } lon && (lat != 0 || lon != 0))
            {
                json.WriteStartObject();
                json.WriteString("type", "Feature");
                json.WriteStartObject("properties");
                WriteApProperties(json, ap);
                if (ap.HighestSignal is { } hs) json.WriteNumber("high_gps_sig", hs);
                if (ap.HighestRssi is { } hr) json.WriteNumber("high_gps_rssi", hr);
                json.WriteString("FA", Utc(ap.FirstSeen).ToString("yyyy-MM-dd HH:mm:ss", Inv));
                json.WriteString("LA", Utc(ap.LastSeen).ToString("yyyy-MM-dd HH:mm:ss", Inv));
                json.WriteNumber("lat", Math.Round(lat, 7));
                json.WriteNumber("lon", Math.Round(lon, 7));
                json.WriteEndObject();
                WritePoint(json, lat, lon);
                json.WriteEndObject();
            }
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteApProperties(Utf8JsonWriter json, AccessPoint ap)
    {
        json.WriteString("mac", ap.Bssid);
        json.WriteString("ssid", ap.Ssid);
        json.WriteNumber("sectype", SecurityType(ap));
        json.WriteNumber("chan", ap.Channel);
        if (ap.FrequencyMhz > 0) json.WriteNumber("frequency", ap.FrequencyMhz);
        json.WriteString("auth", ap.Authentication.ToLegacyString());
        json.WriteString("encry", ap.Encryption.ToLegacyString());
        json.WriteString("radio", ap.RadioType);
        json.WriteString("NT", ap.NetworkType == NetworkType.Adhoc ? "Ad-Hoc" : "Infrastructure");
        if (!string.IsNullOrEmpty(ap.Manufacturer)) json.WriteString("manuf", ap.Manufacturer);
    }

    private static void WritePoint(Utf8JsonWriter json, double lat, double lon)
    {
        json.WriteStartObject("geometry");
        json.WriteString("type", "Point");
        json.WriteStartArray("coordinates");
        json.WriteNumberValue(Math.Round(lon, 7));
        json.WriteNumberValue(Math.Round(lat, 7));
        json.WriteEndArray();
        json.WriteEndObject();
    }

    // ── KML ───────────────────────────────────────────────────────────────────

    // RSSI bands, as in WiGLE's survey KML, coloured with WifiDB's signal-map colours (KML is aabbggrr)
    private static readonly (int UpTo, string Id, string Color)[] Bands =
    {
        (-100, "r_100_down", "cc002fe4"), (-90, "r_99_90", "cc0000ff"), (-80, "r_89_80", "cc0050ff"),
        (-70, "r_79_70", "cc0092ff"), (-60, "r_69_60", "cc00ecff"), (-50, "r_59_50", "cc00ff80"),
        (int.MaxValue, "r_49_up", "cc00760d"),
    };

    private static string BandFor(int rssi) => Bands.First(b => rssi <= b.UpTo).Id;

    /// <summary>A KML signal map: a folder per AP, with a dot for each reading coloured by its RSSI.</summary>
    public static async Task WriteKmlSignalMapAsync(string path, IEnumerable<AccessPoint> aps, string title)
    {
        var settings = new XmlWriterSettings { Indent = true, IndentChars = " ", Encoding = new UTF8Encoding(false), Async = true };
        await using var writer = XmlWriter.Create(path, settings);
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "kml", "http://www.opengis.net/kml/2.2");
        await writer.WriteStartElementAsync(null, "Document", null);
        await writer.WriteElementStringAsync(null, "name", null, ExportService.XmlText(title));

        foreach (var (_, id, color) in Bands)
        {
            await writer.WriteStartElementAsync(null, "Style", null);
            await writer.WriteAttributeStringAsync(null, "id", null, id);
            await writer.WriteStartElementAsync(null, "IconStyle", null);
            await writer.WriteElementStringAsync(null, "color", null, color);
            await writer.WriteElementStringAsync(null, "scale", null, "0.6");
            await writer.WriteStartElementAsync(null, "Icon", null);
            await writer.WriteElementStringAsync(null, "href", null, "http://maps.google.com/mapfiles/kml/shapes/shaded_dot.png");
            await writer.WriteEndElementAsync(); // Icon
            await writer.WriteEndElementAsync(); // IconStyle
            await writer.WriteStartElementAsync(null, "LabelStyle", null);
            await writer.WriteElementStringAsync(null, "scale", null, "0");
            await writer.WriteEndElementAsync(); // LabelStyle
            await writer.WriteEndElementAsync(); // Style
        }

        foreach (var ap in aps)
        {
            var readings = Positioned(ap).OrderBy(h => h.GpsData!.Timestamp).ToList();
            if (readings.Count == 0) continue;
            await writer.WriteStartElementAsync(null, "Folder", null);
            await writer.WriteElementStringAsync(null, "name", null,
                ExportService.XmlText(string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : $"{ap.Ssid} ({ap.Bssid})"));
            await writer.WriteElementStringAsync(null, "open", null, "0");
            foreach (var h in readings)
            {
                var g = h.GpsData!;
                int rssi = RssiOf(h);
                var time = Utc(g.Timestamp);
                await writer.WriteStartElementAsync(null, "Placemark", null);
                await writer.WriteElementStringAsync(null, "name", null, $"{rssi} dBm");
                await writer.WriteElementStringAsync(null, "description", null,
                    $"{ExportService.XmlText(ap.Ssid)}\n{ap.Bssid}\nRSSI {rssi} dBm, signal {h.Signal}%\n{time.ToString("yyyy-MM-dd HH:mm:ss", Inv)} UTC");
                await writer.WriteStartElementAsync(null, "TimeStamp", null);
                await writer.WriteElementStringAsync(null, "when", null, time.ToString("yyyy-MM-ddTHH:mm:ssZ", Inv));
                await writer.WriteEndElementAsync();
                await writer.WriteElementStringAsync(null, "styleUrl", null, "#" + BandFor(rssi));
                await writer.WriteStartElementAsync(null, "ExtendedData", null);
                foreach (var (name, value) in new[] { ("rssi", rssi.ToString(Inv)), ("signal", h.Signal.ToString(Inv)) })
                {
                    await writer.WriteStartElementAsync(null, "Data", null);
                    await writer.WriteAttributeStringAsync(null, "name", null, name);
                    await writer.WriteElementStringAsync(null, "value", null, value);
                    await writer.WriteEndElementAsync();
                }
                await writer.WriteEndElementAsync(); // ExtendedData
                await writer.WriteStartElementAsync(null, "Point", null);
                await writer.WriteElementStringAsync(null, "coordinates", null,
                    $"{g.Longitude.ToString("F7", Inv)},{g.Latitude.ToString("F7", Inv)},0");
                await writer.WriteEndElementAsync(); // Point
                await writer.WriteEndElementAsync(); // Placemark
            }
            await writer.WriteEndElementAsync(); // Folder
        }

        await writer.WriteEndElementAsync(); // Document
        await writer.WriteEndElementAsync(); // kml
        await writer.WriteEndDocumentAsync();
    }
}
