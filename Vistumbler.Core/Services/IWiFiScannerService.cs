using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Service for scanning and monitoring WiFi networks
/// </summary>
public interface IWiFiScannerService
{
    /// <summary>
    /// Event raised when new networks are detected
    /// </summary>
    event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    
    /// <summary>
    /// Event raised when scanning encounters an error
    /// </summary>
    event EventHandler<ScanErrorEventArgs>? ScanError;
    
    /// <summary>
    /// Start scanning for WiFi networks
    /// </summary>
    Task StartScanningAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stop scanning for WiFi networks
    /// </summary>
    void StopScanning();
    
    /// <summary>
    /// Get list of available WiFi adapters
    /// </summary>
    Task<List<WiFiAdapter>> GetAvailableAdaptersAsync();
    
    /// <summary>
    /// Set the active WiFi adapter
    /// </summary>
    void SetActiveAdapter(string adapterId);
    
    /// <summary>
    /// Check if currently scanning
    /// </summary>
    bool IsScanning { get; }
    
    /// <summary>
    /// Get the currently connected access point BSSID
    /// </summary>
    Task<string?> GetConnectedBssidAsync();
}

public class AccessPointsDetectedEventArgs : EventArgs
{
    public List<AccessPoint> AccessPoints { get; set; } = new();
}

public class ScanErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

public class WiFiAdapter
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
