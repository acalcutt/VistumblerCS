using System.Text;
using System.Xml;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Export;

/// <summary>
/// Service for exporting access point data to various formats
/// </summary>
public class ExportService : IExportService
{
    public async Task ExportToKmlAsync(string filePath, List<AccessPoint> accessPoints, ExportOptions options)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8,
            Async = true
        };

        using var writer = XmlWriter.Create(filePath, settings);
        
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "kml", "http://www.opengis.net/kml/2.2");
        await writer.WriteStartElementAsync(null, "Document", null);
        
        // Write document name
        await writer.WriteElementStringAsync(null, "name", null, "Vistumbler WiFi Scan");
        
        // Write styles for different signal strengths
        await WriteKmlStylesAsync(writer);
        
        // Filter access points based on options
        var filteredAps = FilterAccessPoints(accessPoints, options);
        
        // Write placemarks for each access point
        foreach (var ap in filteredAps)
        {
            if (ap.Latitude.HasValue && ap.Longitude.HasValue)
            {
                await WritePlacemarkAsync(writer, ap, options);
            }
        }
        
        await writer.WriteEndElementAsync(); // Document
        await writer.WriteEndElementAsync(); // kml
        await writer.WriteEndDocumentAsync();
    }

    public async Task ExportToGpxAsync(string filePath, List<AccessPoint> accessPoints)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8,
            Async = true
        };

        using var writer = XmlWriter.Create(filePath, settings);
        
        await writer.WriteStartDocumentAsync();
        await writer.WriteStartElementAsync(null, "gpx", "http://www.topografix.com/GPX/1/1");
        await writer.WriteAttributeStringAsync(null, "version", null, "1.1");
        await writer.WriteAttributeStringAsync(null, "creator", null, "Vistumbler CS");
        
        foreach (var ap in accessPoints.Where(a => a.Latitude.HasValue && a.Longitude.HasValue))
        {
            await writer.WriteStartElementAsync(null, "wpt", null);
            await writer.WriteAttributeStringAsync(null, "lat", null, ap.Latitude!.Value.ToString("F6"));
            await writer.WriteAttributeStringAsync(null, "lon", null, ap.Longitude!.Value.ToString("F6"));
            
            await writer.WriteElementStringAsync(null, "name", null, ap.Ssid);
            await writer.WriteElementStringAsync(null, "desc", null, 
                $"BSSID: {ap.Bssid}, Signal: {ap.Signal}%, Channel: {ap.Channel}");
            
            await writer.WriteEndElementAsync(); // wpt
        }
        
        await writer.WriteEndElementAsync(); // gpx
        await writer.WriteEndDocumentAsync();
    }

    public async Task ExportToCsvAsync(string filePath, List<AccessPoint> accessPoints, bool includeSignalHistory = false)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        // Write header
        await writer.WriteLineAsync(
            "BSSID,SSID,Channel,Authentication,Encryption,NetworkType,Signal,HighSignal,RSSI,HighRSSI," +
            "RadioType,Manufacturer,Label,Latitude,Longitude,FirstSeen,LastSeen");
        
        // Write data
        foreach (var ap in accessPoints)
        {
            var line = $"{EscapeCsv(ap.Bssid)},{EscapeCsv(ap.Ssid)},{ap.Channel}," +
                      $"{ap.Authentication},{ap.Encryption},{ap.NetworkType}," +
                      $"{ap.Signal},{ap.HighestSignal},{ap.Rssi},{ap.HighestRssi}," +
                      $"{EscapeCsv(ap.RadioType)},{EscapeCsv(ap.Manufacturer)},{EscapeCsv(ap.Label)}," +
                      $"{ap.Latitude},{ap.Longitude}," +
                      $"{ap.FirstSeen:yyyy-MM-dd HH:mm:ss},{ap.LastSeen:yyyy-MM-dd HH:mm:ss}";
            
            await writer.WriteLineAsync(line);
        }
    }

    public async Task ExportToVs1Async(string filePath, List<AccessPoint> accessPoints)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        // Write header
        await writer.WriteLineAsync("# Vistumbler VS1 File");
        await writer.WriteLineAsync($"# Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await writer.WriteLineAsync("# GpsID|Latitude|Longitude|NumOfSats|HorDilPitch|Alt|Geo|SpeedInMPH|SpeedInKmH|TrackAngle|Date|Time");
        await writer.WriteLineAsync("# APID|BSSID|SSID|CHAN|AUTH|ENCR|RADTYPE|BTX|OTX|NETTYPE|Signal|HighSignal|RSSI|HighRSSI|Latitude|Longitude|FirstSeen|LastSeen|Manufacturer|Label");
        await writer.WriteLineAsync("# HistID|APID|GpsID|Signal|RSSI|Date|Time");
        
        // Write access points
        foreach (var ap in accessPoints)
        {
            var line = $"AP|{ap.ApId}|{ap.Bssid}|{ap.Ssid}|{ap.Channel}|{ap.Authentication}|{ap.Encryption}|" +
                      $"{ap.RadioType}|{ap.BasicTransferRates}|{ap.OtherTransferRates}|{ap.NetworkType}|" +
                      $"{ap.Signal}|{ap.HighestSignal}|{ap.Rssi}|{ap.HighestRssi}|" +
                      $"{ap.Latitude}|{ap.Longitude}|{ap.FirstSeen:yyyy-MM-dd HH:mm:ss}|{ap.LastSeen:yyyy-MM-dd HH:mm:ss}|" +
                      $"{ap.Manufacturer}|{ap.Label}";
            
            await writer.WriteLineAsync(line);
        }
    }

    public async Task ExportToVszAsync(string filePath, List<AccessPoint> accessPoints)
    {
        // VSZ is a compressed VS1 file
        // This would use System.IO.Compression to create a ZIP file containing the VS1 data
        var tempVs1 = Path.GetTempFileName();
        await ExportToVs1Async(tempVs1, accessPoints);
        
        // TODO: Compress the VS1 file to VSZ format
        // For now, just copy the file
        File.Copy(tempVs1, filePath, true);
        File.Delete(tempVs1);
    }

    private List<AccessPoint> FilterAccessPoints(List<AccessPoint> accessPoints, ExportOptions options)
    {
        return accessPoints.Where(ap =>
        {
            if (ap.Encryption == EncryptionType.None && !options.IncludeOpenNetworks)
                return false;
            
            if (ap.Encryption == EncryptionType.WEP && !options.IncludeWepNetworks)
                return false;
            
            if ((ap.Encryption == EncryptionType.TKIP || ap.Encryption == EncryptionType.AES) && !options.IncludeSecureNetworks)
                return false;
            
            return true;
        }).ToList();
    }

    private async Task WriteKmlStylesAsync(XmlWriter writer)
    {
        // Define styles for different signal strengths
        var signalColors = new[]
        {
            ("VeryLow", "ff0000ff"),   // Red
            ("Low", "ff0055ff"),        // Orange
            ("Medium", "ff00ffff"),     // Yellow
            ("Good", "ff01ffc8"),       // Light Green
            ("Excellent", "ff70ff48")   // Green
        };

        foreach (var (name, color) in signalColors)
        {
            await writer.WriteStartElementAsync(null, "Style", null);
            await writer.WriteAttributeStringAsync(null, "id", null, $"Signal{name}");
            
            await writer.WriteStartElementAsync(null, "IconStyle", null);
            await writer.WriteElementStringAsync(null, "color", null, color);
            await writer.WriteEndElementAsync(); // IconStyle
            
            await writer.WriteEndElementAsync(); // Style
        }
    }

    private async Task WritePlacemarkAsync(XmlWriter writer, AccessPoint ap, ExportOptions options)
    {
        await writer.WriteStartElementAsync(null, "Placemark", null);
        
        // Name
        await writer.WriteElementStringAsync(null, "name", null, string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : ap.Ssid);
        
        // Description
        var description = $"BSSID: {ap.Bssid}\n" +
                         $"Channel: {ap.Channel}\n" +
                         $"Signal: {ap.Signal}%\n" +
                         $"RSSI: {ap.Rssi} dBm\n" +
                         $"Authentication: {ap.Authentication}\n" +
                         $"Encryption: {ap.Encryption}\n" +
                         $"Manufacturer: {ap.Manufacturer}";
        
        await writer.WriteElementStringAsync(null, "description", null, description);
        
        // Style based on signal strength
        if (options.UseSignalColors)
        {
            var styleId = GetSignalStyleId(ap.Signal ?? 0);
            await writer.WriteElementStringAsync(null, "styleUrl", null, $"#{styleId}");
        }
        
        // Point coordinates
        await writer.WriteStartElementAsync(null, "Point", null);
        await writer.WriteElementStringAsync(null, "coordinates", null, 
            $"{ap.Longitude},{ap.Latitude},0");
        await writer.WriteEndElementAsync(); // Point
        
        await writer.WriteEndElementAsync(); // Placemark
    }

    private string GetSignalStyleId(int signal)
    {
        return signal switch
        {
            >= 80 => "SignalExcellent",
            >= 60 => "SignalGood",
            >= 40 => "SignalMedium",
            >= 20 => "SignalLow",
            _ => "SignalVeryLow"
        };
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        
        return value;
    }
}
