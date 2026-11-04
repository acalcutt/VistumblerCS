using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                 
                 // NetStumbler keeps signal and noise in dBm, not %: each reading's RSSI, or an estimate from its
                 // percentage when there's none, as the original's _ExportNS1Binary does
                 var dbm = ap.SignalHistory.Select(h => h.Rssi != 0 ? h.Rssi : h.Signal / 2 - 100).ToList();
                 int maxSignal = dbm.Count > 0 ? dbm.Max() : ap.HighestRssi ?? -100;
                 int minSignal = dbm.Count > 0 ? dbm.Min() : ap.Rssi ?? -100;
                 writer.Write(maxSignal);         // MaxSignal
                 writer.Write(-150);              // MinNoise
                 writer.Write(maxSignal + 100);   // MaxSNR
                 
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
                 for (int i = 0; i < ap.SignalHistory.Count; i++)
                 {
                     var hist = ap.SignalHistory[i];
                     writer.Write(ToFileTimeSafe(hist.Timestamp));
                     writer.Write(dbm[i]);       // Signal
                     writer.Write((int)-100);    // Noise
                     
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
                 writer.Write(minSignal);    // MinSignal
                 writer.Write((int)-100);    // MaxNoise
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

    public async Task ExportToKismetDbAsync(string filePath, List<AccessPoint> accessPoints)
    {
        await Task.Run(() =>
        {
            if (File.Exists(filePath)) File.Delete(filePath);

            var connectionString = $"Data Source={filePath};";
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();

            var schemaStatements = new[]
            {
                "CREATE TABLE IF NOT EXISTS KISMET (kismet_version TEXT, db_version INTEGER, db_module TEXT)",
                "INSERT OR REPLACE INTO KISMET (kismet_version, db_version, db_module) VALUES ('2023-07', 10, 'kismetlog')",
                "CREATE TABLE IF NOT EXISTS alerts (ts_sec INTEGER, ts_usec INTEGER, phyname TEXT, devmac TEXT, lat REAL, lon REAL, header TEXT, json BLOB)",
                "CREATE TABLE IF NOT EXISTS data (ts_sec INTEGER, ts_usec INTEGER, phyname TEXT, devmac TEXT, lat REAL, lon REAL, alt REAL, speed REAL, heading REAL, datasource TEXT, type TEXT, json BLOB, signal INTEGER)",
                "CREATE TABLE IF NOT EXISTS datasources (uuid TEXT, typestring TEXT, definition TEXT, name TEXT, interface TEXT, json BLOB, UNIQUE(uuid) ON CONFLICT REPLACE)",
                "INSERT OR REPLACE INTO datasources (uuid, typestring, definition, name, interface, json) VALUES ('00000000-0000-0000-0000-000000000000', 'vistumbler', 'vistumbler', 'vistumbler', 'vistumbler', '{}')",
                "CREATE TABLE IF NOT EXISTS devices (first_time INTEGER, last_time INTEGER, devkey TEXT, phyname TEXT, devmac TEXT, strongest_signal INTEGER, min_lat REAL, min_lon REAL, max_lat REAL, max_lon REAL, avg_lat REAL, avg_lon REAL, bytes_data INTEGER, type TEXT, device BLOB, UNIQUE(phyname, devmac) ON CONFLICT REPLACE)",
                "CREATE INDEX IF NOT EXISTS idx_devices_devkey ON devices(devkey)",
                "CREATE INDEX IF NOT EXISTS idx_devices_devmac ON devices(devmac)",
                "CREATE TABLE IF NOT EXISTS messages (ts_sec INTEGER, lat REAL, lon REAL, alt REAL, speed REAL, heading REAL, msgtype TEXT, message TEXT)",
                "CREATE TABLE IF NOT EXISTS packets (ts_sec INTEGER, ts_usec INTEGER, phyname TEXT, sourcemac TEXT, destmac TEXT, transmac TEXT, frequency REAL, devkey TEXT, lat REAL, lon REAL, alt REAL, speed REAL, heading REAL, packet_len INTEGER, signal INTEGER, datasource TEXT, dlt INTEGER, packet BLOB, error INTEGER, tags TEXT, datarate REAL, hash INTEGER, packetid INTEGER, packet_full_len INTEGER)",
                "CREATE INDEX IF NOT EXISTS idx_packets_sourcemac ON packets(sourcemac)",
                "CREATE TABLE IF NOT EXISTS snapshots (ts_sec INTEGER, ts_usec INTEGER, lat REAL, lon REAL, snaptype TEXT, json TEXT)",
            };
            using (var cmd = connection.CreateCommand())
            {
                foreach (var sql in schemaStatements)
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }

            int packetId = 1;
            foreach (var ap in accessPoints)
            {
                // Prepare Data
                var deviceJson = GenerateKismetDeviceJson(ap);
                string jsonString = deviceJson.ToJsonString();
                
                long firstTime = ToUnixTime(ap.FirstSeen);
                long lastTime = ToUnixTime(ap.LastSeen);
                string devKey = ap.Bssid ?? "";
                string phyName = "IEEE802.11";
                string devMac = ap.Bssid ?? "";
                int strongestSignal = ap.HighestRssi ?? ap.Rssi ?? -100;
                
                double lat = ap.Latitude ?? 0;
                double lon = ap.Longitude ?? 0;

                string type = "Wi-Fi AP";
                if (ap.NetworkType == NetworkType.Adhoc) type = "Wi-Fi Ad-Hoc";
                // If it's a client/probe, logic differs, but AccessPoint usually implies AP.

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO devices (first_time, last_time, devkey, phyname, devmac, strongest_signal, min_lat, min_lon, max_lat, max_lon, avg_lat, avg_lon, bytes_data, type, device)
                        VALUES (@first_time, @last_time, @devkey, @phyname, @devmac, @strongest_signal, @lat, @lon, @lat, @lon, @lat, @lon, 0, @type, @device);
                    ";
                    cmd.Parameters.AddWithValue("@first_time", firstTime);
                    cmd.Parameters.AddWithValue("@last_time", lastTime);
                    cmd.Parameters.AddWithValue("@devkey", devKey);
                    cmd.Parameters.AddWithValue("@phyname", phyName);
                    cmd.Parameters.AddWithValue("@devmac", devMac);
                    cmd.Parameters.AddWithValue("@strongest_signal", strongestSignal);
                    cmd.Parameters.AddWithValue("@lat", lat);
                    cmd.Parameters.AddWithValue("@lon", lon);
                    cmd.Parameters.AddWithValue("@type", type);
                    cmd.Parameters.AddWithValue("@device", jsonString);
                    cmd.ExecuteNonQuery();
                }

                // Packets from History
                if (ap.SignalHistory != null)
                {
                    foreach (var hist in ap.SignalHistory)
                    {
                        // Signal/RSSI logic: Kismet packets expect dBm in 'signal' column.
                        // Vistumbler Hist table: Signal (%), RSSI (dBm).
                        // Legacy: Stores RSSI in 'signal' column. If RSSI=0 and Signal>0, calc RSSI = (Signal/2)-100.
                        int signalDbm = hist.Rssi; // Assuming Rssi property holds dBm
                        if (signalDbm == 0 && hist.Signal > 0)
                        {
                            // Fallback if Rssi is missing but Signal % is present
                             signalDbm = (hist.Signal / 2) - 100; 
                        }

                        // Tags for Vistumbler Signal %
                        // Legacy: "VISTUMBLER_SIG=" & $hSignal
                        string tags = $"VISTUMBLER_SIG={hist.Signal}";

                        long pktTime = ToUnixTime(hist.Timestamp);
                        double pktLat = hist.GpsData?.Latitude ?? 0;
                        double pktLon = hist.GpsData?.Longitude ?? 0;
                        double pktAlt = hist.GpsData?.Altitude ?? 0;
                        double pktSpeed = (hist.GpsData?.SpeedKnots ?? 0) * 1.852;
                        
                        double freq = GetFreqFromChannel(ap.Channel) / 1000.0; // MHz to GHz?? Wait.
                        // Legacy: $aGrpFreqs[$g] (kHz) / 1000 => MHz?
                        // Legacy: $iFreqKhz = $fFreq * 1000. 
                        // packet frequency is REAL. 
                        // In legacy AddPacket: uses $frequency directly.
                        // C# GetFreqFromChannel returns MHz (int). 
                        // Kismet usually expects MHz (e.g. 2412.0) or KHz?
                        // Legacy `_KismetDB_GenerateRadiotapBeacon` takes frequency.
                        // Let's use MHz.

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = @"
                                INSERT INTO packets (ts_sec, ts_usec, phyname, sourcemac, destmac, transmac, frequency, devkey, lat, lon, alt, speed, heading, packet_len, signal, datasource, dlt, packet, error, tags, datarate, hash, packetid, packet_full_len)
                                VALUES (@ts, 0, 'IEEE802.11', @src, 'FF:FF:FF:FF:FF:FF', @src, @freq, '', @lat, @lon, @alt, @speed, 0, 0, @sig, 'vistumbler', 127, X'', 0, @tags, 0, 0, @pid, 0);
                            ";
                            cmd.Parameters.AddWithValue("@ts", pktTime);
                            cmd.Parameters.AddWithValue("@src", devMac);
                            cmd.Parameters.AddWithValue("@freq", GetFreqFromChannel(ap.Channel));
                            cmd.Parameters.AddWithValue("@lat", pktLat);
                            cmd.Parameters.AddWithValue("@lon", pktLon);
                            cmd.Parameters.AddWithValue("@alt", pktAlt);
                            cmd.Parameters.AddWithValue("@speed", pktSpeed);
                            cmd.Parameters.AddWithValue("@sig", signalDbm);
                            cmd.Parameters.AddWithValue("@tags", tags);
                            cmd.Parameters.AddWithValue("@pid", packetId++);
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }

            transaction.Commit();
            connection.Close();
        });
    }

    private JsonObject GenerateKismetDeviceJson(AccessPoint ap)
    {
        // Construct the complicated Kismet Device JSOn
        // We need: dot11.device -> advertised_ssid_map
        
        // Checksum for SSID Map Key (Legacy uses simple hash or xxhash)
        // We'll use a simple arbitrary string or just the SSID itself if unique enough, 
        // but Kismet usually expects a hash key.
        // Legacy: BitXOR loop.
        string ssid = ap.Ssid ?? "";
        int ssidCsum = 0;
        foreach (char c in ssid)
        {
            ssidCsum = (ssidCsum * 31) ^ (int)c;
        }
        ssidCsum = Math.Abs(ssidCsum);
        string ssidKey = ssidCsum.ToString();

        // Crypt Set Calculation (Simulated)
        int cryptSet = 0; // Populate properly if possible
        if (ap.Encryption != EncryptionType.None) cryptSet = 2; // Rough guess

        var ssidRecord = new JsonObject
        {
            ["dot11.advertisedssid.ssid"] = ssid,
            ["dot11.advertisedssid.ssidlen"] = ssid.Length,
            ["dot11.advertisedssid.ssid_len"] = ssid.Length,
            ["dot11.advertisedssid.length"] = ssid.Length,
            ["dot11.advertisedssid.crypt_set"] = cryptSet,
            ["dot11.advertisedssid.crypt_bitfield"] = cryptSet,
            ["dot11.advertisedssid.channel"] = ap.Channel.ToString(),
            ["dot11.advertisedssid.beacon_info"] = "",
            ["dot11.advertisedssid.first_time"] = 0,
            ["dot11.advertisedssid.last_time"] = 0
        };

        var ssidMap = new JsonObject
        {
            [ssidKey] = ssidRecord
        };

        var dot11 = new JsonObject
        {
            ["dot11.device.last_beaconed_ssid"] = ssid,
            ["dot11.device.last_beaconed_ssid_record"] = ssidRecord,
            ["dot11.device.last_beaconed_ssid_checksum"] = ssidCsum,
            ["dot11.device.num_advertised_ssids"] = 1,
            ["dot11.device.advertised_ssid_map"] = ssidMap
        };

        int freqKhz = GetFreqFromChannel(ap.Channel) * 1000;
        var freqMap = new JsonObject
        {
            [freqKhz.ToString()] = 1
        };

        var device = new JsonObject
        {
            ["kismet.device.base.key"] = ap.Bssid ?? "",
            ["kismet.device.base.macaddr"] = ap.Bssid ?? "",
            ["kismet.device.base.name"] = ssid,
            ["kismet.device.base.phyname"] = "IEEE802.11",
            ["kismet.device.base.manuf"] = ap.Manufacturer ?? "Unknown",
            ["kismet.device.base.channel"] = ap.Channel.ToString(),
            ["kismet.device.base.frequency"] = freqKhz,
            ["kismet.device.base.freq_khz_map"] = freqMap,
            ["kismet.device.base.crypt_string"] = ap.Encryption.ToString(),
            ["kismet.device.base.type"] = ap.NetworkType == NetworkType.Adhoc ? "Wi-Fi Ad-Hoc" : "Wi-Fi AP",
            ["kismet.device.base.commonname"] = ssid,
            ["dot11.device"] = dot11,
            // Custom Vistumbler Fields
            ["vistumbler.device.radio_type"] = ap.RadioType ?? "", // Ensure RadioType is on AccessPoint model
            ["vistumbler.device.signal_quality"] = ap.Signal ?? 0 // Saving Signal % in JSON as well
        };

        return device;
    }

    private long ToUnixTime(DateTime date)
    {
        return new DateTimeOffset(date).ToUnixTimeSeconds();
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


    public async Task ExportToNetXmlAsync(string filePath, List<AccessPoint> accessPoints)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = Encoding.UTF8, // Switching to UTF8 for better compatibility in modern tools
            Async = true
        };

        using var writer = XmlWriter.Create(filePath, settings);
        
        await writer.WriteStartDocumentAsync();
        await writer.WriteDocTypeAsync("detection-run", "SYSTEM", "http://kismetwireless.net/kismet-3.1.0.dtd", null);
        
        await writer.WriteStartElementAsync(null, "detection-run", null);
        await writer.WriteAttributeStringAsync(null, "kismet-version", null, "Vistumbler");
        await writer.WriteAttributeStringAsync(null, "start-time", null, FormatKismetDate(DateTime.Now));
        
        await writer.WriteStartElementAsync(null, "card-source", null);
        await writer.WriteAttributeStringAsync(null, "uuid", null, "00000000-0000-0000-0000-000000000000");
        await writer.WriteStringAsync("vistumbler");
        await writer.WriteEndElementAsync(); // card-source

        foreach (var ap in accessPoints)
        {
             await WriteNetXmlNetworkAsync(writer, ap);
        }

        await writer.WriteEndElementAsync(); // detection-run
        await writer.WriteEndDocumentAsync();
    }

    private async Task WriteNetXmlNetworkAsync(XmlWriter writer, AccessPoint ap)
    {
        // Calculate Stats
        int minRssi = ap.Rssi ?? -100, maxRssi = ap.HighestRssi ?? ap.Rssi ?? -100, lastRssi = ap.Rssi ?? -100;
        
        if (ap.SignalHistory != null && ap.SignalHistory.Count > 0)
        {
             // Try to use History if populated with RSSI (Noise is usually 0 in Vistumbler)
             // NOTE: Vistumbler Hist table stores Signal (%) and RSSI (dBm). We want dBm.
             // If History is loaded...
             // Check if we have RSSI values in history?
             // AccessPoint model has SignalHistory with Signal (int). Is it % or dBm?
             // ImportService: hist.Signal = reader.ReadInt32(); (VS1 has Signal % usually, RSSI is separate or derived)
             // Let's assume SignalHistory.Signal is %. We might need RSSI derived.
             // But if we have RSSI on AP, use that for now to avoid complexity without direct history RSSI field.
             // Actually, the AP usually has the aggregates.
        }

        string type = ap.NetworkType == NetworkType.Adhoc ? "ad-hoc" : "infrastructure";
        string startTime = FormatKismetDate(ap.FirstSeen);
        string endTime = FormatKismetDate(ap.LastSeen);
        
        await writer.WriteStartElementAsync(null, "wireless-network", null);
        await writer.WriteAttributeStringAsync(null, "number", null, "0");
        await writer.WriteAttributeStringAsync(null, "type", null, type);
        await writer.WriteAttributeStringAsync(null, "first-time", null, startTime);
        await writer.WriteAttributeStringAsync(null, "last-time", null, endTime);
        
        // SSID
        await writer.WriteStartElementAsync(null, "SSID", null);
        await writer.WriteAttributeStringAsync(null, "first-time", null, startTime);
        await writer.WriteAttributeStringAsync(null, "last-time", null, endTime);
        
        await writer.WriteElementStringAsync(null, "type", null, "Beacon"); 
        await writer.WriteElementStringAsync(null, "max-rate", null, "54.0");
        await writer.WriteElementStringAsync(null, "packets", null, "0");
        await writer.WriteElementStringAsync(null, "beaconrate", null, "10");
        
        string encStr = ap.Encryption.ToString(); // Improve mapping if needed
        if (ap.Encryption == EncryptionType.None) encStr = "None";
        else if (ap.Encryption == EncryptionType.WEP) encStr = "WEP";
        else if (ap.Authentication.ToString().Contains("WPA")) encStr = ap.Authentication.ToString().Replace("_", "+"); 
        
        await writer.WriteElementStringAsync(null, "encryption", null, encStr);
        
        await writer.WriteStartElementAsync(null, "essid", null);
        await writer.WriteAttributeStringAsync(null, "cloaked", null, string.IsNullOrEmpty(ap.Ssid) ? "true" : "false");
        await writer.WriteStringAsync(ap.Ssid);
        await writer.WriteEndElementAsync(); // essid
        
        await writer.WriteEndElementAsync(); // SSID
        
        await writer.WriteElementStringAsync(null, "BSSID", null, ap.Bssid);
        await writer.WriteElementStringAsync(null, "manuf", null, ap.Manufacturer);
        await writer.WriteElementStringAsync(null, "channel", null, ap.Channel.ToString());
        await writer.WriteElementStringAsync(null, "freqmhz", null, $"{GetFreqFromChannel(ap.Channel)} 0");
        await writer.WriteElementStringAsync(null, "maxseenrate", null, "54.0");
        
        // SNR
        await writer.WriteStartElementAsync(null, "snr-info", null);
        await writer.WriteElementStringAsync(null, "last_signal_dbm", null, lastRssi.ToString());
        await writer.WriteElementStringAsync(null, "last_noise_dbm", null, "0");
        await writer.WriteElementStringAsync(null, "last_signal_rssi", null, lastRssi.ToString());
        await writer.WriteElementStringAsync(null, "last_noise_rssi", null, "0");
        await writer.WriteElementStringAsync(null, "min_signal_dbm", null, minRssi.ToString());
        await writer.WriteElementStringAsync(null, "min_noise_dbm", null, "0");
        await writer.WriteElementStringAsync(null, "min_signal_rssi", null, minRssi.ToString());
        await writer.WriteElementStringAsync(null, "min_noise_rssi", null, "0");
        await writer.WriteElementStringAsync(null, "max_signal_dbm", null, maxRssi.ToString());
        await writer.WriteElementStringAsync(null, "max_noise_dbm", null, "0");
        await writer.WriteElementStringAsync(null, "max_signal_rssi", null, maxRssi.ToString());
        await writer.WriteElementStringAsync(null, "max_noise_rssi", null, "0");
        await writer.WriteEndElementAsync(); // snr-info
        
        // Vistumbler Custom Attributes for Radio Type and Signal Percent
        // These are not standard Kismet but used by Vistumbler for extended data preservation
        if (!string.IsNullOrEmpty(ap.RadioType))
        {
            await writer.WriteStartElementAsync(null, "bsstype", null);
            await writer.WriteStringAsync(ap.RadioType);
            await writer.WriteEndElementAsync();
        }

        if (ap.Signal.HasValue)
        {
             await writer.WriteStartElementAsync(null, "signal_quality", null);
             await writer.WriteStringAsync(ap.Signal.Value.ToString());
             await writer.WriteEndElementAsync();
        }

        // GPS
        if (ap.Latitude.HasValue || ap.Longitude.HasValue)
        {
            double lat = ap.Latitude ?? 0;
            double lon = ap.Longitude ?? 0;
            await writer.WriteStartElementAsync(null, "gps-info", null);
            await writer.WriteElementStringAsync(null, "min-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "min-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "min-alt", null, "0");
            await writer.WriteElementStringAsync(null, "min-spd", null, "0");
             await writer.WriteElementStringAsync(null, "max-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "max-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "max-alt", null, "0");
            await writer.WriteElementStringAsync(null, "max-spd", null, "0");
             await writer.WriteElementStringAsync(null, "peak-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "peak-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "peak-alt", null, "0");
            await writer.WriteElementStringAsync(null, "peak-spd", null, "0");
             await writer.WriteElementStringAsync(null, "avg-lat", null, lat.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "avg-lon", null, lon.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await writer.WriteElementStringAsync(null, "avg-alt", null, "0");
            await writer.WriteElementStringAsync(null, "avg-spd", null, "0");
            await writer.WriteEndElementAsync(); // gps-info
        }

        await writer.WriteElementStringAsync(null, "datasize", null, "0");
        await writer.WriteEndElementAsync(); // wireless-network
    }

    private string FormatKismetDate(DateTime dt)
    {
         return dt.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }
    
    private int GetFreqFromChannel(int channel)
    {
        if (channel >= 1 && channel <= 13) return 2407 + (channel * 5);
        if (channel == 14) return 2484;
        if (channel >= 36 && channel <= 177) return 5000 + (channel * 5);
        return 0;
    }

    /// <summary>
    /// KML for Google Earth: a placemark per AP with a position, and, with <see cref="ExportOptions.ShowTrack"/>, the GPS
    /// track as the original Vistumbler draws it (a line in its "Location" style, broken where the fixes are more than
    /// 3 minutes apart).
    /// </summary>
    public async Task ExportToKmlAsync(string filePath, List<AccessPoint> accessPoints, ExportOptions options, IReadOnlyList<GpsData>? gpsFixes = null)
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

        if (options.ShowTrack && gpsFixes is { Count: > 0 })
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            await writer.WriteStartElementAsync(null, "Style", null);
            await writer.WriteAttributeStringAsync(null, "id", null, "Location");
            await writer.WriteStartElementAsync(null, "LineStyle", null);
            await writer.WriteElementStringAsync(null, "color", null, options.TrackColor);
            await writer.WriteElementStringAsync(null, "width", null, "4");
            await writer.WriteEndElementAsync(); // LineStyle
            await writer.WriteEndElementAsync(); // Style

            await writer.WriteStartElementAsync(null, "Folder", null);
            await writer.WriteElementStringAsync(null, "name", null, "GPS Track");
            foreach (var segment in TrackSegments(gpsFixes).Where(s => s.Count >= 2))
            {
                await writer.WriteStartElementAsync(null, "Placemark", null);
                await writer.WriteElementStringAsync(null, "name", null, "GPS Track");
                await writer.WriteElementStringAsync(null, "styleUrl", null, "#Location");
                await writer.WriteStartElementAsync(null, "LineString", null);
                await writer.WriteElementStringAsync(null, "extrude", null, "1");
                await writer.WriteElementStringAsync(null, "tessellate", null, "1");
                await writer.WriteElementStringAsync(null, "coordinates", null,
                    string.Join("\n", segment.Select(g => $"{g.Longitude.ToString(inv)},{g.Latitude.ToString(inv)},0")));
                await writer.WriteEndElementAsync(); // LineString
                await writer.WriteEndElementAsync(); // Placemark
            }
            await writer.WriteEndElementAsync(); // Folder
        }

        await writer.WriteEndElementAsync(); // Document
        await writer.WriteEndElementAsync(); // kml
        await writer.WriteEndDocumentAsync();
    }

    /// <summary>The GPS fixes in time order, split where they're more than 3 minutes apart (a stop, another day).</summary>
    private static List<List<GpsData>> TrackSegments(IReadOnlyList<GpsData> fixes)
    {
        var segments = new List<List<GpsData>>();
        List<GpsData>? current = null;
        DateTime prev = default;
        foreach (var g in fixes.Where(g => g.Latitude != 0 || g.Longitude != 0).OrderBy(g => g.Timestamp))
        {
            if (current is null || (g.Timestamp - prev).TotalSeconds > 180)
                segments.Add(current = new List<GpsData>());
            current.Add(g);
            prev = g.Timestamp;
        }
        return segments;
    }

    /// <summary>GPX: a waypoint per AP with a position, and the GPS track, a segment per stretch of fixes.</summary>
    public async Task ExportToGpxAsync(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<GpsData>? gpsFixes = null)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
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
        await writer.WriteAttributeStringAsync(null, "creator", null, "VistumblerCS");
        
        foreach (var ap in accessPoints.Where(a => a.Latitude.HasValue && a.Longitude.HasValue))
        {
            await writer.WriteStartElementAsync(null, "wpt", null);
            await writer.WriteAttributeStringAsync(null, "lat", null, ap.Latitude!.Value.ToString("F6", inv));
            await writer.WriteAttributeStringAsync(null, "lon", null, ap.Longitude!.Value.ToString("F6", inv));

            await writer.WriteElementStringAsync(null, "name", null, XmlText(ap.Ssid));
            await writer.WriteElementStringAsync(null, "desc", null,
                $"BSSID: {ap.Bssid}, Signal: {ap.HighestSignal ?? ap.Signal}%, Channel: {ap.Channel}");

            await writer.WriteEndElementAsync(); // wpt
        }

        if (gpsFixes is { Count: > 0 })
        {
            await writer.WriteStartElementAsync(null, "trk", null);
            await writer.WriteElementStringAsync(null, "name", null, "GPS Track");
            foreach (var segment in TrackSegments(gpsFixes))
            {
                await writer.WriteStartElementAsync(null, "trkseg", null);
                foreach (var g in segment)
                {
                    await writer.WriteStartElementAsync(null, "trkpt", null);
                    await writer.WriteAttributeStringAsync(null, "lat", null, g.Latitude.ToString("F7", inv));
                    await writer.WriteAttributeStringAsync(null, "lon", null, g.Longitude.ToString("F7", inv));
                    if (g.Altitude is { } alt) await writer.WriteElementStringAsync(null, "ele", null, alt.ToString("0.0", inv));
                    await writer.WriteElementStringAsync(null, "time", null, Utc(g.Timestamp).ToString("yyyy-MM-ddTHH:mm:ssZ", inv));
                    if (g.NumberOfSatellites > 0) await writer.WriteElementStringAsync(null, "sat", null, g.NumberOfSatellites.ToString(inv));
                    if (g.HorizontalDilution is { } hdop) await writer.WriteElementStringAsync(null, "hdop", null, hdop.ToString(inv));
                    await writer.WriteEndElementAsync(); // trkpt
                }
                await writer.WriteEndElementAsync(); // trkseg
            }
            await writer.WriteEndElementAsync(); // trk
        }

        await writer.WriteEndElementAsync(); // gpx
        await writer.WriteEndDocumentAsync();
    }

    /// <summary>
    /// Vistumbler's CSV, as the original's _ExportToCSV writes it (and its _ImportCSV, VistumblerMAUI and WifiDB read
    /// it). Detailed: 26 columns, a row per reading (<see cref="ExportLoader.LoadHistoryAsync"/>) with its GPS fix.
    /// Summary: 18 columns, a row per AP at its strongest position, with its first and last seen.
    /// </summary>
    public async Task ExportToCsvAsync(string filePath, List<AccessPoint> accessPoints, bool detailed = true)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));

        // Quoted text, with no quotes inside, as the original writes it
        static string Q(string? s) => "\"" + (s ?? string.Empty).Replace("\"", "") + "\"";
        string D(double? d) => d?.ToString(inv) ?? "0";

        if (detailed)
        {
            await writer.WriteLineAsync(
                "SSID,BSSID,MANUFACTURER,SIGNAL,High Signal,RSSI,High RSSI,AUTHENTICATION,ENCRYPTION," +
                "RADIO TYPE,CHANNEL,BTX,OTX,NETWORK TYPE,LABEL,LATITUDE,LONGITUDE,SATELLITES,HDOP," +
                "ALTITUDE,HEIGHT OF GEOID,SPEED(km/h),SPEED(MPH),TRACK ANGLE,DATE(UTC),TIME(UTC)");
            foreach (var ap in accessPoints)
            {
                foreach (var h in ap.SignalHistory.Where(h => h.Signal != 0).OrderBy(h => h.Timestamp))
                {
                    var g = h.GpsData;
                    var ts = Utc(g?.Timestamp ?? h.Timestamp);
                    await writer.WriteLineAsync(string.Join(",",
                        Q(ap.Ssid), ap.Bssid, Q(ap.Manufacturer),
                        h.Signal, ap.HighestSignal ?? 0, h.Rssi, ap.HighestRssi ?? 0,
                        ap.Authentication.ToLegacyString(), ap.Encryption.ToLegacyString(), ap.RadioType, ap.Channel,
                        Q(ap.BasicTransferRates), Q(ap.OtherTransferRates), ap.NetworkType.ToLegacyString(), Q(ap.Label),
                        D(g?.Latitude), D(g?.Longitude),
                        g?.NumberOfSatellites ?? 0, D(g?.HorizontalDilution), D(g?.Altitude), "0",
                        (g?.SpeedKmh ?? 0).ToString("0.0", inv), (g?.SpeedMph ?? 0).ToString("0.0", inv), D(g?.TrackAngle),
                        ts.ToString("yyyy-MM-dd", inv), ts.ToString("HH:mm:ss.fff", inv)));
                }
            }
            return;
        }

        await writer.WriteLineAsync(
            "SSID,BSSID,MANUFACTURER,HIGHEST SIGNAL W/GPS,AUTHENTICATION,ENCRYPTION,RADIO TYPE,CHANNEL,LATITUDE,LONGITUDE," +
            "BTX,OTX,FIRST SEEN(UTC),LAST SEEN(UTC),NETWORK TYPE,LABEL, HIGHEST SIGNAL, HIGHEST RSSI");
        foreach (var ap in accessPoints)
        {
            // the strongest reading with a position, or the AP's own position without readings
            var best = ap.SignalHistory.Where(h => h.GpsData is not null).MaxBy(h => h.Signal);
            double? lat = best?.GpsData!.Latitude ?? ap.Latitude, lon = best?.GpsData!.Longitude ?? ap.Longitude;
            await writer.WriteLineAsync(string.Join(",",
                Q(ap.Ssid), ap.Bssid, Q(ap.Manufacturer), best?.Signal ?? 0,
                ap.Authentication.ToLegacyString(), ap.Encryption.ToLegacyString(), ap.RadioType, ap.Channel,
                D(lat), D(lon), Q(ap.BasicTransferRates), Q(ap.OtherTransferRates),
                Utc(ap.FirstSeen).ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
                Utc(ap.LastSeen).ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
                ap.NetworkType.ToLegacyString(), Q(ap.Label), ap.HighestSignal ?? 0, ap.HighestRssi ?? 0));
        }
    }

    /// <summary>
    /// WiGLE's CSV, version 1.6 (https://api.wigle.net/csvFormat.html): 14 columns, a row per reading with a position
    /// (its <see cref="SignalHistory.GpsData"/>, <see cref="ExportLoader.LoadHistoryAsync"/>), or one at the best
    /// position for an AP without readings. Cell towers and Bluetooth devices follow as WiGLE WiFi Wardriving writes
    /// them: the key as MAC, the name as SSID, the capabilities as AuthMode, MfgrId for BLE, and their Type.
    /// </summary>
    public async Task ExportToWigleCsvAsync(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<RadioNetwork>? radios = null)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(false));

        await writer.WriteLineAsync("WigleWifi-1.6,appRelease=VistumblerCS,model=PC,release=1.0,device=PC,display=,board=,brand=,star=Sol,body=3,subBody=0");
        await writer.WriteLineAsync("MAC,SSID,AuthMode,FirstSeen,Channel,Frequency,RSSI,CurrentLatitude,CurrentLongitude,AltitudeMeters,AccuracyMeters,RCOIs,MfgrId,Type");
        // WiGLE's rows aren't quoted: commas and line breaks come out of the text
        static string Clean(string? s) => (s ?? string.Empty).Replace(",", "").Replace("\r", "").Replace("\n", "");

        foreach (var ap in accessPoints)
        {
            string head = $"{ap.Bssid.ToLowerInvariant()},{Clean(ap.Ssid)},{BuildWigleAuthMode(ap.Authentication, ap.Encryption, ap.NetworkType)}";
            int freq = GetFreqFromChannel(ap.Channel);
            string Row(DateTime when, int? rssi, double lat, double lon, double? alt) =>
                $"{head},{when.ToString("yyyy-MM-dd HH:mm:ss", inv)},{ap.Channel},{freq},{rssi?.ToString(inv) ?? ""}," +
                $"{lat.ToString(inv)},{lon.ToString(inv)},{(alt ?? 0).ToString(inv)},0,,,WIFI";

            var readings = ap.SignalHistory.Where(h => h.GpsData is not null).ToList();
            if (readings.Count > 0)
                foreach (var h in readings)
                    await writer.WriteLineAsync(Row(h.Timestamp, h.Rssi, h.GpsData!.Latitude, h.GpsData.Longitude, h.GpsData.Altitude));
            else if (ap.Latitude is { } lat && ap.Longitude is { } lon)
                await writer.WriteLineAsync(Row(ap.FirstSeen, ap.Rssi, lat, lon, null));
        }

        foreach (var n in radios ?? Array.Empty<RadioNetwork>())
        {
            string head = $"{n.Key},{Clean(n.Name)},{Clean(n.Capabilities)}";
            string channel = n.Channel is { } c ? c.ToString(inv) : "";
            string freq = n.Frequency != 0 ? n.Frequency.ToString(inv) : "";
            string mfgr = n.MfgrId is { } m and not 0 ? m.ToString(inv) : "";
            string Row(DateTime when, int rssi, double lat, double lon, double? alt) =>
                $"{head},{when.ToString("yyyy-MM-dd HH:mm:ss", inv)},{channel},{freq},{rssi.ToString(inv)}," +
                $"{lat.ToString(inv)},{lon.ToString(inv)},{(alt ?? 0).ToString(inv)},0,,{mfgr},{n.Type}";

            var readings = n.History.Where(r => r.GpsData is not null).ToList();
            if (readings.Count > 0)
                foreach (var r in readings)
                    await writer.WriteLineAsync(Row(r.Timestamp, r.Rssi, r.GpsData!.Latitude, r.GpsData.Longitude, r.GpsData.Altitude));
            else if (n.Latitude is { } lat && n.Longitude is { } lon)
                await writer.WriteLineAsync(Row(n.FirstSeen, n.HighestRssi == int.MinValue ? n.Rssi : n.HighestRssi, lat, lon, null));
        }
    }

    /// <summary>
    /// Converts Vistumbler Authentication/Encryption/NetworkType to WiGLE AuthMode capability flags.
    /// Port of _WigleCSV_BuildAuthMode from WigleCSV.au3.
    /// </summary>
    private string BuildWigleAuthMode(AuthenticationType auth, EncryptionType encr, NetworkType netType)
    {
        // Normalize encryption: GCMP-256 → GCMP, CCMP-256 → CCMP
        string encrNorm;
        if (encr == EncryptionType.GCMP || encr == EncryptionType.GCMP_256)
            encrNorm = "GCMP";
        else if (encr == EncryptionType.CCMP || encr == EncryptionType.CCMP_256 || encr == EncryptionType.AES)
            encrNorm = "CCMP";
        else
            encrNorm = encr.ToLegacyString();

        string flags;
        if (auth == AuthenticationType.WPA3_Enterprise || auth == AuthenticationType.WPA3_Enterprise_192)
            flags = $"[WPA3-EAP-{encrNorm}]";
        else if (auth == AuthenticationType.WPA3_PSK || auth == AuthenticationType.WPA3)
            flags = $"[WPA3-SAE-{encrNorm}]";
        else if (auth == AuthenticationType.WPA2_Enterprise)
            flags = $"[WPA2-EAP-{encrNorm}]";
        else if (auth == AuthenticationType.WPA2_PSK || auth == AuthenticationType.WPA2)
            flags = $"[WPA2-PSK-{encrNorm}]";
        else if (auth == AuthenticationType.WPA_Enterprise)
            flags = $"[WPA-EAP-{encrNorm}]";
        else if (auth == AuthenticationType.WPA_PSK || auth == AuthenticationType.WPA)
            flags = $"[WPA-PSK-{encrNorm}]";
        else if (auth == AuthenticationType.OWE)
            flags = "[OWE]";
        else if ((auth == AuthenticationType.Open || auth == AuthenticationType.Shared) && encr == EncryptionType.WEP)
            flags = "[WEP]";
        else if (auth == AuthenticationType.Open && encr == EncryptionType.None)
            return "";
        else
            flags = "";

        if (flags != "")
        {
            flags += netType == NetworkType.Adhoc ? "[IBSS]" : "[ESS]";
        }

        return flags;
    }

    /// <summary>
    /// Vistumbler's VS1, "Detailed Export Version 4.0", as the original writes it (_ExportVS1): the GPS lines (12
    /// fields), then an access point a line (15 fields) whose last field is its readings, "GID,SIGNAL,RSSI" joined by
    /// '\', each GID one of the GPS lines. Readers tell the lines apart by their field count, so a value can't have a
    /// '|' in it. The readings come from each access point's <see cref="AccessPoint.SignalHistory"/>, with their
    /// <see cref="SignalHistory.GpsData"/> (<see cref="ExportLoader.LoadHistoryAsync"/>).
    /// <para>
    /// With cell towers or Bluetooth devices (<see cref="ExportLoader.LoadRadioNetworksAsync"/>) it's version 4.1, as
    /// VistumblerMAUI writes it: a third section of 10-field lines after the APs, "Type|Key|Name|Capabilities|Channel|
    /// Frequency|MfgrId|Manufacturer|High RSSI|GID,RSSI\…", which readers before 4.1 skip. Without any, the file is
    /// exactly 4.0.
    /// </para>
    /// </summary>
    public async Task ExportToVs1Async(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<RadioNetwork>? radios = null)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        const string sep = "# -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------";
        bool hasRadios = radios is { Count: > 0 };

        // The GPS lines: every fix a reading was taken at, numbered as stored
        var gps = new SortedDictionary<int, GpsData>();
        foreach (var h in accessPoints.SelectMany(ap => ap.SignalHistory))
            if (h.GpsData is { } g && g.GpsId > 0) gps[g.GpsId] = g;
        foreach (var r in (radios ?? Array.Empty<RadioNetwork>()).SelectMany(n => n.History))
            if (r.GpsData is { } g && g.GpsId > 0) gps[g.GpsId] = g;

        // CRLF, as Vistumbler writes it and WifiDB's importer reads it
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8) { NewLine = "\r\n" };
        await writer.WriteLineAsync(hasRadios ? "# Vistumbler VS1 - Detailed Export Version 4.1" : "# Vistumbler VS1 - Detailed Export Version 4.0");
        await writer.WriteLineAsync("# Created By: VistumblerCS");
        await writer.WriteLineAsync(sep);
        await writer.WriteLineAsync("# GpsID|Latitude|Longitude|NumOfSatalites|HorizontalDilutionOfPrecision|Altitude(m)|HeightOfGeoidAboveWGS84Ellipsoid(m)|Speed(km/h)|Speed(MPH)|TrackAngle(Deg)|Date(UTC y-m-d)|Time(UTC h:m:s.ms)");
        await writer.WriteLineAsync(sep);
        foreach (var g in gps.Values)
        {
            var utc = g.Timestamp.Kind == DateTimeKind.Local ? g.Timestamp.ToUniversalTime() : g.Timestamp;
            await writer.WriteLineAsync(string.Join('|',
                g.GpsId,
                DecimalToDmm(g.Latitude, isLat: true),
                DecimalToDmm(g.Longitude, isLat: false),
                g.NumberOfSatellites,
                (g.HorizontalDilution ?? 0).ToString("0.0", inv),
                (g.Altitude ?? 0).ToString("0.0", inv),
                "0",
                g.SpeedKmh.ToString("0.0", inv),
                g.SpeedMph.ToString("0.0", inv),
                (g.TrackAngle ?? 0).ToString("0.0", inv),
                utc.ToString("yyyy-MM-dd", inv),
                utc.ToString("HH:mm:ss.fff", inv)));
        }

        await writer.WriteLineAsync(sep);
        await writer.WriteLineAsync("# SSID|BSSID|MANUFACTURER|Authentication|Encryption|Security Type|Radio Type|Channel|Basic Transfer Rates|Other Transfer Rates|High Signal|High RSSI|Network Type|Label|GID,SIGNAL,RSSI");
        await writer.WriteLineAsync(sep);
        foreach (var ap in accessPoints)
        {
            var history = string.Join('\\', ap.SignalHistory
                .Where(h => h.GpsData is { GpsId: > 0 })
                .Select(h => $"{h.GpsData!.GpsId},{h.Signal},{h.Rssi}"));
            await writer.WriteLineAsync(string.Join('|',
                Vs1Field(ap.Ssid),
                Vs1Field(ap.Bssid),
                Vs1Field(ap.Manufacturer),
                Vs1Field(ap.Authentication.ToLegacyString()),
                Vs1Field(ap.Encryption.ToLegacyString()),
                Vs1SecurityType(ap.Encryption),
                Vs1Field(ap.RadioType),
                ap.Channel,
                Vs1Field(ap.BasicTransferRates),
                Vs1Field(ap.OtherTransferRates),
                ap.HighestSignal ?? ap.Signal ?? 0,
                ap.HighestRssi ?? ap.Rssi ?? -100,
                Vs1Field(ap.NetworkType.ToLegacyString()),
                Vs1Field(ap.Label),
                history));
        }

        if (!hasRadios) return;
        await writer.WriteLineAsync(sep);
        await writer.WriteLineAsync("# Type|Key|Name|Capabilities|Channel|Frequency|MfgrId|Manufacturer|High RSSI|GID,RSSI");
        await writer.WriteLineAsync(sep);
        foreach (var n in radios!)
        {
            var readings = string.Join('\\', n.History.Where(r => r.GpsData is { GpsId: > 0 }).Select(r => $"{r.GpsData!.GpsId},{r.Rssi.ToString(inv)}"));
            await writer.WriteLineAsync(string.Join('|',
                n.Type,
                Vs1Field(n.Key),
                Vs1Field(n.Name),
                Vs1Field(n.Capabilities),
                n.Channel?.ToString(inv) ?? "",
                n.Frequency.ToString(inv),
                n.MfgrId?.ToString(inv) ?? "",
                Vs1Field(n.Manufacturer),
                n.HighestRssi == int.MinValue ? n.Rssi : n.HighestRssi,
                readings));
        }
    }

    // Keeps a value to one VS1 field: no '|' or line break, which would change the line's field count
    private static string Vs1Field(string? value)
        => (value ?? string.Empty).Replace('|', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // Vistumbler's security type: 1 open, 2 WEP, 3 anything stronger
    private static int Vs1SecurityType(EncryptionType encryption) => encryption switch
    {
        EncryptionType.None => 1,
        EncryptionType.WEP => 2,
        _ => 3,
    };

    // Decimal degrees as Vistumbler stores them: "N 4807.0380" (hemisphere, degrees and minutes)
    private static string DecimalToDmm(double dd, bool isLat)
    {
        char hemi = isLat ? (dd >= 0 ? 'N' : 'S') : (dd >= 0 ? 'E' : 'W');
        double a = Math.Abs(dd);
        int deg = (int)a;
        double dmm = deg * 100 + (a - deg) * 60.0;
        return $"{hemi} {dmm.ToString("0000.0000", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public async Task ExportToVszAsync(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<RadioNetwork>? radios = null)
    {
        // VSZ is a compressed VS1 file
        var tempVs1 = Path.GetTempFileName();
        try
        {
            await ExportToVs1Async(tempVs1, accessPoints, radios);

            if (File.Exists(filePath))
                File.Delete(filePath);

            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);
            // Always "data.vs1": the original Vistumbler (_ExtractVSZ) and WifiDB's importer open that entry by name
            archive.CreateEntryFromFile(tempVs1, "data.vs1");
        }
        finally
        {
            if (File.Exists(tempVs1))
                File.Delete(tempVs1);
        }
    }

    private List<AccessPoint> FilterAccessPoints(List<AccessPoint> accessPoints, ExportOptions options)
    {
        // Open, WEP or secure, as the original's security type: everything that's not open or WEP is secure (CCMP, GCMP, …)
        return accessPoints.Where(ap => Vs1SecurityType(ap.Encryption) switch
        {
            1 => options.IncludeOpenNetworks,
            2 => options.IncludeWepNetworks,
            _ => options.IncludeSecureNetworks,
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
        await writer.WriteElementStringAsync(null, "name", null, XmlText(string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : ap.Ssid));

        // Description
        var description = $"BSSID: {ap.Bssid}\n" +
                         $"Channel: {ap.Channel}\n" +
                         $"Signal: {ap.HighestSignal ?? ap.Signal}%\n" +
                         $"RSSI: {ap.HighestRssi ?? ap.Rssi} dBm\n" +
                         $"Authentication: {ap.Authentication.ToLegacyString()}\n" +
                         $"Encryption: {ap.Encryption.ToLegacyString()}\n" +
                         $"Manufacturer: {ap.Manufacturer}";

        await writer.WriteElementStringAsync(null, "description", null, XmlText(description));

        // Style by the strongest signal, where the placemark is
        if (options.UseSignalColors)
        {
            var styleId = GetSignalStyleId(ap.HighestSignal ?? ap.Signal ?? 0);
            await writer.WriteElementStringAsync(null, "styleUrl", null, $"#{styleId}");
        }

        // Point coordinates
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        await writer.WriteStartElementAsync(null, "Point", null);
        await writer.WriteElementStringAsync(null, "coordinates", null,
            $"{ap.Longitude!.Value.ToString(inv)},{ap.Latitude!.Value.ToString(inv)},0");
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

    // The stored times are UTC; one still local (a reading taken this session) is converted
    private static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;

    // Text XML can hold: an SSID can have control characters that XmlWriter refuses
    private static string XmlText(string? s)
        => string.IsNullOrEmpty(s) ? string.Empty : new string(s.Where(XmlConvert.IsXmlChar).ToArray());

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
