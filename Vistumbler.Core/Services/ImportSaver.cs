using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Saves imported access points with their signal history: each reading goes in with the GPS fix it was taken at,
/// and a fix shared by several APs (as a VS1 file's GPS lines are) is stored once.
/// </summary>
public static class ImportSaver
{
    /// <returns>How many access points were saved.</returns>
    public static async Task<int> SaveAsync(IDatabaseService db, IEnumerable<AccessPoint> accessPoints)
    {
        var gpsIds = new Dictionary<GpsData, int>(ReferenceEqualityComparer.Instance);
        int saved = 0;
        foreach (var ap in accessPoints)
        {
            int apId = await db.UpsertAccessPointAsync(ap);
            if (apId == 0) continue;
            saved++;
            foreach (var h in ap.SignalHistory)
            {
                int? gpsId = null;
                if (h.GpsData is { } gps)
                {
                    if (!gpsIds.TryGetValue(gps, out var id))
                        gpsIds[gps] = id = await db.AddGpsDataAsync(gps);
                    gpsId = id;
                }
                await db.AddSignalHistoryAsync(new SignalHistory
                {
                    ApId = apId,
                    GpsId = gpsId,
                    Signal = h.Signal,
                    Rssi = h.Rssi,
                    Timestamp = h.Timestamp,
                });
            }
        }
        return saved;
    }
}
