using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Service for exporting data to various formats
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Export access points to KML format for Google Earth
    /// </summary>
    Task ExportToKmlAsync(string filePath, List<AccessPoint> accessPoints, ExportOptions options, IReadOnlyList<GpsData>? gpsFixes = null);
    
    /// <summary>
    /// Export access points to GPX format
    /// </summary>
    Task ExportToGpxAsync(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<GpsData>? gpsFixes = null);

    /// <summary>
    /// Export access points to NS1 format (NetStumbler binary)
    /// </summary>
    Task ExportToNs1Async(string filePath, List<AccessPoint> accessPoints);

    /// <summary>
    /// Export access points to KismetDB format
    /// </summary>
    Task ExportToKismetDbAsync(string filePath, List<AccessPoint> accessPoints);
    
    /// <summary>
    /// Export access points to NetXML format (Kismet Legacy)
    /// </summary>
    Task ExportToNetXmlAsync(string filePath, List<AccessPoint> accessPoints);

    /// <summary>
    /// Export access points to Vistumbler's CSV: detailed (a row per reading, which WifiDB imports) or summary (a row per AP)
    /// </summary>
    Task ExportToCsvAsync(string filePath, List<AccessPoint> accessPoints, bool detailed = true);

    /// <summary>
    /// Export access points to Wigle CSV format, with the cell towers and Bluetooth devices
    /// </summary>
    Task ExportToWigleCsvAsync(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<RadioNetwork>? radios = null);
    
    /// <summary>
    /// Export to VS1 text format (Vistumbler native): version 4.0, or 4.1 with the cell towers and Bluetooth devices
    /// when there are any
    /// </summary>
    Task ExportToVs1Async(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<RadioNetwork>? radios = null);

    /// <summary>
    /// Export to VSZ compressed format
    /// </summary>
    Task ExportToVszAsync(string filePath, List<AccessPoint> accessPoints, IReadOnlyList<RadioNetwork>? radios = null);

    /// <summary>
    /// GeoJSON with WifiDB's property names: a point per AP at its strongest position, or with <paramref name="signalMap"/>
    /// a point per reading (the readings loaded, <see cref="ExportLoader.LoadHistoryAsync"/>)
    /// </summary>
    Task ExportToGeoJsonAsync(string filePath, List<AccessPoint> accessPoints, bool signalMap);

    /// <summary>
    /// A KML signal map: a folder per AP, with a dot for each reading coloured by its RSSI
    /// </summary>
    Task ExportToKmlSignalMapAsync(string filePath, List<AccessPoint> accessPoints);
}

public class ExportOptions
{
    public bool IncludeOpenNetworks { get; set; } = true;
    public bool IncludeWepNetworks { get; set; } = true;
    public bool IncludeSecureNetworks { get; set; } = true;
    public bool ShowTrack { get; set; } = true;
    public bool UseSignalColors { get; set; } = true;
    public string TrackColor { get; set; } = "7F0000FF";
}
