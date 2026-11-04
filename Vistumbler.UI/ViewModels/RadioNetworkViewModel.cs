using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// A cell tower or Bluetooth device as the Bluetooth and Cell towers tabs list it: one row, updated in place as it's
/// heard again while scanning.
/// </summary>
public partial class RadioNetworkViewModel : ViewModelBase
{
    public RadioNetworkViewModel(RadioNetwork network) => Update(network);

    public string Key { get; private set; } = string.Empty;
    public RadioNetworkType Type { get; private set; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _capabilities = string.Empty;
    [ObservableProperty] private string _manufacturer = string.Empty;
    [ObservableProperty] private int? _channel;
    [ObservableProperty] private int _frequency;
    [ObservableProperty] private int? _mfgrId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RssiDisplay))]
    private int _rssi;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighRssiDisplay))]
    private int _highestRssi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirstSeenDisplay))]
    private DateTime _firstSeen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSeenDisplay))]
    private DateTime _lastSeen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatitudeDisplay))]
    private double? _latitude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LongitudeDisplay))]
    private double? _longitude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveDisplay))]
    [NotifyPropertyChangedFor(nameof(RssiDisplay))]
    private bool _isActive;

    public bool IsBluetooth => Type is RadioNetworkType.BT or RadioNetworkType.BLE;

    public string TypeDisplay => Type switch
    {
        RadioNetworkType.BT => "Bluetooth",
        RadioNetworkType.BLE => "Bluetooth LE",
        RadioNetworkType.NR => "5G NR",
        _ => Type.ToString(),
    };

    /// <summary>The capabilities without the trailing ";code" (a classic device's bond state, a cell's operator code).</summary>
    public string CapabilitiesDisplay
    {
        get
        {
            int semicolon = Capabilities.LastIndexOf(';');
            return semicolon > 0 ? Capabilities[..semicolon] : Capabilities;
        }
    }

    public string NameDisplay => string.IsNullOrEmpty(Name) ? (IsBluetooth ? "(no name)" : Key) : Name;
    public string HighRssiDisplay => HighestRssi == int.MinValue ? "" : $"{HighestRssi} dBm";
    public string RssiDisplay => IsActive || Rssi != 0 ? $"{Rssi} dBm" : "";
    public string ChannelDisplay => Channel?.ToString() ?? "";
    public string MfgrIdDisplay => MfgrId is { } m and not 0 ? $"0x{m:X4}" : "";
    public string FirstSeenDisplay => FirstSeen == default ? "" : FirstSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string LastSeenDisplay => LastSeen == default ? "" : LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string LatitudeDisplay => Latitude?.ToString("F6") ?? "";
    public string LongitudeDisplay => Longitude?.ToString("F6") ?? "";
    public string ActiveDisplay => IsActive ? "Active" : "Dead";

    /// <summary>Takes a network's current values (a reload, or a scan that heard it again).</summary>
    public void Update(RadioNetwork n)
    {
        Key = n.Key;
        Type = n.Type;
        Name = n.Name;
        Capabilities = n.Capabilities;
        Manufacturer = n.Manufacturer;
        Channel = n.Channel;
        Frequency = n.Frequency;
        MfgrId = n.MfgrId;
        Rssi = n.Rssi;
        HighestRssi = n.HighestRssi;
        FirstSeen = n.FirstSeen;
        LastSeen = n.LastSeen;
        Latitude = n.Latitude;
        Longitude = n.Longitude;
        OnPropertyChanged(nameof(NameDisplay));
        OnPropertyChanged(nameof(CapabilitiesDisplay));
        OnPropertyChanged(nameof(RssiDisplay));
        OnPropertyChanged(nameof(ChannelDisplay));
        OnPropertyChanged(nameof(MfgrIdDisplay));
        OnPropertyChanged(nameof(TypeDisplay));
    }
}
