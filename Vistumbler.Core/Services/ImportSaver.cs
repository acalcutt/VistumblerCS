using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Saves imported access points with their signal history: each reading goes in with the GPS fix it was taken at,
/// and a fix shared by several APs (as a VS1 file's GPS lines are) is stored once. The cell towers and Bluetooth
/// devices an import brings (VS1 4.1, WiGLE CSV) go in the same way, sharing the same fixes.
/// </summary>
public static class ImportSaver
{
    /// <returns>How many access points were saved.</returns>
    public static async Task<int> SaveAsync(IDatabaseService db, IEnumerable<AccessPoint> accessPoints, IEnumerable<RadioNetwork>? radios = null)
    {
        var gpsIds = new Dictionary<GpsData, int>(ReferenceEqualityComparer.Instance);
        async Task<int?> GpsIdOf(GpsData? gps)
        {
            if (gps is null) return null;
            if (!gpsIds.TryGetValue(gps, out var id))
                gpsIds[gps] = id = await db.AddGpsDataAsync(gps);
            return id;
        }

        int saved = 0;
        foreach (var ap in accessPoints)
        {
            int apId = await db.UpsertAccessPointAsync(ap);
            if (apId == 0) continue;
            saved++;
            foreach (var h in ap.SignalHistory)
            {
                await db.AddSignalHistoryAsync(new SignalHistory
                {
                    ApId = apId,
                    GpsId = await GpsIdOf(h.GpsData),
                    Signal = h.Signal,
                    Rssi = h.Rssi,
                    Timestamp = h.Timestamp,
                });
            }
        }

        foreach (var n in radios ?? Enumerable.Empty<RadioNetwork>())
        {
            int id = await db.UpsertRadioNetworkAsync(n);
            if (id == 0) continue;
            foreach (var r in n.History)
                await db.AddRadioReadingAsync(new RadioReading { NetworkId = id, GpsId = await GpsIdOf(r.GpsData), Rssi = r.Rssi, Timestamp = r.Timestamp });
        }
        return saved;
    }
}
