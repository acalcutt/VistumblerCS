using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Vistumbler.Core.Models;
using Vistumbler.Core.Extensions;

namespace Vistumbler.UI.ViewModels;

public partial class AccessPointViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _apId;

    [ObservableProperty]
    private string _bssid = string.Empty;

    [ObservableProperty]
    private string _ssid = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private int _channel;

    [ObservableProperty]
    private int _frequencyMhz;

    [ObservableProperty]
    private int? _signal;

    [ObservableProperty]
    private int? _highestSignal;

    [ObservableProperty]
    private int? _rssi;

    [ObservableProperty]
    private int? _highestRssi;

    [ObservableProperty]
    private string _radioType = string.Empty;

    [ObservableProperty]
    private NetworkType _networkType;

    [ObservableProperty]
    private AuthenticationType _authentication;

    [ObservableProperty]
    private EncryptionType _encryption;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstActiveDisplay))]
    private DateTime _firstSeen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastActiveDisplay))]
    private DateTime _lastSeen;

    [ObservableProperty]
    private double? _latitude;

    [ObservableProperty]
    private double? _longitude;

    [ObservableProperty]
    private int _lineNumber;

    [ObservableProperty]
    private string _basicTransferRates = string.Empty;

    [ObservableProperty]
    private string _otherTransferRates = string.Empty;

    public AccessPointViewModel(AccessPoint accessPoint)
    {
        UpdateFrom(accessPoint);
    }

    public void UpdateFrom(AccessPoint accessPoint)
    {
        // Don't overwrite ApId if we already have one
        if (ApId == 0) ApId = accessPoint.ApId;
        
        // Always update identity information properly
        // Check BSSID matching? We assume UpdateFrom is called on matching BSSID.
        if (string.IsNullOrEmpty(Bssid)) Bssid = accessPoint.Bssid;
        if (string.IsNullOrEmpty(Ssid) && !string.IsNullOrEmpty(accessPoint.Ssid)) Ssid = accessPoint.Ssid;
        
        // Manufacturer & Label - update if new value is better (non-empty and not Unknown)
        if (!string.IsNullOrEmpty(accessPoint.Manufacturer) && accessPoint.Manufacturer != "Unknown") 
            Manufacturer = accessPoint.Manufacturer;
            
        if (!string.IsNullOrEmpty(accessPoint.Label) && accessPoint.Label != "Unknown") Label = accessPoint.Label;

        Channel = accessPoint.Channel;
        FrequencyMhz = accessPoint.FrequencyMhz;
        Signal = accessPoint.Signal;
        Rssi = accessPoint.Rssi;
        RadioType = accessPoint.RadioType;
        NetworkType = accessPoint.NetworkType;
        
        // Critical: Do NOT overwrite known Auth/Encryption with Unknown
        if (accessPoint.Authentication != AuthenticationType.Unknown)
            Authentication = accessPoint.Authentication;
            
        if (accessPoint.Encryption != EncryptionType.Unknown)
            Encryption = accessPoint.Encryption;
            
        IsActive = accessPoint.IsActive;
        LastSeen = accessPoint.LastSeen;
        
        // GPS - Merge logic
        // If the new AP has GPS data, update it. 
        // If new AP has NO GPS data (e.g. indoor scan), keep old GPS? Usually yes.
        if (accessPoint.Latitude.HasValue && accessPoint.Longitude.HasValue)
        {
            Latitude = accessPoint.Latitude;
            Longitude = accessPoint.Longitude;
        }

        // Update highest signal if current is higher
        if (Signal > HighestSignal || HighestSignal == null)
            HighestSignal = Signal;


        if (Rssi > HighestRssi || HighestRssi == null)
            HighestRssi = Rssi;

        // Set FirstSeen if not already set
        if (FirstSeen == default)
            FirstSeen = accessPoint.FirstSeen;

        if (!string.IsNullOrEmpty(accessPoint.BasicTransferRates))
            BasicTransferRates = accessPoint.BasicTransferRates;
        if (!string.IsNullOrEmpty(accessPoint.OtherTransferRates))
            OtherTransferRates = accessPoint.OtherTransferRates;
    }

    public string DisplaySignal => Signal.HasValue ? $"{Signal}%" : "N/A";
    public string DisplayRssi   => Rssi.HasValue   ? $"{Rssi} dBm" : "N/A";
    public string HighRssiDisplay => HighestRssi.HasValue ? $"{HighestRssi} dBm" : "N/A";
    public string FrequencyDisplay => FrequencyMhz > 0 ? $"{FrequencyMhz} MHz" : "";
    public string LatitudeDisplay  => Latitude.HasValue  ? Latitude.Value.ToString("F6")  : "";
    public string LongitudeDisplay => Longitude.HasValue ? Longitude.Value.ToString("F6") : "";
    public string LatitudeDdmmssDisplay  => Latitude.HasValue  ? ToDdmmss(Latitude.Value,  isLat: true)  : "";
    public string LongitudeDdmmssDisplay => Longitude.HasValue ? ToDdmmss(Longitude.Value, isLat: false) : "";
    public string LatitudeDdmmmmDisplay  => Latitude.HasValue  ? ToDdmmm(Latitude.Value,  isLat: true)  : "";
    public string LongitudeDdmmmmDisplay => Longitude.HasValue ? ToDdmmm(Longitude.Value, isLat: false) : "";
    public string FirstActiveDisplay => FirstSeen == default ? "" : FirstSeen.ToString("yyyy-MM-dd HH:mm:ss");
    public string LastActiveDisplay  => LastSeen  == default ? "" : LastSeen.ToString("yyyy-MM-dd HH:mm:ss");
    public string NetworkTypeDisplay     => NetworkType.ToLegacyString();
    public string AuthenticationDisplay  => Authentication.ToLegacyString();
    public string EncryptionDisplay      => Encryption.ToLegacyString();
    public string ActiveDisplay          => IsActive ? "Active" : "Dead";

    // ── GPS format helpers ────────────────────────────────────────────────────

    private static string ToDdmmss(double deg, bool isLat)
    {
        char dir = isLat ? (deg >= 0 ? 'N' : 'S') : (deg >= 0 ? 'E' : 'W');
        deg = Math.Abs(deg);
        int d = (int)deg;
        double minFull = (deg - d) * 60.0;
        int m = (int)minFull;
        double s = (minFull - m) * 60.0;
        string ds = isLat ? $"{d:D2}" : $"{d:D3}";
        return $"{dir} {ds}{m:D2}{s:00.00}";
    }

    private static string ToDdmmm(double deg, bool isLat)
    {
        char dir = isLat ? (deg >= 0 ? 'N' : 'S') : (deg >= 0 ? 'E' : 'W');
        deg = Math.Abs(deg);
        int d = (int)deg;
        double mFull = (deg - d) * 60.0;
        string ds = isLat ? $"{d:D2}" : $"{d:D3}";
        return $"{dir} {ds}{mFull:00.0000}";
    }

    /// <summary>
    /// Signal history for this AP – newest first. Updated each scan cycle by MainViewModel.
    /// Bound to SignalGraphControl.SignalHistory.
    /// </summary>
    public ObservableCollection<SignalHistory> SignalHistoryItems { get; } = new();

    public void AddSignalHistoryEntry(SignalHistory entry)
    {
        SignalHistoryItems.Insert(0, entry);
        // Keep a reasonable cap so the list doesn't grow forever in memory
        while (SignalHistoryItems.Count > 500)
            SignalHistoryItems.RemoveAt(SignalHistoryItems.Count - 1);
    }
}
