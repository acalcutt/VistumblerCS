using Vistumbler.Core.Enums;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.Infrastructure.Gps;

/// <summary>
/// Routes GPS calls to either <see cref="SerialGpsService"/> (COM port) or
/// <see cref="WindowsLocationGpsService"/> (Windows Location API) based on the
/// <see cref="GpsConfiguration.Source"/> supplied at <see cref="StartAsync"/>.
///
/// This class is the single <see cref="IGpsService"/> registered in DI.
/// </summary>
public class GpsServiceRouter : IGpsService
{
    private readonly SerialGpsService          _serial;
    private readonly WindowsLocationGpsService _winLocation;
    private IGpsService?                       _active;

    public GpsServiceRouter(SerialGpsService serial, WindowsLocationGpsService winLocation)
    {
        _serial      = serial;
        _winLocation = winLocation;

        // Forward events from both underlying services
        _serial.GpsDataReceived      += (s, e) => GpsDataReceived?.Invoke(this, e);
        _serial.GpsError             += (s, e) => GpsError?.Invoke(this, e);
        _winLocation.GpsDataReceived += (s, e) => GpsDataReceived?.Invoke(this, e);
        _winLocation.GpsError        += (s, e) => GpsError?.Invoke(this, e);
    }

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData        => _active?.CurrentGpsData;
    public bool     IsConnected           => _active?.IsConnected ?? false;
    public double   SecondsSinceLastUpdate => _active?.SecondsSinceLastUpdate ?? 0;

    public string[] GetAvailablePorts() => _serial.GetAvailablePorts();

    public async Task StartAsync(GpsConfiguration configuration, CancellationToken cancellationToken = default)
    {
        // Stop any currently-running back-end first
        Stop();

        _active = configuration.Source switch
        {
            GpsSourceType.WindowsLocation => _winLocation,
            _                             => _serial,
        };

        await _active.StartAsync(configuration, cancellationToken);
    }

    public void Stop()
    {
        _active?.Stop();
        _active = null;
    }
}
