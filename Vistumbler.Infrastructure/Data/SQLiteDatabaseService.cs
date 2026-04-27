using System.Data;
using System.Data.SQLite;
using Dapper;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Data;

/// <summary>
/// SQLite database service implementation
/// </summary>
public class SQLiteDatabaseService : IDatabaseService
{
    private SQLiteConnection? _connection;
    private string? _databasePath;

    public async Task InitializeAsync(string databasePath)
    {
        _databasePath = databasePath;

        // Create database file if it doesn't exist
        if (!File.Exists(databasePath))
        {
            SQLiteConnection.CreateFile(databasePath);
        }

        _connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
        await _connection.OpenAsync();

        await CreateTablesAsync();
    }

    private async Task CreateTablesAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            CREATE TABLE IF NOT EXISTS AccessPoints (
                ApId INTEGER PRIMARY KEY AUTOINCREMENT,
                Bssid TEXT UNIQUE NOT NULL,
                Ssid TEXT,
                Manufacturer TEXT,
                Label TEXT,
                NetworkType INTEGER,
                Authentication INTEGER,
                Encryption INTEGER,
                RadioType TEXT,
                Channel INTEGER,
                Signal INTEGER,
                HighestSignal INTEGER,
                Rssi INTEGER,
                HighestRssi INTEGER,
                BasicTransferRates TEXT,
                OtherTransferRates TEXT,
                FirstSeen TEXT,
                LastSeen TEXT,
                Latitude REAL,
                Longitude REAL
            );

            CREATE TABLE IF NOT EXISTS SignalHistory (
                HistId INTEGER PRIMARY KEY AUTOINCREMENT,
                ApId INTEGER NOT NULL,
                GpsId INTEGER,
                Signal INTEGER,
                Rssi INTEGER,
                Timestamp TEXT,
                FOREIGN KEY(ApId) REFERENCES AccessPoints(ApId),
                FOREIGN KEY(GpsId) REFERENCES GpsData(GpsId)
            );

            CREATE TABLE IF NOT EXISTS GpsData (
                GpsId INTEGER PRIMARY KEY AUTOINCREMENT,
                Latitude REAL,
                Longitude REAL,
                Altitude REAL,
                NumberOfSatellites INTEGER,
                HorizontalDilution REAL,
                SpeedKnots REAL,
                TrackAngle REAL,
                Timestamp TEXT,
                Quality INTEGER
            );

            CREATE TABLE IF NOT EXISTS Manufacturers (
                MacPrefix TEXT PRIMARY KEY,
                Manufacturer TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Labels (
                Bssid TEXT PRIMARY KEY,
                Label TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_ap_bssid ON AccessPoints(Bssid);
            CREATE INDEX IF NOT EXISTS idx_signal_apid ON SignalHistory(ApId);
            CREATE INDEX IF NOT EXISTS idx_signal_timestamp ON SignalHistory(Timestamp);
        ";

        await _connection.ExecuteAsync(sql);
    }

    public async Task<int> UpsertAccessPointAsync(AccessPoint accessPoint)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            INSERT INTO AccessPoints (
                Bssid, Ssid, Manufacturer, Label, NetworkType, Authentication, Encryption,
                RadioType, Channel, Signal, HighestSignal, Rssi, HighestRssi,
                BasicTransferRates, OtherTransferRates, FirstSeen, LastSeen, Latitude, Longitude
            ) VALUES (
                @Bssid, @Ssid, @Manufacturer, @Label, @NetworkType, @Authentication, @Encryption,
                @RadioType, @Channel, @Signal, @HighestSignal, @Rssi, @HighestRssi,
                @BasicTransferRates, @OtherTransferRates, @FirstSeen, @LastSeen, @Latitude, @Longitude
            )
            ON CONFLICT(Bssid) DO UPDATE SET
                Ssid = @Ssid,
                Manufacturer = @Manufacturer,
                Label = @Label,
                NetworkType = @NetworkType,
                Authentication = @Authentication,
                Encryption = @Encryption,
                RadioType = @RadioType,
                Channel = @Channel,
                Signal = @Signal,
                HighestSignal = CASE WHEN @Signal > HighestSignal THEN @Signal ELSE HighestSignal END,
                Rssi = @Rssi,
                HighestRssi = CASE WHEN @Rssi > HighestRssi THEN @Rssi ELSE HighestRssi END,
                BasicTransferRates = @BasicTransferRates,
                OtherTransferRates = @OtherTransferRates,
                LastSeen = @LastSeen,
                Latitude = COALESCE(@Latitude, Latitude),
                Longitude = COALESCE(@Longitude, Longitude);

            SELECT last_insert_rowid();
        ";

        var result = await _connection.ExecuteScalarAsync<int>(sql, accessPoint);
        
        // If it was an update, get the existing ID
        if (result == 0)
        {
            var ap = await GetAccessPointByBssidAsync(accessPoint.Bssid);
            return ap?.ApId ?? 0;
        }

        return result;
    }

    public async Task AddSignalHistoryAsync(SignalHistory signalHistory)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            INSERT INTO SignalHistory (ApId, GpsId, Signal, Rssi, Timestamp)
            VALUES (@ApId, @GpsId, @Signal, @Rssi, @Timestamp)
        ";

