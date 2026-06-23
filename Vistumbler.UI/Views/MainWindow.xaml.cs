using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Vistumbler.Core.Services;
using Vistumbler.UI.ViewModels;
using Vistumbler.UI.Extensions;

namespace Vistumbler.UI.Views;

public partial class MainWindow : Window
{
    private double _savedHeight;
    private double _savedMinHeight;

    // Track which WifiDB GeoJSON layers are currently visible (sourceId → on/off)
    private readonly System.Collections.Generic.HashSet<string> _activeWifiDbLayers = new();

    // Saved height of the map/graph row (Row 2) so it can be restored after hiding
    private double _mapGraphRowHeight = 300;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Settings.PropertyChanged += OnSettingsChanged;
        ApplyColumnSettings(viewModel.Settings);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _savedHeight = Height;
        _savedMinHeight = MinHeight;

        // Rebuild the Interface menu when the adapter list or active adapter changes
        viewModel.AvailableAdapters.CollectionChanged += (_, _) => RebuildInterfaceMenu();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveAdapter))
                RebuildInterfaceMenu();
        };

        // Rebuild the Filters menu when the filter list or active filter changes
        viewModel.AvailableFilters.CollectionChanged += (_, _) => RebuildFilterMenu();
        viewModel.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.ActiveFilterId))
                RebuildFilterMenu();
        };

        // Push live AP GeoJSON to the map after each scan cycle
        viewModel.LiveApGeoJsonUpdated += (_, geoJson) =>
        {
            if (viewModel.GraphMode == Vistumbler.Core.Enums.GraphMode.Map)
                MapHost.SetWifiGeoJsonLayerData("live_aps", geoJson);
        };

        // Update the location indicator on the map whenever a GPS fix arrives.
        // UpdateLocationIndicator handles the "style not yet loaded" case gracefully.
        viewModel.GpsLocationUpdated += (_, e) =>
            MapHost.UpdateLocationIndicator(e.Latitude, e.Longitude, e.Bearing, e.AccuracyMeters);

        // Remove the indicator when GPS is stopped
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsGpsActive) && !viewModel.IsGpsActive)
                MapHost.ClearLocationIndicator();
        };

        // Collapse/expand the map+graph row and GridSplitter row when switching modes
        void ApplyGraphRowVisibility()
        {
            bool show = viewModel.IsGraphVisible || viewModel.IsMapVisible;
            var outerGrid = (Grid)Content;
            if (show)
            {
                outerGrid.RowDefinitions[2].MinHeight = 60;
                outerGrid.RowDefinitions[2].Height    = new System.Windows.GridLength(_mapGraphRowHeight);
                outerGrid.RowDefinitions[3].Height    = new System.Windows.GridLength(5);
            }
            else
            {
                if (outerGrid.RowDefinitions[2].ActualHeight > 0)
                    _mapGraphRowHeight = outerGrid.RowDefinitions[2].ActualHeight;
                outerGrid.RowDefinitions[2].MinHeight = 0;
                outerGrid.RowDefinitions[2].Height    = new System.Windows.GridLength(0);
                outerGrid.RowDefinitions[3].Height    = new System.Windows.GridLength(0);
            }
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.GraphMode) or
                                  nameof(MainViewModel.IsMinimalGuiMode))
                ApplyGraphRowVisibility();
        };

        // Apply on load so the initial Hidden state collapses the rows immediately
        Loaded += (_, _) => ApplyGraphRowVisibility();
    }

    // ── Filter menu (dynamic filter items) ─────────────────────────────────────

    /// <summary>
    /// Rebuilds the filter items in the View > Filters menu. The first two XAML items
    /// (Add/Remove Filters, Refresh Filters) and the separator are preserved.
    /// Dynamic filter items are appended after the separator.
    /// </summary>
    private void RebuildFilterMenu()
    {
        var vm = (MainViewModel)DataContext!;

        // Static XAML items: index 0 = "Add / Remove Filters", 1 = "Refresh Filters", 2 = Separator
        const int staticCount = 3;
        while (FilterMenu.Items.Count > staticCount)
            FilterMenu.Items.RemoveAt(staticCount);

        foreach (var filter in vm.AvailableFilters)
        {
            var item = new MenuItem
            {
                Header      = filter.FiltName,
                IsCheckable = true,
                IsChecked   = filter.FiltId == vm.Settings.ActiveFilterId
            };
            item.Click += (_, _) => vm.SelectFilterCommand.Execute(filter);
            FilterMenu.Items.Add(item);
        }
    }

    // ── Interface menu (dynamic adapter items) ─────────────────────────────────

    /// <summary>
    /// Rebuilds the adapter items in the Interface menu. The first two items
    /// (Refresh Interfaces + Separator) are declared in XAML and kept in place.
    /// </summary>
    private void RebuildInterfaceMenu()
    {
        var vm = (MainViewModel)DataContext!;

        // Remove old adapter items (everything after the XAML separator at index 1)
        while (InterfaceMenu.Items.Count > 2)
            InterfaceMenu.Items.RemoveAt(2);

        foreach (var adapter in vm.AvailableAdapters)
        {
            var item = new MenuItem
            {
                Header      = adapter.Name,
                IsCheckable = true,
                IsChecked   = adapter == vm.ActiveAdapter
            };
            item.Click += (_, _) => vm.SelectAdapterCommand.Execute(adapter);
            InterfaceMenu.Items.Add(item);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsMinimalGuiMode)) return;
        var vm = (MainViewModel)DataContext!;
        if (vm.IsMinimalGuiMode)
        {
            _savedHeight = Height;
            _savedMinHeight = MinHeight;
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            MinHeight = _savedMinHeight;
            Height = _savedHeight;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SettingsViewModel s)
            ApplyColumnSettings(s);
    }

    private void ApplyColumnSettings(SettingsViewModel s)
    {
        ApplyCol(ColLineNumber,         s.ShowLineNumber,         s.LineNumberWidth);
        ApplyCol(ColActive,             s.ShowActive,             s.ActiveWidth);
        ApplyCol(ColSsid,               s.ShowSsid,               s.SsidWidth);
        ApplyCol(ColMacAddress,         s.ShowMacAddress,         s.MacAddressWidth);
        ApplyCol(ColSignal,             s.ShowSignal,             s.SignalWidth);
        ApplyCol(ColHighSignal,         s.ShowHighSignal,         s.HighSignalWidth);
        ApplyCol(ColRssi,               s.ShowRssi,               s.RssiWidth);
        ApplyCol(ColHighRssi,           s.ShowHighRssi,           s.HighRssiWidth);
        ApplyCol(ColAuthentication,     s.ShowAuthentication,     s.AuthenticationWidth);
        ApplyCol(ColEncryption,         s.ShowEncryption,         s.EncryptionWidth);
        ApplyCol(ColRadioType,          s.ShowRadioType,          s.RadioTypeWidth);
        ApplyCol(ColNetworkType,        s.ShowNetworkType,        s.NetworkTypeWidth);
        ApplyCol(ColChannel,            s.ShowChannel,            s.ChannelWidth);
        ApplyCol(ColFrequency,          s.ShowFrequency,          s.FrequencyWidth);
        ApplyCol(ColManufacturer,       s.ShowManufacturer,       s.ManufacturerWidth);
        ApplyCol(ColLabel,              s.ShowLabel,              s.LabelWidth);
        ApplyCol(ColLatitude,           s.ShowLatitude,           s.LatitudeWidth);
        ApplyCol(ColLongitude,          s.ShowLongitude,          s.LongitudeWidth);
        ApplyCol(ColLatitudeDdmmss,     s.ShowLatitudeDdmmss,     s.LatitudeDdmmssWidth);
        ApplyCol(ColLongitudeDdmmss,    s.ShowLongitudeDdmmss,    s.LongitudeDdmmssWidth);
        ApplyCol(ColLatitudeDdmmmm,     s.ShowLatitudeDdmmmm,     s.LatitudeDdmmmmWidth);
        ApplyCol(ColLongitudeDdmmmm,    s.ShowLongitudeDdmmmm,    s.LongitudeDdmmmmWidth);
        ApplyCol(ColBasicTransferRates, s.ShowBasicTransferRates, s.BasicTransferRatesWidth);
        ApplyCol(ColOtherTransferRates, s.ShowOtherTransferRates, s.OtherTransferRatesWidth);
        ApplyCol(ColFirstActive,        s.ShowFirstActive,        s.FirstActiveWidth);
        ApplyCol(ColLastActive,         s.ShowLastActive,         s.LastActiveWidth);
    }

    private static void ApplyCol(DataGridColumn col, bool visible, int width)
    {
        col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        col.Width = new DataGridLength(width);
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is TreeNodeViewModel node)
            vm.SelectedTreeviewNode = node;
    }

    // ── WifiDB layer buttons ──────────────────────────────────────────────────


    /// <summary>
    /// Toggles a WifiDB GeoJSON layer on/off. The button Tag property must
    /// be set to the WifiDB API function name (e.g. "exp_daily").
    /// Source id is derived from the function name (e.g. "wifidb_exp_daily").
    /// </summary>
    private void WifiDbLayerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        string tag      = btn.Tag?.ToString() ?? "";
        string sourceId = "wifidb_" + tag;
        var viewModel   = (MainViewModel)DataContext!;
        string urlBase  = viewModel.Settings.WifiDbUrl.TrimEnd('/');

        // Daily uses the API endpoint (last 36 hours, all users — no credentials needed).
        // All other layers use pre-generated static GeoJSON files.
        string url = tag switch
        {
            "WifiDB_daily"    => $"{urlBase}/api/geojson.php?func=exp_daily&json=1",
            "WifiDB_weekly"   => $"{urlBase}/out/geojson/WifiDB_weekly.json",
            "WifiDB_monthly"  => $"{urlBase}/out/geojson/WifiDB_monthly.json",
            "WifiDB_0to1year" => $"{urlBase}/out/geojson/WifiDB_0to1year.json",
            "WifiDB_1to2year" => $"{urlBase}/out/geojson/WifiDB_1to2year.json",
            "WifiDB_2to3year" => $"{urlBase}/out/geojson/WifiDB_2to3year.json",
            "WifiDB_Legacy"   => $"{urlBase}/out/geojson/WifiDB_Legacy.json",
            _                 => string.Empty,
        };

        if (string.IsNullOrEmpty(url)) return;

        if (_activeWifiDbLayers.Contains(sourceId))
        {
            MapHost.RemoveWifiGeoJsonLayer(sourceId);
            _activeWifiDbLayers.Remove(sourceId);
            btn.Content = btn.ToolTip;          // restore original label
        }
        else
        {
            MapHost.SetWifiGeoJsonLayer(sourceId, url);
            _activeWifiDbLayers.Add(sourceId);
            btn.Content = "\u25a0 " + btn.ToolTip; // filled square = active
        }
    }
}
