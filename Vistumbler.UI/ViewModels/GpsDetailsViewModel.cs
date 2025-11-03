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

    public void UpdateFromGpsData(GpsData? gpsData)
    {
        if (gpsData == null)
        {
            Latitude = "N/A";
            Longitude = "N/A";
            Altitude = "N/A";
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
        Quality = gpsData.Quality.ToString();
        Timestamp = gpsData.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
