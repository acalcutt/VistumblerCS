using System.IO.Compression;
using System.Text;
using System.Xml;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using Vistumbler.Core.Extensions;

namespace Vistumbler.Infrastructure.Export;

/// <summary>
/// Service for exporting access point data to various formats
/// </summary>
public class ExportService : IExportService
{
    public async Task ExportToNs1Async(string filePath, List<AccessPoint> accessPoints)
    {
        await Task.Run(() =>
        {
             using var fs = File.Create(filePath);
             using var writer = new BinaryWriter(fs);
             
             // Header
             writer.Write("NetS".ToCharArray());
             writer.Write((uint)12);
             writer.Write((uint)accessPoints.Count);
             
             foreach(var ap in accessPoints)
             {
                 // SSID
                 var ssidBytes = Encoding.ASCII.GetBytes(ap.Ssid ?? "");
                 writer.Write((byte)ssidBytes.Length);
                 writer.Write(ssidBytes);
                 
                 // BSSID
                 var bssidParts = (ap.Bssid ?? "00:00:00:00:00:00").Split(':', '-');
                 if (bssidParts.Length == 6)
                 {
                    foreach(var part in bssidParts)
                    {
                        try { writer.Write(Convert.ToByte(part, 16)); } catch { writer.Write((byte)0); }
                    }
                 }
                 else
                 {
                     writer.Write(new byte[6]);
                 }
                 
                 writer.Write((int)(ap.HighestSignal ?? 0));
                 writer.Write((int)0); // MinNoise (unknown)
                 writer.Write((int)(ap.HighestRssi ?? 0));
                 
                 // Flags
                 uint flags = 0;
                 if (ap.NetworkType == NetworkType.Infrastructure) flags |= 0x0001;
                 else if (ap.NetworkType == NetworkType.Adhoc) flags |= 0x0002;
                 
                 if (ap.Encryption != EncryptionType.None)
                 {
                     flags |= 0x0010;
                 }
                 writer.Write(flags);
                 
                 writer.Write((uint)100); // Beacon Interval (dummy)
                 
                 writer.Write(ToFileTimeSafe(ap.FirstSeen));
                 writer.Write(ToFileTimeSafe(ap.LastSeen));
                 
                 writer.Write(ap.Latitude ?? 0.0);
                 writer.Write(ap.Longitude ?? 0.0);
                 
                 // Signal History
                 writer.Write((uint)ap.SignalHistory.Count);
                 foreach(var hist in ap.SignalHistory)
                 {
                     writer.Write(ToFileTimeSafe(hist.Timestamp));
                     writer.Write(hist.Signal);
                     writer.Write((int)0); // Noise
                     
                     if (hist.GpsData != null)
                     {
                         writer.Write((int)1); // LocationSource = GPS
                         writer.Write(hist.GpsData.Latitude);
                         writer.Write(hist.GpsData.Longitude);
                         writer.Write(hist.GpsData.Altitude ?? 0);
                         writer.Write((uint)hist.GpsData.NumberOfSatellites);
                         
                         // Speed in KMH
                         var kmh = (hist.GpsData.SpeedKnots ?? 0) * 1.852;
                         writer.Write(kmh);
                         
                         writer.Write(hist.GpsData.TrackAngle ?? 0);
                         writer.Write((double)0); // MagVar
                         writer.Write(hist.GpsData.HorizontalDilution ?? 0);
                     }
                     else
                     {
                         writer.Write((int)0); // LocationSource = None
                     }
                 }
                 
                 // Name (using SSID or Label?)
                 var name = ap.Label;
                 if (string.IsNullOrEmpty(name)) name = ap.Ssid;
                 var nameBytes = Encoding.ASCII.GetBytes(name ?? "");
                 writer.Write((byte)nameBytes.Length);
                 writer.Write(nameBytes);
                 
                 // Channels (Bitmask)
                 ulong channelsMask = GetChannelBitMask(ap.Channel);
                 writer.Write(channelsMask);
                 
                 writer.Write((uint)ap.Channel); // LastChannel
                 
                 writer.Write((uint)0); // IP
                 writer.Write((int)(ap.Signal ?? 0)); // MinSignal
                 writer.Write((int)0); // MaxNoise
                 writer.Write((uint)0); // DataRate
                 writer.Write((uint)0); // IPSubnet
                 writer.Write((uint)0); // IPMask

                 // Calculate ApFlags for Vistumbler Custom Fields
                 uint apFlags = 0;
                 
                 // Auth Flags (Legacy Basic)
                 if (ap.Authentication == AuthenticationType.WPA_PSK) apFlags |= 0x0001;
                 else if (ap.Authentication == AuthenticationType.WPA_Enterprise) apFlags |= 0x0002;
                 else if (ap.Authentication == AuthenticationType.WPA2_PSK) apFlags |= 0x0004;
                 else if (ap.Authentication == AuthenticationType.WPA2_Enterprise) apFlags |= 0x0008;

                 // Auth Flags (Extended)
                 if (ap.Authentication == AuthenticationType.WPA3_PSK || // SAE (WPA3-Personal) 
                     ap.Authentication.ToString().Contains("WPA3")) 
                 {
                     apFlags |= 0x0010;
                 }

                 if (ap.Authentication == AuthenticationType.OWE) apFlags |= 0x0020;

                 // Encr Flags (Legacy Basic)
                 if (ap.Encryption == EncryptionType.TKIP) apFlags |= 0x0040;
                 else if (ap.Encryption == EncryptionType.CCMP || ap.Encryption == EncryptionType.AES) apFlags |= 0x0080;

                 // Encr Flags (Extended)
                 if (ap.Encryption == EncryptionType.GCMP) apFlags |= 0x0100;
                 if (ap.Encryption == EncryptionType.GCMP_256) apFlags |= 0x0200;
                 if (ap.Encryption == EncryptionType.CCMP_256) apFlags |= 0x0400;
                 if (ap.Encryption.ToString().StartsWith("BIP")) apFlags |= 0x0800;

                 writer.Write(apFlags); // ApFlags
                 
                 writer.Write((uint)0); // IELength
                 // No IEs
             }
        });
    }

