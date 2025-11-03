using System.Data.OleDb;
using System.Data.SQLite;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Data;

/// <summary>
/// Utility to migrate data from Access MDB to SQLite
/// </summary>
public class MdbToSqliteMigration
{
    private readonly IDatabaseService _sqliteService;

    public MdbToSqliteMigration(IDatabaseService sqliteService)
    {
        _sqliteService = sqliteService;
    }

    public async Task MigrateAsync(string mdbPath, string sqlitePath, IProgress<MigrationProgress>? progress = null)
    {
        if (!File.Exists(mdbPath))
            throw new FileNotFoundException("MDB file not found", mdbPath);

        // Initialize SQLite database
        await _sqliteService.InitializeAsync(sqlitePath);

        var connectionString = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={mdbPath};";
        
        using var mdbConnection = new OleDbConnection(connectionString);
        await mdbConnection.OpenAsync();

        // Migrate in order: AP -> GPS -> HIST
        var totalRecords = await GetTotalRecordsAsync(mdbConnection);
        int processedRecords = 0;

        // Migrate Access Points
        var accessPoints = await MigrateAccessPointsAsync(mdbConnection);
        processedRecords += accessPoints.Count;
        progress?.Report(new MigrationProgress
        {
            Stage = "Access Points",
            Processed = processedRecords,
            Total = totalRecords,
            Message = $"Migrated {accessPoints.Count} access points"
        });

        // Migrate GPS Data
        var gpsDataCount = await MigrateGpsDataAsync(mdbConnection);
        processedRecords += gpsDataCount;
        progress?.Report(new MigrationProgress
        {
            Stage = "GPS Data",
            Processed = processedRecords,
            Total = totalRecords,
            Message = $"Migrated {gpsDataCount} GPS records"
        });

        // Migrate Signal History
        var historyCount = await MigrateSignalHistoryAsync(mdbConnection, accessPoints);
        processedRecords += historyCount;
        progress?.Report(new MigrationProgress
        {
            Stage = "Signal History",
            Processed = processedRecords,
            Total = totalRecords,
            Message = $"Migrated {historyCount} history records"
        });

        progress?.Report(new MigrationProgress
        {
            Stage = "Complete",
            Processed = totalRecords,
            Total = totalRecords,
            Message = "Migration completed successfully"
        });
    }

    private async Task<int> GetTotalRecordsAsync(OleDbConnection connection)
    {
        int total = 0;

        var tables = new[] { "AP", "GPS", "HIST" };
        foreach (var table in tables)
        {
            var sql = $"SELECT COUNT(*) FROM [{table}]";
            var cmd = new OleDbCommand(sql, connection);
            total += Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        return total;
    }

    private async Task<List<AccessPoint>> MigrateAccessPointsAsync(OleDbConnection connection)
    {
        var accessPoints = new List<AccessPoint>();

        var sql = @"
            SELECT ApID, BSSID, SSID, CHAN, AUTH, ENCR, RADTYPE, NETTYPE, 
                   Signal, HighSignal, RSSI, HighRSSI, BTX, OTX, 
                   FirstHistID, LastHistID, FirstDateTime, LastDateTime,
                   Latitude, Longitude, Manu, Label
            FROM AP
        ";

        using var cmd = new OleDbCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var ap = new AccessPoint
            {
                ApId = reader.GetInt32(0),
                Bssid = reader.GetString(1),
                Ssid = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Channel = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Authentication = ParseAuthentication(reader.IsDBNull(4) ? "" : reader.GetString(4)),
                Encryption = ParseEncryption(reader.IsDBNull(5) ? "" : reader.GetString(5)),
                RadioType = reader.IsDBNull(6) ? "" : reader.GetString(6),
                NetworkType = ParseNetworkType(reader.IsDBNull(7) ? "" : reader.GetString(7)),
                Signal = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                HighestSignal = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                Rssi = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                HighestRssi = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                BasicTransferRates = reader.IsDBNull(12) ? "" : reader.GetString(12),
                OtherTransferRates = reader.IsDBNull(13) ? "" : reader.GetString(13),
                FirstSeen = ParseDateTime(reader, 16),
                LastSeen = ParseDateTime(reader, 17),
                Latitude = reader.IsDBNull(18) ? null : reader.GetDouble(18),
                Longitude = reader.IsDBNull(19) ? null : reader.GetDouble(19),
                Manufacturer = reader.IsDBNull(20) ? "" : reader.GetString(20),
                Label = reader.IsDBNull(21) ? "" : reader.GetString(21)
            };

            await _sqliteService.UpsertAccessPointAsync(ap);
            accessPoints.Add(ap);
        }

        return accessPoints;
    }

