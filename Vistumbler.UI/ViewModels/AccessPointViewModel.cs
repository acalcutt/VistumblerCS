using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;

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
        ApId = accessPoint.ApId;
        Bssid = accessPoint.Bssid;
        Ssid = accessPoint.Ssid;
        Manufacturer = accessPoint.Manufacturer;
        Label = accessPoint.Label;
        Channel = accessPoint.Channel;
        Signal = accessPoint.Signal;
        Rssi = accessPoint.Rssi;
        RadioType = accessPoint.RadioType;
        NetworkType = accessPoint.NetworkType;
        Authentication = accessPoint.Authentication;
        Encryption = accessPoint.Encryption;
        IsActive = accessPoint.IsActive;
        LastSeen = accessPoint.LastSeen;
        Latitude = accessPoint.Latitude;
        Longitude = accessPoint.Longitude;

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
    public string NetworkTypeDisplay => NetworkType.ToString();
    public string AuthenticationDisplay => Authentication.ToString();
    public string EncryptionDisplay => Encryption.ToString();
    public string ActiveDisplay => IsActive ? "Active" : "Dead";
}
