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
    private DateTime _firstSeen;

    [ObservableProperty]
    private DateTime _lastSeen;

    [ObservableProperty]
    private double? _latitude;

    [ObservableProperty]
    private double? _longitude;

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
        
        // Manufacturer & Label - Prefer existing if new is empty/unknown
        if (string.IsNullOrEmpty(Manufacturer) && !string.IsNullOrEmpty(accessPoint.Manufacturer) && accessPoint.Manufacturer != "Unknown") 
            Manufacturer = accessPoint.Manufacturer;
            
        if (!string.IsNullOrEmpty(accessPoint.Label) && accessPoint.Label != "Unknown") Label = accessPoint.Label;

        Channel = accessPoint.Channel;
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
    }

    public string DisplaySignal => Signal.HasValue ? $"{Signal}%" : "N/A";
    public string DisplayRssi => Rssi.HasValue ? $"{Rssi} dBm" : "N/A";
    public string NetworkTypeDisplay => NetworkType.ToLegacyString();
    public string AuthenticationDisplay => Authentication.ToLegacyString();
    public string EncryptionDisplay => Encryption.ToLegacyString();
    public string ActiveDisplay => IsActive ? "Active" : "Dead";

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
