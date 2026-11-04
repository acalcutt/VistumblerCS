using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Models;

namespace Vistumbler.UI.ViewModels;

/// <summary>
/// Cell towers and Bluetooth devices: the Bluetooth and Cell towers tabs beside the Wi-Fi list, kept apart from the
/// APs as their own tables are (VistumblerMAUI shows the same lists through a picker on its Scan page).
/// </summary>
public partial class MainViewModel
{
    public ObservableCollection<RadioNetworkViewModel> BluetoothDevices { get; } = new();
    public ObservableCollection<RadioNetworkViewModel> CellTowers { get; } = new();

    // Every row by its key, for a scan that hears one again
    private readonly Dictionary<string, RadioNetworkViewModel> _radioRows = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private string _bluetoothTabHeader = "Bluetooth";
    [ObservableProperty] private string _cellTabHeader = "Cell towers";

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