    private ulong GetChannelBitMask(int channel)
    {
        if (channel >= 1 && channel <= 14) return (ulong)1 << (channel - 1);
        
        switch(channel)
        {
            case 34: return 0x80000000;
            case 36: return 0x00008000;
            case 38: return 0x08000000;
            case 40: return 0x00010000;
            case 42: return 0x100000000;
            case 44: return 0x00020000;
            case 46: return 0x10000000;
            case 48: return 0x00040000;
            case 52: return 0x00080000;
            case 54: return 0x20000000;
            case 56: return 0x00100000;
            case 60: return 0x00200000;
            case 62: return 0x40000000;
            case 64: return 0x00400000;
            case 149: return 0x00800000;
            case 153: return 0x01000000;
            case 157: return 0x02000000;
            case 161: return 0x04000000;
            default: return 0;
        }
    }

    private long ToFileTimeSafe(DateTime date)
    {
        try
        {
            if (date.Year < 1601) return 0;
            return date.ToFileTimeUtc();
        }
        catch
        {
            return 0;
        }
    }

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
                      $"{ap.Authentication.ToLegacyString()},{ap.Encryption.ToLegacyString()},{ap.NetworkType}," +
                      $"{ap.Signal},{ap.HighestSignal},{ap.Rssi},{ap.HighestRssi}," +
                      $"{EscapeCsv(ap.RadioType)},{EscapeCsv(ap.Manufacturer)},{EscapeCsv(ap.Label)}," +
                      $"{ap.Latitude},{ap.Longitude}," +
                      $"{ap.FirstSeen:yyyy-MM-dd HH:mm:ss},{ap.LastSeen:yyyy-MM-dd HH:mm:ss}";
            
            await writer.WriteLineAsync(line);
        }
    }

    public async Task ExportToWigleCsvAsync(string filePath, List<AccessPoint> accessPoints)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        // Write Wigle Header
        await writer.WriteLineAsync("WigleWifi-1.4,appRelease=VistumblerCS,model=PC,release=1.0,device=PC,display=,board=,brand=");
        await writer.WriteLineAsync("MAC,SSID,AuthMode,FirstSeen,Channel,RSSI,CurrentLatitude,CurrentLongitude,AltitudeMeters,AccuracyMeters,Type");

        foreach (var ap in accessPoints)
        {
            // Map Auth/Enc to Wigle Strings somewhat
            string auth = "[ESS]";
            if (ap.Encryption == EncryptionType.WEP) auth = "[WEP][ESS]";
            if (ap.Authentication == AuthenticationType.WPA) auth = "[WPA-PSK-?][ESS]"; 
            if (ap.Authentication == AuthenticationType.WPA2) auth = "[WPA2-PSK-?][ESS]";
            // This is a simplified mapping.
            
            var line = $"{ap.Bssid},{EscapeCsv(ap.Ssid)},{auth},{ap.FirstSeen:yyyy-MM-dd HH:mm:ss},{ap.Channel},{ap.Rssi},{ap.Latitude},{ap.Longitude},0,0,WIFI";
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
            var line = $"AP|{ap.ApId}|{ap.Bssid}|{ap.Ssid}|{ap.Channel}|{ap.Authentication.ToLegacyString()}|{ap.Encryption.ToLegacyString()}|" +
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
        var tempVs1 = Path.GetTempFileName();
        try
        {
            await ExportToVs1Async(tempVs1, accessPoints);

            if (File.Exists(filePath))
                File.Delete(filePath);

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            var entryName = Path.GetFileNameWithoutExtension(filePath) + ".vs1";
            archive.CreateEntryFromFile(tempVs1, entryName);
        }
        finally
        {
            if (File.Exists(tempVs1))
                File.Delete(tempVs1);
        }
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
