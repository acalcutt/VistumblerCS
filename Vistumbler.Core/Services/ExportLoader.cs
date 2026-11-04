using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// What an export needs beyond the list of access points: each one's readings, with the GPS fix each was taken at (a
/// VS1 file's GPS lines and its history, a WiGLE CSV's rows).
/// </summary>
public static class ExportLoader
{
    /// <summary>Fills each access point's <see cref="AccessPoint.SignalHistory"/> from the database, fixes and all.</summary>
    public static async Task LoadHistoryAsync(IDatabaseService db, IEnumerable<AccessPoint> accessPoints)
    {
        var gps = (await db.GetAllGpsDataAsync()).ToDictionary(g => g.GpsId);
        foreach (var ap in accessPoints)
        {
            if (ap.ApId == 0) continue;
            var history = await db.GetSignalHistoryAsync(ap.ApId);
            foreach (var h in history)
                if (h.GpsId is { } id && gps.TryGetValue(id, out var g)) h.GpsData = g;
            ap.SignalHistory = history;
        }
    }

    /// <summary>The stored cell towers and Bluetooth devices, each with its readings and their GPS fixes.</summary>
    public static async Task<List<RadioNetwork>> LoadRadioNetworksAsync(IDatabaseService db)
    {
        var networks = await db.GetAllRadioNetworksAsync();
        if (networks.Count == 0) return networks;
        var gps = (await db.GetAllGpsDataAsync()).ToDictionary(g => g.GpsId);
        foreach (var n in networks)
        {
            n.History = await db.GetRadioHistoryAsync(n.Id);
            foreach (var r in n.History)
                if (r.GpsId is { } id && gps.TryGetValue(id, out var g)) r.GpsData = g;
        }
        return networks;
    }
}
