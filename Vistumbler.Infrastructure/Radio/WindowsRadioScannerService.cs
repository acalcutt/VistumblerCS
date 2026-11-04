using System.Collections.Concurrent;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace Vistumbler.Infrastructure.Radio;

/// <summary>
/// Bluetooth devices (and, with a cellular modem, cell towers) heard alongside the Wi-Fi scan, through Windows' own
/// APIs: Bluetooth LE advertisements from a <see cref="BluetoothLEAdvertisementWatcher"/>, classic Bluetooth devices
/// from Windows' inquiry (a <see cref="DeviceWatcher"/> over Bluetooth endpoints, started again each time a round of
/// it ends). What's heard is reported once a second, a device once a batch with its latest signal.
/// </summary>
public sealed partial class WindowsRadioScannerService : IRadioScannerService, IDisposable
{
    public event EventHandler<IReadOnlyList<RadioNetwork>>? NetworksDetected;

    // Windows' protocol id for classic Bluetooth endpoints (the AEP selector's ProtocolId)
    private const string ClassicProtocolSelector = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
    private static readonly string[] ClassicProperties =
    {
        "System.Devices.Aep.DeviceAddress",
        "System.Devices.Aep.SignalStrength",
        "System.Devices.Aep.IsPaired",
        "System.Devices.Aep.Bluetooth.Cod.Major",
        "System.Devices.Aep.Bluetooth.Cod.Minor",
    };

    private BluetoothLEAdvertisementWatcher? _leWatcher;
    private DeviceWatcher? _classicWatcher;
    private readonly ConcurrentDictionary<string, DeviceInformation> _classicDevices = new();
    private Timer? _flushTimer;
    private volatile bool _running;

    // What's been heard since the last batch, by address
    private readonly ConcurrentDictionary<string, RadioNetwork> _heard = new(StringComparer.OrdinalIgnoreCase);
    // A BLE device's name comes in some advertisements and not others: the last one seen is kept
    private readonly ConcurrentDictionary<string, string> _leNames = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRunning => _running;

    public async Task<bool> IsBluetoothAvailableAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            return adapter is not null && (adapter.IsLowEnergySupported || adapter.IsClassicSupported);
        }
        catch
        {
            return false;   // no Bluetooth stack, or no access to it
        }
    }

    public void Start(bool bluetooth, IReadOnlyCollection<string> cellularModemIds)
    {
        Stop();
        _running = true;
        if (bluetooth)
        {
            StartLowEnergy();
            StartClassic();
        }
        StartCells(cellularModemIds);
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _running = false;
        _flushTimer?.Dispose();
        _flushTimer = null;
        if (_leWatcher is not null)
        {
            _leWatcher.Received -= OnAdvertisement;
            try { _leWatcher.Stop(); } catch { }
            _leWatcher = null;
        }
        if (_classicWatcher is not null)
        {
            var w = _classicWatcher;
            _classicWatcher = null;
            try { if (w.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) w.Stop(); } catch { }
        }
        StopCells();
        _heard.Clear();
    }

    public void Dispose() => Stop();

    private void Flush()
    {
        if (_heard.IsEmpty) return;
        var batch = new List<RadioNetwork>();
        foreach (var key in _heard.Keys)
            if (_heard.TryRemove(key, out var n)) batch.Add(n);
        if (batch.Count > 0) NetworksDetected?.Invoke(this, batch);
    }

    /// <summary>A cell tower read from the modem (WindowsRadioScannerService.Cells.cs), for the next batch.</summary>
    private void Heard(RadioNetwork n) => _heard[n.Key] = n;

    // ── Bluetooth LE ──────────────────────────────────────────────────────────

    private void StartLowEnergy()
    {
        try
        {
            _leWatcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            _leWatcher.Received += OnAdvertisement;
            _leWatcher.Start();
        }
        catch
        {
            _leWatcher = null;   // no LE radio, or it's switched off
        }
    }

    private void OnAdvertisement(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        string address = FormatAddress(args.BluetoothAddress);
        string name = args.Advertisement.LocalName ?? string.Empty;
        if (name.Length > 0) _leNames[address] = name;
        else _leNames.TryGetValue(address, out name!);

        var maker = args.Advertisement.ManufacturerData.FirstOrDefault();
        _heard[address] = new RadioNetwork
        {
            Key = address,
            Type = RadioNetworkType.BLE,
            Name = name ?? string.Empty,
            MfgrId = maker?.CompanyId,
            Rssi = args.RawSignalStrengthInDBm,
            HighestRssi = args.RawSignalStrengthInDBm,
        };
    }

    // ── Classic Bluetooth ─────────────────────────────────────────────────────

    private void StartClassic()
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(ClassicProtocolSelector, ClassicProperties, DeviceInformationKind.AssociationEndpoint);
            watcher.Added += (_, info) => { _classicDevices[info.Id] = info; ClassicHeard(info); };
            watcher.Updated += (_, update) =>
            {
                if (_classicDevices.TryGetValue(update.Id, out var info))
                {
                    info.Update(update);
                    ClassicHeard(info);
                }
            };
            // An inquiry round ends; another is started while scanning goes on, as Windows' own Bluetooth page does
            watcher.EnumerationCompleted += (w, _) => { try { w.Stop(); } catch { } };
            watcher.Stopped += (w, _) =>
            {
                if (_running && ReferenceEquals(w, _classicWatcher))
                    try { w.Start(); } catch { }
            };
            _classicWatcher = watcher;
            watcher.Start();
        }
        catch
        {
            _classicWatcher = null;
        }
    }

    private void ClassicHeard(DeviceInformation info)
    {
        // Paired devices are listed whether they're in range or not: only one with a signal was heard
        if (info.Properties.TryGetValue("System.Devices.Aep.SignalStrength", out var s) is false || s is not int rssi) return;
        if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) is false || a is not string address || address.Length == 0) return;
        address = address.ToUpperInvariant().Replace('-', ':');
        int major = info.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Cod.Major", out var mj) && mj is byte b1 ? b1 : 0;
        int minor = info.Properties.TryGetValue("System.Devices.Aep.Bluetooth.Cod.Minor", out var mn) && mn is byte b2 ? b2 : 0;
        bool paired = info.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var p) && p is true;

        _heard[address] = new RadioNetwork
        {
            Key = address,
            Type = RadioNetworkType.BT,
            Name = info.Name ?? string.Empty,
            // as WiGLE and VistumblerMAUI write it: the device class, then ';' and the bond state (10 none, 12 bonded)
            Capabilities = $"{MajorClassName(major)};{(paired ? 12 : 10)}",
            Frequency = (major << 8) | (minor << 2),   // the device class code
            Rssi = rssi,
            HighestRssi = rssi,
        };
    }

    // The Bluetooth Class of Device's major classes
    internal static string MajorClassName(int major) => major switch
    {
        1 => "Computer",
        2 => "Phone",
        3 => "Network",
        4 => "Audio/Video",
        5 => "Peripheral",
        6 => "Imaging",
        7 => "Wearable",
        8 => "Toy",
        9 => "Health",
        31 => "Uncategorized",
        _ => "Misc",
    };

    internal static string FormatAddress(ulong address)
        => string.Join(':', Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
}
