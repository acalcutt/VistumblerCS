namespace Vistumbler.Core.Models;

/// <summary>
/// Represents a signal measurement for an access point at a specific time and location
/// </summary>
public class SignalHistory
{
    public int HistId { get; set; }
    public int ApId { get; set; }
    public int? GpsId { get; set; }
    public int Signal { get; set; }
    public int Rssi { get; set; }
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Navigation property for the access point
    /// </summary>
    public AccessPoint? AccessPoint { get; set; }
    
    /// <summary>
    /// Navigation property for GPS data
    /// </summary>
    public GpsData? GpsData { get; set; }
}