    private async Task<int> MigrateGpsDataAsync(OleDbConnection connection)
    {
        int count = 0;

        var sql = @"
            SELECT GpsID, Latitude, Longitude, NumOfSats, HorDilPitch, Alt, 
                   SpeedInMPH, SpeedInKmH, TrackAngle, Date1, Time1
            FROM GPS
        ";

        using var cmd = new OleDbCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var gpsData = new GpsData
            {
                GpsId = reader.GetInt32(0),
                Latitude = reader.GetDouble(1),
                Longitude = reader.GetDouble(2),
                NumberOfSatellites = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                HorizontalDilution = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                Altitude = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                SpeedKnots = reader.IsDBNull(6) ? null : reader.GetDouble(6) / 1.15078, // Convert MPH to knots
                TrackAngle = reader.IsDBNull(8) ? null : reader.GetDouble(8),
                Timestamp = ParseDateTime(reader, 9, 10),
                Quality = GpsQuality.GpsFix
            };

            await _sqliteService.AddGpsDataAsync(gpsData);
            count++;
        }

        return count;
    }

    private async Task<int> MigrateSignalHistoryAsync(OleDbConnection connection, List<AccessPoint> accessPoints)
    {
        int count = 0;

        var sql = @"
            SELECT HistID, ApID, GpsID, Signal, RSSI, Date1, Time1
            FROM HIST
        ";

        using var cmd = new OleDbCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var history = new SignalHistory
            {
                HistId = reader.GetInt32(0),
                ApId = reader.GetInt32(1),
                GpsId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Signal = reader.GetInt32(3),
                Rssi = reader.GetInt32(4),
                Timestamp = ParseDateTime(reader, 5, 6)
            };

            await _sqliteService.AddSignalHistoryAsync(history);
            count++;
        }

        return count;
    }

    private AuthenticationType ParseAuthentication(string auth)
    {
        return auth.ToLower() switch
        {
            "open" => AuthenticationType.Open,
            "wpa" => AuthenticationType.WPA,
            "wpa2" => AuthenticationType.WPA2,
            "wpa3" => AuthenticationType.WPA3,
            "wpa-psk" => AuthenticationType.WPA_PSK,
            "wpa2-psk" => AuthenticationType.WPA2_PSK,
            _ => AuthenticationType.Unknown
        };
    }

    private EncryptionType ParseEncryption(string encr)
    {
        return encr.ToLower() switch
        {
            "none" => EncryptionType.None,
            "wep" => EncryptionType.WEP,
            "tkip" => EncryptionType.TKIP,
            "aes" => EncryptionType.AES,
            "ccmp" => EncryptionType.CCMP,
            _ => EncryptionType.Unknown
        };
    }

    private NetworkType ParseNetworkType(string netType)
    {
        return netType.ToLower() switch
        {
            "infrastructure" => NetworkType.Infrastructure,
            "adhoc" => NetworkType.Adhoc,
            _ => NetworkType.Unknown
        };
    }

    private DateTime ParseDateTime(OleDbDataReader reader, int dateIndex, int timeIndex = -1)
    {
        try
        {
            if (timeIndex >= 0)
            {
                // Separate date and time columns
                var date = reader.IsDBNull(dateIndex) ? DateTime.Today : reader.GetDateTime(dateIndex);
                var time = reader.IsDBNull(timeIndex) ? TimeSpan.Zero : reader.GetDateTime(timeIndex).TimeOfDay;
                return date.Date + time;
            }
            else
            {
                // Single datetime column
                return reader.IsDBNull(dateIndex) ? DateTime.Now : reader.GetDateTime(dateIndex);
            }
        }
        catch
        {
            return DateTime.Now;
        }
    }
}

public class MigrationProgress
{
    public string Stage { get; set; } = string.Empty;
    public int Processed { get; set; }
    public int Total { get; set; }
    public string Message { get; set; } = string.Empty;
    public double PercentComplete => Total > 0 ? (double)Processed / Total * 100 : 0;
}
