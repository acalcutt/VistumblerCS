using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MapLibreNative.Maui.WPF;
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

    // Track which cell tile sources are currently visible (all 9 added/removed together)
    private readonly System.Collections.Generic.HashSet<string> _activeCellSources = new();

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

        // Show AP info popup when a WifiDB circle is clicked on the map
        MapHost.MapClicked += OnMapHostClicked;
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

    private void OnMapHostClicked(object? sender, MlnMapClickEventArgs e)
    {
        // Only query if at least one layer is active
        if (_activeWifiDbLayers.Count == 0 && _activeCellSources.Count == 0) return;

        // Each bucket maps to a single combined circle layer (sourceId itself).
        var circleLayerIds = _activeWifiDbLayers.Concat(_activeCellSources).ToArray();

        string? json = MapHost.QueryRenderedFeaturesInBox(e.ScreenX, e.ScreenY, thresholdPx: 6,
            layerIds: circleLayerIds);
        if (string.IsNullOrWhiteSpace(json)) return;

        // Parse the GeoJSON FeatureCollection returned by QueryRenderedFeaturesInBox
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;
            JsonElement features;
            if (root.ValueKind == JsonValueKind.Array)
                features = root;
            else if (root.TryGetProperty("features", out features) && features.ValueKind != JsonValueKind.Array)
                return;

            if (features.GetArrayLength() == 0) return;

            // Pick the first feature
            var feat  = features[0];
            if (!feat.TryGetProperty("properties", out var props)) return;

            var lines = new System.Text.StringBuilder();

            // Detect cell vs WiFi feature by presence of the `type` property (LTE/GSM/etc.)
            bool isCell = props.TryGetProperty("type", out var typeVal) &&
                          typeVal.ValueKind != JsonValueKind.Null &&
                          !props.TryGetProperty("sectype", out _);

            if (isCell)
            {
                AppendProp(lines, "Network",  props, "ssid");
                AppendProp(lines, "MAC/ID",   props, "mac");
                AppendProp(lines, "Type",     props, "type");
                AppendProp(lines, "Channel",  props, "chan");
                AppendProp(lines, "Auth",     props, "authmode");
                AppendProp(lines, "Points",   props, "points");
                AppendProp(lines, "RSSI",     props, "rssi");
                AppendProp(lines, "User",     props, "user");
                AppendProp(lines, "First Seen", props, "fa");
                AppendProp(lines, "Last Seen",  props, "la");
            }
            else
            {
                AppendProp(lines, "SSID",        props, "ssid");
                AppendProp(lines, "MAC",         props, "mac");
                AppendProp(lines, "Channel",     props, "chan");
                AppendProp(lines, "Auth",        props, "auth");
                AppendProp(lines, "Encrypt",     props, "encry");
                AppendProp(lines, "Manufacturer",props, "manuf");
                AppendProp(lines, "Net Type",    props, "nt");
                AppendProp(lines, "Radio",       props, "radio");
                AppendProp(lines, "First Seen",  props, "fa");
                AppendProp(lines, "Last Seen",   props, "la");
                AppendProp(lines, "High Signal", props, "high_gps_sig");
                AppendProp(lines, "High RSSI",   props, "high_gps_rssi");
                AppendProp(lines, "Alt",         props, "alt");
            }

            ApInfoText.Text = lines.ToString().TrimEnd('\n');

            // Placement="RelativePoint" with PlacementTarget=MapHost means offsets are
            // relative to MapHost's top-left corner in DIPs. Convert physical px → DIPs.
            var ps = PresentationSource.FromVisual(MapHost);
            double scaleX = ps?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double scaleY = ps?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
            ApInfoPopup.HorizontalOffset = e.ScreenX * scaleX;
            ApInfoPopup.VerticalOffset   = e.ScreenY * scaleY;
            ApInfoPopup.IsOpen = true;
        }
        catch { /* ignore malformed JSON */ }
    }

    private static void AppendProp(System.Text.StringBuilder sb, string label,
        JsonElement props, string key)
    {
        if (props.TryGetProperty(key, out var val) && val.ValueKind != JsonValueKind.Null)
            sb.AppendLine($"{label}: {val}");
    }


    // ── Cell Networks toggle button ────────────────────────────────────────────

    private static readonly string[] CellBuckets =
    [
        "cell_daily", "cell_weekly", "cell_monthly",
        "cell_0to1year", "cell_1to2year", "cell_2to3year",
        "cell_3to5year", "cell_5to10year", "cell_10yrplus",
    ];

    /// <summary>
    /// Toggles all 9 cell bucket layers on or off in one click.
    /// All buckets are loaded together so the button acts as a single group toggle.
    /// </summary>
    private void CellNetworksButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var viewModel  = (MainViewModel)DataContext!;
        string urlBase = viewModel.Settings.WifiDbUrl.TrimEnd('/');

        if (_activeCellSources.Count > 0)
        {
            // Remove all cell layers
            foreach (var bucket in CellBuckets)
            {
                string sourceId = "wifidb_" + bucket;
                MapHost.RemoveCellVectorLayer(sourceId, bucket);
                _activeCellSources.Remove(sourceId);
            }
            SetLayerButtonActive(btn, false);
        }
        else
        {
            // Add all cell layers
            foreach (var bucket in CellBuckets)
            {
                string sourceId    = "wifidb_" + bucket;
                string tileJsonUrl = $"{urlBase}/api/tilejson.php?bucket={bucket}";
                MapHost.SetCellVectorLayer(sourceId, tileJsonUrl, bucket);
                _activeCellSources.Add(sourceId);
            }
            SetLayerButtonActive(btn, true);
        }
    }


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

        // Map each button tag to its MVT bucket name — all buckets use the pre-generated
        // MVT tile endpoint so MapLibre only fetches tiles in view.
        // bucket = the layer name inside each MVT tile (matches tilejson.php/mvt.php).
        string? bucket = tag switch
        {
            "WifiDB_daily"    => "daily",
            "WifiDB_weekly"   => "weekly",
            "WifiDB_monthly"  => "monthly",
            "WifiDB_0to1year" => "0to1year",
            "WifiDB_1to2year" => "1to2year",
            "WifiDB_2to3year"  => "2to3year",
            "WifiDB_3to5year"  => "3to5year",
            "WifiDB_5to10year" => "5to10year",
            "WifiDB_10yrplus"  => "10yrplus",
            _                 => null,
        };

        if (bucket is null) return;

        if (_activeWifiDbLayers.Contains(sourceId))
        {
            MapHost.RemoveWifiVectorLayer(sourceId, bucket);
            _activeWifiDbLayers.Remove(sourceId);
            SetLayerButtonActive(btn, false);
        }
        else
        {
            string tileJsonUrl = $"{urlBase}/api/tilejson.php?bucket={bucket}";
            MapHost.SetWifiVectorLayer(sourceId, tileJsonUrl, bucket);
            _activeWifiDbLayers.Add(sourceId);
            SetLayerButtonActive(btn, true);
        }
    }

    // Marker prepended to a layer button's label while its layer(s) are shown.
    private const string ActiveLayerMarker = "■ ";

    /// <summary>
    /// Toggles a WifiDB/cell layer button's label between plain and active ("■ "-prefixed)
    /// state. The base label is taken from the button's current Content (the short name set in
    /// XAML), independent of its ToolTip — which now carries descriptive hover text. Also
    /// dismisses any open tooltip so it can't linger as a "ghost" over the map HwndHost when
    /// the Content changes underneath it.
    /// </summary>
    private static void SetLayerButtonActive(Button btn, bool active)
    {
        string label = btn.Content as string ?? string.Empty;
        if (label.StartsWith(ActiveLayerMarker, StringComparison.Ordinal))
            label = label[ActiveLayerMarker.Length..];
        btn.Content = active ? ActiveLayerMarker + label : label;

        // Toggling IsEnabled closes a currently-displayed tooltip; it remains available
        // for future hovers.
        ToolTipService.SetIsEnabled(btn, false);
        ToolTipService.SetIsEnabled(btn, true);
    }
}
