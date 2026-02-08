using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Import;

public class ImportService : IImportService
{
    public async Task<List<AccessPoint>> ImportFromNs1Async(string filePath)
    {
        return await Task.Run(() => 
        {
            var accessPoints = new List<AccessPoint>();
            if (!File.Exists(filePath)) return accessPoints;

            using var fs = File.OpenRead(filePath);
            using var reader = new BinaryReader(fs);

            // Read Header
            if (fs.Length < 12) return accessPoints;
            var signature = new string(reader.ReadChars(4));
            if (signature != "NetS") return accessPoints; // Invalid signature

            var version = reader.ReadUInt32();
            if (version != 12) return accessPoints; // Only version 12 supported

            var apCount = reader.ReadUInt32();

            for (int i = 0; i < apCount; i++)
            {
                var ap = new AccessPoint();
                
                // Read AP Info
                var ssidLength = reader.ReadByte();
                var ssidChars = reader.ReadChars(ssidLength);
                ap.Ssid = new string(ssidChars);
                
                var bssidBytes = reader.ReadBytes(6);
                ap.Bssid = BitConverter.ToString(bssidBytes).Replace("-", ":");
                
                var maxSignal = reader.ReadInt32(); // HighSignal
                ap.HighestSignal = maxSignal;
                
                var minNoise = reader.ReadInt32();
                
                var maxSnr = reader.ReadInt32(); // HighRSSI
                ap.HighestRssi = maxSnr;
                
                var flags = reader.ReadUInt32();
                // Map flags if needed. Bit 4 is Privacy.
                if ((flags & 0x0010) != 0) 
                {
                    ap.Encryption = EncryptionType.WEP;
                }
                else
                {
                    ap.Encryption = EncryptionType.None;
                }
                
                // Network Type (ESS vs IBSS)
                if ((flags & 0x0001) != 0) ap.NetworkType = NetworkType.Infrastructure;
                else if ((flags & 0x0002) != 0) ap.NetworkType = NetworkType.Adhoc;

                var beaconInterval = reader.ReadUInt32();
                
                var firstSeenFileTime = reader.ReadInt64();
                try { ap.FirstSeen = DateTime.FromFileTimeUtc(firstSeenFileTime); } catch { ap.FirstSeen = DateTime.MinValue; }
                
                var lastSeenFileTime = reader.ReadInt64();
                try { ap.LastSeen = DateTime.FromFileTimeUtc(lastSeenFileTime); } catch { ap.LastSeen = DateTime.MinValue; }
                
                var bestLat = reader.ReadDouble();
                var bestLong = reader.ReadDouble();
                
                if (bestLat != 0 || bestLong != 0)
                {
                    ap.Latitude = bestLat;
                    ap.Longitude = bestLong;
                }

                var dataCount = reader.ReadUInt32();

                // Signal History
                for (int j = 0; j < dataCount; j++)
                {
                    var hist = new SignalHistory();
                    
                    var histTime = reader.ReadInt64();
                    try { hist.Timestamp = DateTime.FromFileTimeUtc(histTime); } catch { hist.Timestamp = DateTime.MinValue; }
                    
                    hist.Signal = reader.ReadInt32();
                    var histNoise = reader.ReadInt32();
                    
                    var locationSource = reader.ReadInt32();
                    
                    if (locationSource == 1) // GPS
                    {
                        var gps = new GpsData();
                        gps.Timestamp = hist.Timestamp;
                        gps.Latitude = reader.ReadDouble();
                        gps.Longitude = reader.ReadDouble();
                        gps.Altitude = reader.ReadDouble();
                        gps.NumberOfSatellites = (int)reader.ReadUInt32();
                        
                        var speedKmh = reader.ReadDouble();
                        gps.SpeedKnots = speedKmh / 1.852; 
                        
                        gps.TrackAngle = reader.ReadDouble();
                        var magVar = reader.ReadDouble();
                        gps.HorizontalDilution = reader.ReadDouble();
                        gps.Quality = GpsQuality.GpsFix;
                        
                        hist.GpsData = gps;
                    }
                    
                    ap.SignalHistory.Add(hist);
                }

                // Remaining AP fields
                var nameLength = reader.ReadByte();
                var nameChars = reader.ReadChars(nameLength); 
                // ignoring name
                
                var channels = reader.ReadUInt64();
                var lastChannel = reader.ReadUInt32();
                ap.Channel = (int)lastChannel;
                
                var ipAddress = reader.ReadUInt32();
                var minSignal = reader.ReadInt32();
                var maxNoise = reader.ReadInt32();
                var dataRate = reader.ReadUInt32(); 
                
                var ipSubnet = reader.ReadUInt32();
                var ipMask = reader.ReadUInt32();
                var apFlags = reader.ReadUInt32();
                
                var ieLength = reader.ReadUInt32();
                reader.ReadBytes((int)ieLength); // Skip IEs
                
                accessPoints.Add(ap);
            }

            return accessPoints;
        });
    }

