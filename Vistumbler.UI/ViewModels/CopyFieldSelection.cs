using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// Holds which fields are selected in the Copy dialog.
/// A single static instance persists selections between dialog openings,
/// mirroring the original Vistumbler behaviour where $Copy_* Dim variables
/// retained their state for the lifetime of the process.
/// </summary>
public partial class CopyFieldSelection : ObservableObject
{
    // ── Left column ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _lineNumber;
    [ObservableProperty] private bool _bssid;
    [ObservableProperty] private bool _ssid;
    [ObservableProperty] private bool _channel;
    [ObservableProperty] private bool _authentication;
    [ObservableProperty] private bool _encryption;
    [ObservableProperty] private bool _networkType;
    [ObservableProperty] private bool _radioType;
    [ObservableProperty] private bool _signal;
    [ObservableProperty] private bool _highSignal;
    [ObservableProperty] private bool _rssi;
    [ObservableProperty] private bool _highRssi;

    // ── Right column ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool _manufacturer;
    [ObservableProperty] private bool _label;
    [ObservableProperty] private bool _latitude;
    [ObservableProperty] private bool _longitude;
    [ObservableProperty] private bool _latitudeDms;
    [ObservableProperty] private bool _longitudeDms;
    [ObservableProperty] private bool _latitudeDmm;
    [ObservableProperty] private bool _longitudeDmm;
    [ObservableProperty] private bool _basicTransferRates;
    [ObservableProperty] private bool _otherTransferRates;
    [ObservableProperty] private bool _firstActive;
    [ObservableProperty] private bool _lastActive;
}
