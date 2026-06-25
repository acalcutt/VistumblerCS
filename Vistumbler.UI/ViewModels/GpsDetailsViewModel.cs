using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.ViewModels;

public partial class GpsDetailsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _latitude = "N/A";

    [ObservableProperty]
    private string _longitude = "N/A";

    [ObservableProperty]
    private string _altitude = "N/A";

    [ObservableProperty]
    private int _numberOfSatellites;

    [ObservableProperty]
    private string _horizontalDilution = "N/A";

    [ObservableProperty]
    private string _speedKnots = "N/A";

    [ObservableProperty]
    private string _speedMph = "N/A";

    [ObservableProperty]
    private string _speedKmh = "N/A";

    [ObservableProperty]
    private string _trackAngle = "N/A";

    [ObservableProperty]
    private string _quality = "N/A";

    [ObservableProperty]
    private string _timestamp = "N/A";

    /// <summary>Heading in degrees (0-360) for the compass needle. Null when unavailable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeadingForRotation))]
    private double? _trackAngleDegrees;

    /// <summary>Non-null heading used to drive the compass needle RotateTransform.</summary>
    public double HeadingForRotation => TrackAngleDegrees ?? 0.0;

    /// <summary>True when valid GPS data has been received at least once.</summary>
    [ObservableProperty]
    private bool _hasFix;

    public void UpdateFromGpsData(GpsData? gpsData)
    {
        if (gpsData == null)
        {
            Latitude = "N/A";
            Longitude = "N/A";
            Altitude = "N/A";
            TrackAngleDegrees = null;
            HasFix = false;
            return;
        }

        Latitude = $"{gpsData.Latitude:F6}°";
        Longitude = $"{gpsData.Longitude:F6}°";
        Altitude = gpsData.Altitude.HasValue ? $"{gpsData.Altitude:F2} m" : "N/A";
        NumberOfSatellites = gpsData.NumberOfSatellites;
        HorizontalDilution = gpsData.HorizontalDilution.HasValue ? $"{gpsData.HorizontalDilution:F2}" : "N/A";
        SpeedKnots = gpsData.SpeedKnots.HasValue ? $"{gpsData.SpeedKnots:F2} knots" : "N/A";
        SpeedMph = $"{gpsData.SpeedMph:F2} mph";
        SpeedKmh = $"{gpsData.SpeedKmh:F2} km/h";
        TrackAngle = gpsData.TrackAngle.HasValue ? $"{gpsData.TrackAngle:F2}°" : "N/A";
        TrackAngleDegrees = gpsData.TrackAngle;
        Quality = gpsData.Quality.ToString();
        Timestamp = gpsData.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        HasFix = true;
    }
}
