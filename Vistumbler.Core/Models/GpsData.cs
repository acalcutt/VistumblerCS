namespace Vistumbler.Core.Models;

/// <summary>
/// Represents GPS position data from NMEA sentences
/// </summary>
public class GpsData
{
    public int GpsId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
    public int NumberOfSatellites { get; set; }
    public double? HorizontalDilution { get; set; }
    public double? SpeedKnots { get; set; }
    public double? TrackAngle { get; set; }
    public DateTime Timestamp { get; set; }
    public GpsQuality Quality { get; set; }
    
    /// <summary>
    /// Calculate speed in MPH from knots
    /// </summary>
    public double SpeedMph => SpeedKnots.HasValue ? SpeedKnots.Value * 1.15078 : 0;
    
    /// <summary>
    /// Calculate speed in KM/H from knots
    /// </summary>
    public double SpeedKmh => SpeedKnots.HasValue ? SpeedKnots.Value * 1.852 : 0;
}

public enum GpsQuality
{
    Invalid = 0,
    GpsFix = 1,
    DifferentialGpsFix = 2
}
