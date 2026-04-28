using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Service for database operations
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    /// Initialize the database
    /// </summary>
    Task InitializeAsync(string databasePath);
    
    /// <summary>
    /// Add or update an access point
    /// </summary>
    Task<int> UpsertAccessPointAsync(AccessPoint accessPoint);
    
    /// <summary>
    /// Add signal history entry
    /// </summary>
    Task AddSignalHistoryAsync(SignalHistory signalHistory);
    
    /// <summary>
    /// Add GPS data
    /// </summary>
    Task<int> AddGpsDataAsync(GpsData gpsData);
    
    /// <summary>
    /// Get all access points
    /// </summary>
    Task<List<AccessPoint>> GetAllAccessPointsAsync();
    
    /// <summary>
    /// Get access point by BSSID
    /// </summary>
    Task<AccessPoint?> GetAccessPointByBssidAsync(string bssid);
    
    /// <summary>
    /// Get signal history for an access point
    /// </summary>
    Task<List<SignalHistory>> GetSignalHistoryAsync(int apId);
    
    /// <summary>
    /// Clear all access points
    /// </summary>
    Task ClearAllAccessPointsAsync();
    
    /// <summary>
    /// Get manufacturer for MAC address
    /// </summary>
    Task<string> GetManufacturerAsync(string macPrefix);
    
    /// <summary>
    /// Add or update manufacturer
    /// </summary>
    Task UpsertManufacturerAsync(string macPrefix, string manufacturer);

    /// <summary>
    /// Get all manufacturers
    /// </summary>
    Task<List<(string MacPrefix, string Manufacturer)>> GetAllManufacturersAsync();

    /// <summary>
    /// Delete manufacturer by MAC prefix
    /// </summary>
    Task DeleteManufacturerAsync(string macPrefix);

    /// <summary>
    /// Bulk upsert manufacturers from IEEE OUI data (replaces existing entries with matching prefix)
    /// </summary>
    Task BulkUpsertManufacturersAsync(IEnumerable<(string MacPrefix, string Manufacturer)> entries);
    
    /// <summary>
    /// Get label for BSSID
    /// </summary>
    Task<string?> GetLabelAsync(string bssid);
    
    /// <summary>
    /// Add or update label
    /// </summary>
    Task UpsertLabelAsync(string bssid, string label);

    /// <summary>
    /// Get all labels
    /// </summary>
    Task<List<(string Bssid, string Label)>> GetAllLabelsAsync();

    /// <summary>
    /// Delete label by BSSID
    /// </summary>
    Task DeleteLabelAsync(string bssid);
    
    // ── Filters ────────────────────────────────────────────────────────────

    /// <summary>Get all saved filters.</summary>
    Task<List<FilterRecord>> GetAllFiltersAsync();

    /// <summary>Add or update a filter (FiltId == 0 → insert, else update).</summary>
    Task<int> UpsertFilterAsync(FilterRecord filter);

    /// <summary>Delete a filter by id.</summary>
    Task DeleteFilterAsync(int filtId);

    // ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Close database connection
    /// </summary>
    Task CloseAsync();
}