    public async Task<List<AccessPoint>> ImportFromVs1Async(string filePath)
    {
        var accessPoints = new List<AccessPoint>();
        if (!File.Exists(filePath)) return accessPoints;

        var lines = await File.ReadAllLinesAsync(filePath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split('|');
            // Check for valid VS1 line (starts with AP and has enough parts)
            if (parts.Length >= 19 && parts[0] == "AP")
            {
                var ap = ParseVs1Line(parts);
                if (ap != null) accessPoints.Add(ap);
            }
            // Check for Vistumbler V4 VS1 format (at least 15 columns, index 1 is BSSID)
            else if (parts.Length >= 15 && IsMacAddress(parts[1]))
            {
                var ap = ParseExternalVs1Line(parts);
                if (ap != null) accessPoints.Add(ap);
            }
        }
        return accessPoints;
    }

    private bool IsMacAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Simple MAC check (XX:XX:XX:XX:XX:XX)
        return Regex.IsMatch(value, @"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$");
    }

    private AccessPoint? ParseExternalVs1Line(string[] parts)
    {
        try 
        {
            // V4 Format:
            // 0:SSID|1:BSSID|2:MANUF|3:Auth|4:Encr|5:SecType|6:RadType|7:Chan
            // 8:BasicRates|9:OtherRates|10:HighSignal|11:HighRSSI|12:NetType|13:Label|14:History
            
            var ap = new AccessPoint
            {
                Ssid = parts[0],
                Bssid = parts[1],
                Manufacturer = parts[2],
                Authentication = ParseAuthentication(parts[3]),
                Encryption = ParseEncryption(parts[4]),
                RadioType = parts[6],
                Channel = ParseInt(parts[7]),
                BasicTransferRates = parts[8],
                OtherTransferRates = parts[9],
                HighestSignal = ParseInt(parts[10]),
                HighestRssi = ParseInt(parts[11]),
                NetworkType = Enum.TryParse<NetworkType>(parts[12], true, out var nt) ? nt : NetworkType.Unknown,
                Label = parts[13] == "Unknown" ? string.Empty : parts[13],
                FirstSeen = DateTime.Now, // Default since dates are in GPS section
                LastSeen = DateTime.Now
            };

            // Use Highest as current if history parsing is skipped for now
            ap.Signal = ap.HighestSignal;
            ap.Rssi = ap.HighestRssi;

            // Try parse history for better Signal/RSSI?
            // History format: GID,SIGNAL,RSSI\GID,SIGNAL,RSSI
            if (parts.Length > 14 && !string.IsNullOrWhiteSpace(parts[14]))
            {
                var entries = parts[14].Split('\\');
                if (entries.Length > 0)
                {
                    var lastEntry = entries[entries.Length - 1].Split(',');
                    if (lastEntry.Length >= 3)
                    {
                         ap.Signal = ParseInt(lastEntry[1]);
                         ap.Rssi = ParseInt(lastEntry[2]);
                    }
                }
            }

            return ap;
        }
        catch
        {
            return null;
        }
    }

    private AuthenticationType ParseAuthentication(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return AuthenticationType.Unknown;
        
        // Handle VS1 specific strings
        if (value.Equals("WPA2-Personal", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2_PSK;
        if (value.Equals("WPA-Personal", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA_PSK;
        if (value.Equals("WPA2-Enterprise", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2_Enterprise;
        if (value.Equals("WPA-Enterprise", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA_Enterprise;
        if (value.Equals("OWE", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.Open; // Map OWE to Open for now or add enum
        if (value.Equals("Open", StringComparison.OrdinalIgnoreCase)) return AuthenticationType.Open;

        return Enum.TryParse<AuthenticationType>(value, true, out var auth) ? auth : AuthenticationType.Unknown;
    }

    private EncryptionType ParseEncryption(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return EncryptionType.Unknown;

        if (value.Equals("CCMP", StringComparison.OrdinalIgnoreCase)) return EncryptionType.CCMP; // Or AES if CCMP not in enum? It is in enum.
        if (value.Equals("None", StringComparison.OrdinalIgnoreCase)) return EncryptionType.None;
        
        return Enum.TryParse<EncryptionType>(value, true, out var enc) ? enc : EncryptionType.Unknown;
    }


    public async Task<List<AccessPoint>> ImportFromVszAsync(string filePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "VistumblerImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try 
        {
            ZipFile.ExtractToDirectory(filePath, tempDir);
            
            // Find the VS1 or TXT file inside
            var vs1File = Directory.GetFiles(tempDir, "*.vs1").FirstOrDefault() 
                          ?? Directory.GetFiles(tempDir, "*.txt").FirstOrDefault();
            
            if (vs1File != null)
            {
                return await ImportFromVs1Async(vs1File);
            }
            return new List<AccessPoint>();
        }
        catch
        {
            // Handle invalid zip or extraction errors gracefully
            return new List<AccessPoint>();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async Task<List<AccessPoint>> ImportFromCsvAsync(string filePath)
    {
        var accessPoints = new List<AccessPoint>();
        if (!File.Exists(filePath)) return accessPoints;

        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length == 0) return accessPoints;

        // Simple heuristic to detect format
        var header = lines[0];
        bool isWigle = header.Contains("WigleWifi", StringComparison.OrdinalIgnoreCase) || 
                       header.Contains("MAC,SSID,AuthMode", StringComparison.OrdinalIgnoreCase);

        // Parse remaining lines
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Simple split for now. TODO: Handle quoted CSV fields properly if Vistumbler export produces them.
            // Using a simple regex split for quoted CSV: 
            var parts = SplitCsvLine(line);
            
            AccessPoint? ap = null;
            if (isWigle)
                ap = ParseWigleLine(parts);
            else
                ap = ParseDetailedCsvLine(parts);

            if (ap != null) accessPoints.Add(ap);
        }

        return accessPoints;
    }

    // --- Helpers ---

    private AccessPoint? ParseVs1Line(string[] parts)
    {
        try
        {
            // Expected indices based on ExportToVs1Async:
            // 0: AP, 1: Id, 2: Bssid, 3: Ssid, 4: Chan, 5: Auth, 6: Encr, 7: RadType, 8: BasicRates, 9: OtherRates, 10: NetType
            // 11: Sig, 12: HighSig, 13: Rssi, 14: HighRssi, 15: Lat, 16: Lon, 17: FirstSeen, 18: LastSeen, 19: Manuf, 20: Label
            
            var ap = new AccessPoint
            {
                Bssid = parts[2],
                Ssid = parts[3],
                Channel = ParseInt(parts[4]),
                Authentication = Enum.TryParse<AuthenticationType>(parts[5], true, out var auth) ? auth : AuthenticationType.Unknown,
                Encryption = Enum.TryParse<EncryptionType>(parts[6], true, out var enc) ? enc : EncryptionType.Unknown,
                RadioType = parts[7],
                // BasicTransferRates = parts[8], 
                // OtherTransferRates = parts[9],
                NetworkType = Enum.TryParse<NetworkType>(parts[10], true, out var nt) ? nt : NetworkType.Unknown,
                Signal = ParseInt(parts[11]),
                HighestSignal = ParseInt(parts[12]),
                Rssi = ParseInt(parts[13]),
                HighestRssi = ParseInt(parts[14]),
                Latitude = ParseDouble(parts[15]),
                Longitude = ParseDouble(parts[16]),
                FirstSeen = DateTime.TryParse(parts[17], out var fs) ? fs : DateTime.UtcNow,
                LastSeen = DateTime.TryParse(parts[18], out var ls) ? ls : DateTime.UtcNow,
            };
            
            if (parts.Length > 19) ap.Manufacturer = parts[19];
            if (parts.Length > 20) ap.Label = parts[20];
            
            return ap;
        }
        catch
        {
            return null; // Skip malformed lines
        }
    }

    private AccessPoint? ParseNs1Line(string[] parts)
    {
        try
        {
            // Columns: 0:Lat, 1:Lon, 2:( SSID ), 3:Type, 4:( BSSID ), 5:Time, 6:[ SNR Sig Noise ], 7:Name, 8:Flags, 9:ChanBits, 10:BcnInt, 11:DataRate, 12:Channel
            
            var ap = new AccessPoint();
            ap.Latitude = ParseNs1Gps(parts[0]);
            ap.Longitude = ParseNs1Gps(parts[1]);
            
            ap.Ssid = TrimNs1String(parts[2]);
            ap.Bssid = TrimNs1String(parts[4]);
            
            // Signal parsing logic from AutoIt: "subtracts 95 to calculate RSSI" from middle value
            var signalParts = parts[6].Trim('[', ']', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (signalParts.Length >= 2 && int.TryParse(signalParts[1], out var sigVal))
            {
                ap.Rssi = sigVal - 95;
                ap.Signal = Math.Clamp((ap.Rssi.Value + 100) * 2, 0, 100);
            }
            
            // Flags
            if (parts[8].StartsWith("0x") && int.TryParse(parts[8].Substring(2), NumberStyles.HexNumber, null, out var flags))
            {
                ap.Encryption = (flags & 0x10) != 0 ? EncryptionType.WEP : EncryptionType.None;
                ap.NetworkType = (flags & 0x1) != 0 ? NetworkType.Infrastructure : 
                                 (flags & 0x2) != 0 ? NetworkType.Adhoc : NetworkType.Unknown;
            }
            
            ap.Channel = ParseInt(parts[12]);
            
            ap.FirstSeen = DateTime.UtcNow; // NS1 doesn't have full date usually, just time? AutoIt gets Date from header.
            ap.LastSeen = DateTime.UtcNow;

            return ap;
        }
        catch
        {
            return null;
        }
    }
    
    private AccessPoint? ParseDetailedCsvLine(string[] parts)
    {
        if (parts.Length < 10) return null;
        try
        {
            // "BSSID,SSID,Channel,Authentication,Encryption,NetworkType,Signal,HighSignal,RSSI,HighRSSI,RadioType,Manufacturer,Label,Latitude,Longitude,FirstSeen,LastSeen"
             return new AccessPoint
             {
                 Bssid = parts[0],
                 Ssid = parts[1],
                 Channel = ParseInt(parts[2]),
                 Authentication = Enum.TryParse<AuthenticationType>(parts[3], true, out var a) ? a : AuthenticationType.Unknown,
                 Encryption = Enum.TryParse<EncryptionType>(parts[4], true, out var e) ? e : EncryptionType.Unknown,
                 NetworkType = Enum.TryParse<NetworkType>(parts[5], true, out var n) ? n : NetworkType.Unknown,
                 Signal = ParseInt(parts[6]),
                 HighestSignal = ParseInt(parts[7]),
                 Rssi = ParseInt(parts[8]),
                 HighestRssi = ParseInt(parts[9]),
                 // ... handle rest if available
                 Latitude = parts.Length > 13 ? ParseDouble(parts[13]) : null,
                 Longitude = parts.Length > 14 ? ParseDouble(parts[14]) : null,
             };
        }
        catch { return null; }
    }

    private AccessPoint? ParseWigleLine(string[] parts)
    {
        if (parts.Length < 7) return null;
        try
        {
            // MAC,SSID,AuthMode,FirstSeen,Channel,RSSI,CurrentLatitude,CurrentLongitude...
            var ap = new AccessPoint
            {
                Bssid = parts[0],
                Ssid = parts[1],
                Channel = ParseInt(parts[4]),
                Rssi = ParseInt(parts[5]),
                Latitude = parts.Length > 6 ? ParseDouble(parts[6]) : null,
                Longitude = parts.Length > 7 ? ParseDouble(parts[7]) : null
            };
            
            // Map wigle auth to Vistumbler auth if possible
            var authStr = parts[2];
            if (authStr.Contains("WPA2")) ap.Authentication = AuthenticationType.WPA2;
            else if (authStr.Contains("WPA")) ap.Authentication = AuthenticationType.WPA;
            else if (authStr.Contains("WEP")) ap.Encryption = EncryptionType.WEP;
            else if (authStr == "[ESS]") { ap.Encryption = EncryptionType.None; ap.Authentication = AuthenticationType.Open; }

            return ap;
        }
        catch { return null; }
    }

    private string TrimNs1String(string s)
    {
        // ( SSID ) -> SSID
        s = s.Trim();
        if (s.StartsWith("(") && s.EndsWith(")"))
            return s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    private double? ParseNs1Gps(string s)
    {
        // Simple parse for now
        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        // If N 40 12.34 format, return null or implement dms parser
        return null; 
    }

    private int ParseInt(string s) => int.TryParse(s, out var i) ? i : 0;
    private double? ParseDouble(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private string[] SplitCsvLine(string line)
    {
        // Quick valid-enough CSV split for MVP
        // Splits by comma, but not commas inside quotes
        var result = new List<string>();
        var current = "";
        bool inQuotes = false;
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(UnescapeCsv(current));
                current = "";
            }
            else
            {
                current += c;
            }
        }
        result.Add(UnescapeCsv(current));
        return result.ToArray();
    }

    private string UnescapeCsv(string field)
    {
        field = field.Trim();
        if (field.StartsWith("\"") && field.EndsWith("\""))
        {
            field = field.Substring(1, field.Length - 2);
            field = field.Replace("\"\"", "\"");
        }
        return field;
    }
}
