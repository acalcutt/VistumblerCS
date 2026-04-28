using Windows.Devices.Geolocation;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Gps;

/// <summary>
/// GPS service implementation using the Windows Location API
/// (Windows.Devices.Geolocation.Geolocator).  Supports GPS hardware,
/// Wi-Fi positioning, and cell-tower positioning — whichever the platform
/// provides.  Requires Location permission to be granted by the user.
/// </summary>
public class WindowsLocationGpsService : IGpsService
{
    private Geolocator? _locator;
    private bool _isConnected;
    private GpsData? _currentGpsData;
    private DateTime _lastUpdateTime;
    private CancellationTokenSource? _cts;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>? GpsError;

    public GpsData?  CurrentGpsData        => _currentGpsData;
    public bool      IsConnected           => _isConnected;
    public double    SecondsSinceLastUpdate =>
        _lastUpdateTime != default ? (DateTime.Now - _lastUpdateTime).TotalSeconds : 0;

    public string[] GetAvailablePorts() => Array.Empty<string>(); // not applicable

    public async Task StartAsync(GpsConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_isConnected) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Request access – this shows a consent prompt the first time
        var access = await Geolocator.RequestAccessAsync();
        if (access != GeolocationAccessStatus.Allowed)
        {
            OnGpsError(new GpsErrorEventArgs
            {
                ErrorMessage = access == GeolocationAccessStatus.Denied
                    ? "Location access denied. Enable Location in Windows Settings → Privacy."
                    : "Location access is not available on this device."
            });
            return;
        }

        _locator = new Geolocator
        {
            ReportInterval    = 1000,       // request update at most every 1 s
            DesiredAccuracy   = PositionAccuracy.High,
            MovementThreshold = 0,          // report any movement
        };

        _locator.PositionChanged  += OnPositionChanged;
        _locator.StatusChanged    += OnStatusChanged;

        _isConnected = true;

        // Kick off an initial position fix so we have something right away
        try
        {
            var pos = await _locator.GetGeopositionAsync().AsTask(_cts.Token);
            PublishPosition(pos.Coordinate);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnGpsError(new GpsErrorEventArgs
            {
                ErrorMessage = "Could not get initial GPS position",
                Exception    = ex
            });
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _isConnected = false;
        if (_locator != null)
        {
            _locator.PositionChanged -= OnPositionChanged;
            _locator.StatusChanged   -= OnStatusChanged;
            _locator = null;
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnPositionChanged(Geolocator sender, PositionChangedEventArgs args)
    {
        PublishPosition(args.Position.Coordinate);
    }

    private void OnStatusChanged(Geolocator sender, StatusChangedEventArgs args)
    {
        if (args.Status == PositionStatus.Disabled || args.Status == PositionStatus.NotAvailable)
        {
            OnGpsError(new GpsErrorEventArgs
            {
                ErrorMessage = $"Windows Location status changed: {args.Status}"
            });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PublishPosition(Geocoordinate coord)
    {
        _currentGpsData ??= new GpsData();

        _currentGpsData.Latitude            = coord.Point.Position.Latitude;
        _currentGpsData.Longitude           = coord.Point.Position.Longitude;
        _currentGpsData.Altitude            = coord.Point.Position.Altitude;
        _currentGpsData.HorizontalDilution  = coord.Accuracy;    // accuracy in metres
        _currentGpsData.Quality             = GpsQuality.GpsFix; // Windows doesn't expose raw quality
        _currentGpsData.Timestamp           = coord.Timestamp.LocalDateTime;

        if (coord.Heading.HasValue)      _currentGpsData.TrackAngle = coord.Heading.Value;
        if (coord.Speed.HasValue)        _currentGpsData.SpeedKnots = coord.Speed.Value * 1.94384; // m/s → knots
        if (coord.SatelliteData?.PositionDilutionOfPrecision.HasValue == true)
            _currentGpsData.HorizontalDilution = coord.SatelliteData.PositionDilutionOfPrecision!.Value;

        _lastUpdateTime = DateTime.Now;
        OnGpsDataReceived(new GpsDataReceivedEventArgs { GpsData = _currentGpsData });
    }

    protected virtual void OnGpsDataReceived(GpsDataReceivedEventArgs e) => GpsDataReceived?.Invoke(this, e);
    protected virtual void OnGpsError(GpsErrorEventArgs e)               => GpsError?.Invoke(this, e);
}
