using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Service for GPS communication and data parsing
/// </summary>
public interface IGpsService
{
    /// <summary>
    /// Event raised when GPS data is received and parsed
    /// </summary>
    event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    
    /// <summary>
    /// Event raised when GPS connection encounters an error
    /// </summary>
    event EventHandler<GpsErrorEventArgs>? GpsError;
    
    /// <summary>
    /// Start GPS communication
    /// </summary>
    Task StartAsync(GpsConfiguration configuration, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop GPS communication
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Get the current GPS data
    /// </summary>
    GpsData? CurrentGpsData { get; }
    
    /// <summary>
    /// Check if GPS is connected and receiving data
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Get available COM ports
    /// </summary>
    string[] GetAvailablePorts();
    
    /// <summary>
    /// Time since last GPS update in seconds
    /// </summary>
    double SecondsSinceLastUpdate { get; }
}

public class GpsDataReceivedEventArgs : EventArgs
{
    public GpsData GpsData { get; set; } = null!;
}

public class GpsErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public class GpsConfiguration
{
    public string ComPort { get; set; } = "COM4";
    public int BaudRate { get; set; } = 4800;
    public System.IO.Ports.Parity Parity { get; set; } = System.IO.Ports.Parity.None;
    public int DataBits { get; set; } = 8;
    public System.IO.Ports.StopBits StopBits { get; set; } = System.IO.Ports.StopBits.One;
    public int TimeoutSeconds { get; set; } = 30;
}
