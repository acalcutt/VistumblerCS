using System.Data;
using Microsoft.Data.Sqlite;
using Dapper;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Data;

/// <summary>
/// SQLite database service implementation
/// </summary>
public class SQLiteDatabaseService : IDatabaseService
{
    private SqliteConnection? _connection;
    private string? _databasePath;

    public async Task InitializeAsync(string databasePath)
    {
        _databasePath = databasePath;

        _connection = new SqliteConnection($"Data Source={databasePath}");
        await _connection.OpenAsync();

        await CreateTablesAsync();
    }

    private async Task CreateTablesAsync()
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var statements = new[]
        {
            @"CREATE TABLE IF NOT EXISTS AccessPoints (
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
                FrequencyMhz INTEGER,
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
            )",
            @"CREATE TABLE IF NOT EXISTS SignalHistory (
                HistId INTEGER PRIMARY KEY AUTOINCREMENT,
                ApId INTEGER NOT NULL,
                GpsId INTEGER,
                Signal INTEGER,
                Rssi INTEGER,
                Timestamp TEXT,
                FOREIGN KEY(ApId) REFERENCES AccessPoints(ApId),
                FOREIGN KEY(GpsId) REFERENCES GpsData(GpsId)
            )",
            @"CREATE TABLE IF NOT EXISTS GpsData (
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
            )",
            "CREATE TABLE IF NOT EXISTS Manufacturers (MacPrefix TEXT PRIMARY KEY, Manufacturer TEXT NOT NULL)",
            "CREATE TABLE IF NOT EXISTS Labels (Bssid TEXT PRIMARY KEY, Label TEXT NOT NULL)",
            "CREATE INDEX IF NOT EXISTS idx_ap_bssid ON AccessPoints(Bssid)",
            "CREATE INDEX IF NOT EXISTS idx_signal_apid ON SignalHistory(ApId)",
            "CREATE INDEX IF NOT EXISTS idx_signal_timestamp ON SignalHistory(Timestamp)",
            @"CREATE TABLE IF NOT EXISTS Filters (
                FiltId   INTEGER PRIMARY KEY AUTOINCREMENT,
                FiltName TEXT NOT NULL,
                FiltDesc TEXT,
                Ssid     TEXT DEFAULT '*',
                Bssid    TEXT DEFAULT '*',
                Channel  TEXT DEFAULT '*',
                Auth     TEXT DEFAULT '*',
                Encr     TEXT DEFAULT '*',
                RadType  TEXT DEFAULT '*',
                NetType  TEXT DEFAULT '*',
                Signal   TEXT DEFAULT '*',
                HighSig  TEXT DEFAULT '*',
                Rssi     TEXT DEFAULT '*',
                HighRssi TEXT DEFAULT '*',
                Btx      TEXT DEFAULT '*',
                Otx      TEXT DEFAULT '*',
                Active   TEXT DEFAULT '*'
            )",
        };

        foreach (var s in statements)
            await _connection.ExecuteAsync(s);

        // Migration: add FrequencyMhz if it doesn't exist in an older DB
        try
        {
            await _connection.ExecuteAsync("ALTER TABLE AccessPoints ADD COLUMN FrequencyMhz INTEGER DEFAULT 0");
        }
        catch { /* Column already exists — safe to ignore */ }
    }

    public async Task<int> UpsertAccessPointAsync(AccessPoint accessPoint)
    {
        if (_connection == null)
            throw new InvalidOperationException("Database not initialized");

        var sql = @"
            INSERT INTO AccessPoints (
                Bssid, Ssid, Manufacturer, Label, NetworkType, Authentication, Encryption,
                RadioType, Channel, FrequencyMhz, Signal, HighestSignal, Rssi, HighestRssi,
                BasicTransferRates, OtherTransferRates, FirstSeen, LastSeen, Latitude, Longitude
            ) VALUES (
                @Bssid, @Ssid, @Manufacturer, @Label, @NetworkType, @Authentication, @Encryption,
                @RadioType, @Channel, @FrequencyMhz, @Signal, @HighestSignal, @Rssi, @HighestRssi,
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
                FrequencyMhz = @FrequencyMhz,
                Signal = @Signal,
                HighestSignal = CASE WHEN @Signal > HighestSignal THEN @Signal ELSE HighestSignal END,
                Rssi = @Rssi,
                HighestRssi = CASE WHEN @Rssi > HighestRssi THEN @Rssi ELSE HighestRssi END,
                BasicTransferRates = @BasicTransferRates,
                OtherTransferRates = @OtherTransferRates,
                LastSeen = @LastSeen,
                Latitude = CASE
                    WHEN @Latitude IS NOT NULL AND (
                        Latitude IS NULL OR
                        @Rssi >= HighestRssi OR
                        HighestRssi IS NULL
                    ) THEN @Latitude
                    ELSE Latitude
                END,
                Longitude = CASE
                    WHEN @Longitude IS NOT NULL AND (
                        Longitude IS NULL OR
                        @Rssi >= HighestRssi OR
                        HighestRssi IS NULL
                    ) THEN @Longitude
                    ELSE Longitude
                END
            RETURNING ApId;
        ";

        var result = await _connection.ExecuteScalarAsync<int?>(sql, accessPoint);
        if (result is null or 0)
        {
            var ap = await GetAccessPointByBssidAsync(accessPoint.Bssid);
            return ap?.ApId ?? 0;
        }

        return result.Value;
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
            ) RETURNING GpsId;
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

        // Normalize to 6-char OUI hex (strip colons/dashes, uppercase) — matches IEEE OUI format
        var cleanMac = System.Text.RegularExpressions.Regex.Replace(macPrefix, "[^0-9A-Fa-f]", "");
        var prefix = cleanMac.Length >= 6 ? cleanMac.Substring(0, 6).ToUpper() : cleanMac.ToUpper();
        
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
            // Clear the pool BEFORE disposing: Microsoft.Data.Sqlite pools
            // connections by default, so the OS file handle survives Dispose()
            // and keeps the .db locked — blocking any later File.Delete.
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();
            _connection = null;
        }
    }

    // ── Filters ─────────────────────────────────────────────────────────────

    public async Task<List<FilterRecord>> GetAllFiltersAsync()
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");
        var rows = await _connection.QueryAsync<FilterRecord>("SELECT * FROM Filters ORDER BY FiltId");
        return rows.ToList();
    }

    public async Task<int> UpsertFilterAsync(FilterRecord filter)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");

        if (filter.FiltId == 0)
        {
            var sql = @"
                INSERT INTO Filters (FiltName, FiltDesc, Ssid, Bssid, Channel, Auth, Encr,
                    RadType, NetType, Signal, HighSig, Rssi, HighRssi, Btx, Otx, Active)
                VALUES (@FiltName, @FiltDesc, @Ssid, @Bssid, @Channel, @Auth, @Encr,
                    @RadType, @NetType, @Signal, @HighSig, @Rssi, @HighRssi, @Btx, @Otx, @Active);
                SELECT last_insert_rowid();";
            return await _connection.ExecuteScalarAsync<int>(sql, filter);
        }
        else
        {
            var sql = @"
                UPDATE Filters SET
                    FiltName = @FiltName, FiltDesc = @FiltDesc, Ssid = @Ssid, Bssid = @Bssid,
                    Channel = @Channel, Auth = @Auth, Encr = @Encr, RadType = @RadType,
                    NetType = @NetType, Signal = @Signal, HighSig = @HighSig, Rssi = @Rssi,
                    HighRssi = @HighRssi, Btx = @Btx, Otx = @Otx, Active = @Active
                WHERE FiltId = @FiltId";
            await _connection.ExecuteAsync(sql, filter);
            return filter.FiltId;
        }
    }

    public async Task DeleteFilterAsync(int filtId)
    {
        if (_connection == null) throw new InvalidOperationException("Database not initialized");
        await _connection.ExecuteAsync("DELETE FROM Filters WHERE FiltId = @filtId", new { filtId });
    }
}