        await _connection.ExecuteAsync(sql, signalHistory);
    }

    public async Task<int> AddGpsDataAsync(GpsData gpsData)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            INSERT INTO GpsData (
                Latitude, Longitude, Altitude, NumberOfSatellites,
                HorizontalDilution, SpeedKnots, TrackAngle, Timestamp, Quality
            ) VALUES (
                @Latitude, @Longitude, @Altitude, @NumberOfSatellites,
                @HorizontalDilution, @SpeedKnots, @TrackAngle, @Timestamp, @Quality
            );
            SELECT last_insert_rowid();
        ";

        return await _connection.ExecuteScalarAsync<int>(sql, gpsData);
    }

    public async Task<List<AccessPoint>> GetAllAccessPointsAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = "SELECT * FROM AccessPoints ORDER BY LastSeen DESC";
        var result = await _connection.QueryAsync<AccessPoint>(sql);
        return result.ToList();
    }

    public async Task<AccessPoint?> GetAccessPointByBssidAsync(string bssid)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = "SELECT * FROM AccessPoints WHERE Bssid = @Bssid";
        return await _connection.QueryFirstOrDefaultAsync<AccessPoint>(sql, new { Bssid = bssid });
    }

    public async Task<List<SignalHistory>> GetSignalHistoryAsync(int apId)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            SELECT * FROM SignalHistory 
            WHERE ApId = @ApId 
            ORDER BY Timestamp ASC
        ";
        
        var result = await _connection.QueryAsync<SignalHistory>(sql, new { ApId = apId });
        return result.ToList();
    }

    public async Task ClearAllAccessPointsAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        await _connection.ExecuteAsync("DELETE FROM SignalHistory");
        await _connection.ExecuteAsync("DELETE FROM AccessPoints");
        await _connection.ExecuteAsync("DELETE FROM GpsData");
    }

    public async Task<string> GetManufacturerAsync(string macPrefix)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        // Get first 8 characters of MAC (XX:XX:XX format)
        var prefix = macPrefix.Length >= 8 ? macPrefix.Substring(0, 8).ToUpper() : macPrefix.ToUpper();
        
        var sql = "SELECT Manufacturer FROM Manufacturers WHERE MacPrefix = @Prefix";
        var result = await _connection.QueryFirstOrDefaultAsync<string>(sql, new { Prefix = prefix });
        
        return result ?? "Unknown";
    }

    public async Task UpsertManufacturerAsync(string macPrefix, string manufacturer)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            INSERT INTO Manufacturers (MacPrefix, Manufacturer)
            VALUES (@MacPrefix, @Manufacturer)
            ON CONFLICT(MacPrefix) DO UPDATE SET Manufacturer = @Manufacturer
        ";

        await _connection.ExecuteAsync(sql, new { MacPrefix = macPrefix.ToUpper(), Manufacturer = manufacturer });
    }

    public async Task<List<(string MacPrefix, string Manufacturer)>> GetAllManufacturersAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var rows = await _connection.QueryAsync<(string MacPrefix, string Manufacturer)>(
            "SELECT MacPrefix, Manufacturer FROM Manufacturers ORDER BY MacPrefix");
        return rows.ToList();
    }

    public async Task DeleteManufacturerAsync(string macPrefix)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        await _connection.ExecuteAsync(
            "DELETE FROM Manufacturers WHERE MacPrefix = @Prefix",
            new { Prefix = macPrefix.ToUpper() });
    }

    public async Task BulkUpsertManufacturersAsync(IEnumerable<(string MacPrefix, string Manufacturer)> entries)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        const string sql = @"
            INSERT INTO Manufacturers (MacPrefix, Manufacturer)
            VALUES (@MacPrefix, @Manufacturer)
            ON CONFLICT(MacPrefix) DO UPDATE SET Manufacturer = @Manufacturer";

        using var transaction = _connection.BeginTransaction();
        await _connection.ExecuteAsync(sql,
            entries.Select(e => new { MacPrefix = e.MacPrefix.ToUpper(), e.Manufacturer }),
            transaction);
        transaction.Commit();
    }

    public async Task<string?> GetLabelAsync(string bssid)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = "SELECT Label FROM Labels WHERE Bssid = @Bssid";
        return await _connection.QueryFirstOrDefaultAsync<string>(sql, new { Bssid = bssid.ToUpper() });
    }

    public async Task UpsertLabelAsync(string bssid, string label)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            INSERT INTO Labels (Bssid, Label)
            VALUES (@Bssid, @Label)
            ON CONFLICT(Bssid) DO UPDATE SET Label = @Label
        ";

        await _connection.ExecuteAsync(sql, new { Bssid = bssid.ToUpper(), Label = label });
    }

    public async Task<List<(string Bssid, string Label)>> GetAllLabelsAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var rows = await _connection.QueryAsync<(string Bssid, string Label)>(
            "SELECT Bssid, Label FROM Labels ORDER BY Bssid");
        return rows.ToList();
    }

    public async Task DeleteLabelAsync(string bssid)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        await _connection.ExecuteAsync(
            "DELETE FROM Labels WHERE Bssid = @Bssid",
            new { Bssid = bssid.ToUpper() });
    }

    public async Task CloseAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
            _connection = null;
        }
    }
}
