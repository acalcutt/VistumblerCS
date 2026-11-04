using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// Cell towers and Bluetooth devices: the Bluetooth and Cell towers tabs beside the Wi-Fi list, kept apart from the
/// APs as their own tables are (VistumblerMAUI shows the same lists through a picker on its Scan page). They're scanned
/// with the Wi-Fi when the Interface menu turns them on, and come in with imports.
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<RadioNetworkViewModel> BluetoothDevices { get; } = new();
    public ObservableCollection<RadioNetworkViewModel> CellTowers { get; } = new();

    // Every row by its key, for a scan that hears one again
    private readonly Dictionary<string, RadioNetworkViewModel> _radioRows = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private string _bluetoothTabHeader = "Bluetooth";
    [ObservableProperty] private string _cellTabHeader = "Cell towers";

    // ── Interface menu ────────────────────────────────────────────────────────

    /// <summary>Whether there's a Bluetooth radio for the Interface menu to offer.</summary>
    [ObservableProperty] private bool _isBluetoothAvailable;

    /// <summary>The cellular modems the Interface menu offers, any number of them ticked.</summary>
    public ObservableCollection<CellularModem> AvailableCellularModems { get; } = new();

    public IRelayCommand ToggleBluetoothCommand { get; private set; } = null!;
    public IRelayCommand<CellularModem> ToggleCellularModemCommand { get; private set; } = null!;

    private void InitializeRadio()
    {
        ToggleBluetoothCommand = new RelayCommand(() =>
        {
            _settings.ScanBluetooth = !_settings.ScanBluetooth;
            _settings.SaveSettings();
            RestartRadioIfScanning();
        });
        ToggleCellularModemCommand = new RelayCommand<CellularModem>(modem =>
        {
            if (modem is null) return;
            var ids = SelectedCellularModemIds().ToList();
            if (!ids.Remove(modem.Id)) ids.Add(modem.Id);
            _settings.CellularModemIds = string.Join('|', ids);
            _settings.SaveSettings();
            RestartRadioIfScanning();
        });
        _radioScanner.NetworksDetected += OnRadioNetworksDetected;
    }

    /// <summary>The modems ticked in the Interface menu (the setting keeps them joined by '|').</summary>
    public IReadOnlyList<string> SelectedCellularModemIds()
        => _settings.CellularModemIds.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task LoadRadioInterfacesAsync()
    {
        bool bluetooth = await _radioScanner.IsBluetoothAvailableAsync();
        var modems = await _radioScanner.GetCellularModemsAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableCellularModems.Clear();
            foreach (var m in modems) AvailableCellularModems.Add(m);
            IsBluetoothAvailable = bluetooth;
        });
    }

    // ── Scanning ──────────────────────────────────────────────────────────────

    // A device not heard for this long is marked dead. Longer than for Wi-Fi: a round of classic Bluetooth inquiry
    // takes 10 s or more, and phones advertise LE only every few seconds.
    private static readonly TimeSpan RadioDeadAfter = TimeSpan.FromSeconds(30);

    // Heard but not saved yet: saved by the Wi-Fi handler at the end of each scan, on its flow, so the two never write
    // to the database at once
    private readonly ConcurrentQueue<RadioNetwork> _radioToSave = new();

    private void StartRadio()
    {
        var modems = SelectedCellularModemIds().Where(id => AvailableCellularModems.Any(m => m.Id == id)).ToList();
        bool bluetooth = _settings.ScanBluetooth && IsBluetoothAvailable;
        if (bluetooth || modems.Count > 0)
            _radioScanner.Start(bluetooth, modems);
    }

    private void StopRadio() => _radioScanner.Stop();

    private void RestartRadioIfScanning()
    {
        if (!IsScanning) return;
        StopRadio();
        StartRadio();
    }

    private void OnRadioNetworksDetected(object? sender, IReadOnlyList<RadioNetwork> heard)
    {
        var now = DateTime.UtcNow;
        var gps = IsGpsActive ? _gpsService.CurrentGpsData : null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var n in heard)
            {
                n.FirstSeen = n.LastSeen = now;
                if (gps is not null)
                {
                    n.Latitude = gps.Latitude;
                    n.Longitude = gps.Longitude;
                }
                if (_radioRows.TryGetValue(n.Key, out var row)) KeepWhatsKnown(n, row);
                ShowRadioNetwork(n).IsActive = true;
                _radioToSave.Enqueue(n);
            }
        });
    }

    /// <summary>What a row already shows that this hearing of it doesn't say (a BLE name, the strongest reading).</summary>
    private static void KeepWhatsKnown(RadioNetwork n, RadioNetworkViewModel row)
    {
        if (row.FirstSeen != default) n.FirstSeen = row.FirstSeen;
        if (n.Name.Length == 0) n.Name = row.Name;
        if (n.Capabilities.Length == 0) n.Capabilities = row.Capabilities;
        if (n.Manufacturer.Length == 0) n.Manufacturer = row.Manufacturer;
        n.MfgrId ??= row.MfgrId;
        if (row.HighestRssi > n.HighestRssi)
        {
            n.HighestRssi = row.HighestRssi;
            n.Latitude = row.Latitude;
            n.Longitude = row.Longitude;
        }
        else if (n.Latitude is null)
        {
            n.Latitude = row.Latitude;
            n.Longitude = row.Longitude;
        }
    }

    /// <summary>
    /// Saves what's been heard since the last scan, with a reading at the current GPS fix, and marks dead the devices
    /// not heard for a while. Called at the end of each Wi-Fi scan.
    /// </summary>
    private async Task SaveHeardRadioNetworksAsync()
    {
        if (!_radioToSave.IsEmpty)
        {
            int? gpsId = null;
            if (IsGpsActive && _gpsService.CurrentGpsData is { } gps)
                gpsId = await _databaseService.AddGpsDataAsync(gps);

            while (_radioToSave.TryDequeue(out var n))
            {
                // A classic device's address is its maker's; an LE one's is mostly random, its maker in MfgrId instead
                if (n.Type == RadioNetworkType.BT && n.Manufacturer.Length == 0)
                {
                    var prefix = NormalizeMacPrefix(n.Key);
                    if (!_manufacturerCache.TryGetValue(prefix, out var manufacturer))
                    {
                        manufacturer = await _databaseService.GetManufacturerAsync(prefix);
                        _manufacturerCache[prefix] = manufacturer;
                    }
                    if (!string.IsNullOrEmpty(manufacturer))
                    {
                        n.Manufacturer = manufacturer;
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_radioRows.TryGetValue(n.Key, out var row)) row.Manufacturer = manufacturer;
                        });
                    }
                }

                int id = await _databaseService.UpsertRadioNetworkAsync(n);
                await _databaseService.AddRadioReadingAsync(new RadioReading
                {
                    NetworkId = id,
                    GpsId = gpsId,
                    Rssi = n.Rssi,
                    Timestamp = n.LastSeen,
                });
            }
        }

        var now = DateTime.UtcNow;
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var row in _radioRows.Values)
            {
                if (row.IsActive && now - row.LastSeen > RadioDeadAfter)
                {
                    row.IsActive = false;
                    row.Rssi = 0;
                }
            }
        });
    }

    // ── The lists ─────────────────────────────────────────────────────────────

    /// <summary>Lists what's stored (after an import), as the Wi-Fi list is reloaded.</summary>
    private async Task LoadRadioNetworksFromDatabaseAsync()
    {
        var networks = await _databaseService.GetAllRadioNetworksAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            ClearRadioNetworkLists();
            foreach (var n in networks) ShowRadioNetwork(n);
        });
    }

    /// <summary>Adds a network's row, or updates the one it has. Call on the UI thread.</summary>
    private RadioNetworkViewModel ShowRadioNetwork(RadioNetwork n)
    {
        if (_radioRows.TryGetValue(n.Key, out var row))
        {
            row.Update(n);
            return row;
        }
        row = new RadioNetworkViewModel(n);
        _radioRows[n.Key] = row;
        (row.IsBluetooth ? BluetoothDevices : CellTowers).Add(row);
        UpdateRadioTabHeaders();
        return row;
    }

    private void ClearRadioNetworkLists()
    {
        _radioRows.Clear();
        BluetoothDevices.Clear();
        CellTowers.Clear();
        UpdateRadioTabHeaders();
    }

    private void UpdateRadioTabHeaders()
    {
        BluetoothTabHeader = BluetoothDevices.Count > 0 ? $"Bluetooth ({BluetoothDevices.Count})" : "Bluetooth";
        CellTabHeader = CellTowers.Count > 0 ? $"Cell towers ({CellTowers.Count})" : "Cell towers";
    }
}
