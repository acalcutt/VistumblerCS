using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Vistumbler.Core.Enums;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IWiFiScannerService _wifiScanner;
    private readonly IGpsService _gpsService;
    private readonly IDatabaseService _databaseService;
    private readonly IImportService _importService;
    private readonly IExportService _exportService;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _scanCancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<AccessPointViewModel> _accessPoints = new();

    [ObservableProperty]
    private AccessPointViewModel? _selectedAccessPoint;

    // ── NetStumbler-style filter treeview ─────────────────────────────────────

    /// <summary>The five category root nodes shown in the left-hand treeview.</summary>
    public ObservableCollection<TreeNodeViewModel> TreeviewRoots { get; } = new();

    [ObservableProperty]
    private TreeNodeViewModel? _selectedTreeviewNode;

    // The five fixed root nodes
    private readonly TreeNodeViewModel _authRoot       = new() { Name = "Authentication" };
    private readonly TreeNodeViewModel _channelRoot    = new() { Name = "Channel" };
    private readonly TreeNodeViewModel _encryptionRoot = new() { Name = "Encryption" };
    private readonly TreeNodeViewModel _networkRoot    = new() { Name = "Network Type" };
    private readonly TreeNodeViewModel _ssidRoot       = new() { Name = "SSID" };

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isGpsActive;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private int _activeApCount;

    [ObservableProperty]
    private string? _currentLatitude;

    [ObservableProperty]
    private string? _currentLongitude;

    [ObservableProperty]
    private double _loopTime;

    // ── Graph ────────────────────────────────────────────────────────

    [ObservableProperty]
    private GraphMode _graphMode = GraphMode.Hidden;

    [ObservableProperty]
    private bool _useRssiInGraphs = false;

    [ObservableProperty]
    private bool _graphDeadTime = true;

    public bool IsGraphVisible => GraphMode != GraphMode.Hidden;

    partial void OnGraphModeChanged(GraphMode value) => OnPropertyChanged(nameof(IsGraphVisible));

    partial void OnSelectedAccessPointChanged(AccessPointViewModel? value)
    {
        ((RelayCommand)OpenSignalHistoryCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CopyApInfoCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddManufacturerCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddLabelCommand).NotifyCanExecuteChanged();
        ((RelayCommand)OpenGeonamesCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)FindApInWifiDbCommand).NotifyCanExecuteChanged();
    }

    public ICommand StartScanCommand { get; }
    public ICommand StopScanCommand { get; }
    public ICommand StartGpsCommand { get; }
    public ICommand StopGpsCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenImportWindowCommand { get; }
    public ICommand ExportVs1Command { get; }
    public ICommand ExportVszCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportKmlCommand { get; }
    public ICommand ExportGpxCommand { get; }
    public ICommand ExportNs1Command { get; }
    public ICommand ExportNetXmlCommand { get; }
    public ICommand ExportKismetDbCommand { get; }
    public ICommand UpdateManufacturersCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenSettingsAtTabCommand { get; }
    public ICommand ToggleGraph1Command { get; }
    public ICommand ToggleGraph2Command { get; }
    public ICommand ToggleUseRssiCommand { get; }
    public ICommand ToggleGraphDeadTimeCommand { get; }
    public ICommand OpenSignalHistoryCommand { get; }

    // ── Context-menu commands (right-click on AP row) ─────────────────────
    public ICommand CopyApInfoCommand { get; }
    public ICommand AddManufacturerCommand { get; }
    public ICommand AddLabelCommand { get; }
    public ICommand OpenGeonamesCommand { get; }
    public ICommand FindApInWifiDbCommand { get; }

    public MainViewModel(
        IWiFiScannerService wifiScanner,
        IGpsService gpsService,
        IDatabaseService databaseService,
        IImportService importService,
        IExportService exportService,
        IServiceProvider serviceProvider)
    {
        _wifiScanner = wifiScanner;
        _gpsService = gpsService;
        _databaseService = databaseService;
        _importService = importService;
        _exportService = exportService;
        _serviceProvider = serviceProvider;

        // Populate treeview root nodes (order matches original Vistumbler)
        TreeviewRoots.Add(_authRoot);
        TreeviewRoots.Add(_channelRoot);
        TreeviewRoots.Add(_encryptionRoot);
        TreeviewRoots.Add(_networkRoot);
        TreeviewRoots.Add(_ssidRoot);

        // Subscribe to events
        _wifiScanner.AccessPointsDetected += OnAccessPointsDetected;
        _wifiScanner.ScanError += OnScanError;
        _gpsService.GpsDataReceived += OnGpsDataReceived;
        _gpsService.GpsError += OnGpsError;

        // Initialize commands
        StartScanCommand = new AsyncRelayCommand(StartScanAsync, () => !IsScanning);
        StopScanCommand = new RelayCommand(StopScan, () => IsScanning);
        StartGpsCommand = new AsyncRelayCommand(StartGpsAsync, () => !IsGpsActive);
        StopGpsCommand = new RelayCommand(StopGps, () => IsGpsActive);
        ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
        ExitCommand = new RelayCommand(Exit);
        OpenImportWindowCommand = new RelayCommand(OpenImportWindow);
        ExportVs1Command = new AsyncRelayCommand(ExportVs1);
        ExportVszCommand = new AsyncRelayCommand(ExportVsz);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsv);
        ExportKmlCommand = new AsyncRelayCommand(ExportKml);
        ExportGpxCommand = new AsyncRelayCommand(ExportGpx);
        ExportNs1Command = new AsyncRelayCommand(ExportNs1);
        ExportNetXmlCommand    = new AsyncRelayCommand(ExportNetXml);
        ExportKismetDbCommand      = new AsyncRelayCommand(ExportKismetDb);
        UpdateManufacturersCommand = new AsyncRelayCommand(UpdateManufacturersAsync);
        OpenSettingsCommand        = new RelayCommand(OpenSettingsWindow);
        OpenSettingsAtTabCommand = new RelayCommand<string>(s => OpenSettingsWindowAt(int.TryParse(s, out int i) ? i : 0));
        ToggleGraph1Command     = new RelayCommand(() => GraphMode = GraphMode == GraphMode.Line   ? GraphMode.Hidden : GraphMode.Line);
        ToggleGraph2Command     = new RelayCommand(() => GraphMode = GraphMode == GraphMode.Bar    ? GraphMode.Hidden : GraphMode.Bar);
        ToggleUseRssiCommand    = new RelayCommand(() => UseRssiInGraphs = !UseRssiInGraphs);
        ToggleGraphDeadTimeCommand = new RelayCommand(() => GraphDeadTime = !GraphDeadTime);
        OpenSignalHistoryCommand = new RelayCommand(OpenSignalHistoryWindow, () => SelectedAccessPoint != null);
        CopyApInfoCommand     = new RelayCommand(CopyApInfo,         () => SelectedAccessPoint != null);
        AddManufacturerCommand = new AsyncRelayCommand(AddManufacturerAsync, () => SelectedAccessPoint != null);
        AddLabelCommand       = new AsyncRelayCommand(AddLabelAsync,        () => SelectedAccessPoint != null);
        OpenGeonamesCommand   = new RelayCommand(OpenGeonames,       () => SelectedAccessPoint != null);
        FindApInWifiDbCommand = new AsyncRelayCommand(FindApInWifiDb, () => SelectedAccessPoint != null);

        // Initialize database
        InitializeDatabaseAsync();
    }

    private async void InitializeDatabaseAsync()
    {
        try
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Vistumbler",
                "vistumbler.db");

            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            await _databaseService.InitializeAsync(dbPath);
            
            StatusMessage = "Database initialized";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Database error: {ex.Message}";
        }
    }

    private async Task StartScanAsync()
    {
        try
        {
            IsScanning = true;
            _scanCancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "Scanning for networks...";
            
            await _wifiScanner.StartScanningAsync(_scanCancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
            IsScanning = false;
        }
    }

    private void StopScan()
    {
        _scanCancellationTokenSource?.Cancel();
        _wifiScanner.StopScanning();
        IsScanning = false;
        StatusMessage = "Scanning stopped";
    }

    private async Task StartGpsAsync()
    {
        try
        {
            var config = new GpsConfiguration
            {
                ComPort = "COM4",
                BaudRate = 4800
            };

            IsGpsActive = true;
            StatusMessage = "Starting GPS...";
            
            await _gpsService.StartAsync(config);
            StatusMessage = "GPS active";
        }
        catch (Exception ex)
        {
            StatusMessage = $"GPS error: {ex.Message}";
            IsGpsActive = false;
        }
    }

    private void StopGps()
    {
        _gpsService.Stop();
        IsGpsActive = false;
        StatusMessage = "GPS stopped";
    }

    private async Task ClearAllAsync()
    {
        var result = MessageBox.Show(
            "Are you sure you want to clear all access points?",
            "Clear All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await _databaseService.ClearAllAccessPointsAsync();
            AccessPoints.Clear();
            ClearTreeview();
            ActiveApCount = 0;
            StatusMessage = "All access points cleared";
        }
    }

    // ── Context-menu handlers ────────────────────────────────────────────────

    // Persisted between dialog openings (mirrors Vistumbler $Copy_* Dim variables)
    private static readonly CopyFieldSelection _copyFields = new();

    private void CopyApInfo()
    {
        if (SelectedAccessPoint is not { } ap) return;

        var dialog = new Views.CopyApDialog(_copyFields)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        var parts = new List<string>();

        void Add(string value) => parts.Add(value);

        if (_copyFields.LineNumber)       Add(ap.ApId.ToString());
        if (_copyFields.Bssid)            Add(ap.Bssid);
        if (_copyFields.Ssid)             Add(ap.Ssid);
        if (_copyFields.Channel)          Add(ap.Channel.ToString());
        if (_copyFields.Authentication)   Add(ap.AuthenticationDisplay);
        if (_copyFields.Encryption)       Add(ap.EncryptionDisplay);
        if (_copyFields.NetworkType)      Add(ap.NetworkTypeDisplay);
        if (_copyFields.RadioType)        Add(ap.RadioType);
        if (_copyFields.Signal)           Add(ap.DisplaySignal);
        if (_copyFields.HighSignal)       Add(ap.HighestSignal.HasValue ? $"{ap.HighestSignal}%" : "N/A");
        if (_copyFields.Rssi)             Add(ap.DisplayRssi);
        if (_copyFields.HighRssi)         Add(ap.HighestRssi.HasValue ? $"{ap.HighestRssi} dBm" : "N/A");
        if (_copyFields.Manufacturer)     Add(ap.Manufacturer);
        if (_copyFields.Label)            Add(ap.Label);

        if (_copyFields.Latitude)
            Add(ap.Latitude.HasValue  ? ap.Latitude.Value.ToString("F6")  : "");
        if (_copyFields.Longitude)
            Add(ap.Longitude.HasValue ? ap.Longitude.Value.ToString("F6") : "");
        if (_copyFields.LatitudeDms)
            Add(ap.Latitude.HasValue  ? GpsToDms(ap.Latitude.Value,  isLat: true)  : "");
        if (_copyFields.LongitudeDms)
            Add(ap.Longitude.HasValue ? GpsToDms(ap.Longitude.Value, isLat: false) : "");
        if (_copyFields.LatitudeDmm)
            Add(ap.Latitude.HasValue  ? GpsToDmm(ap.Latitude.Value,  isLat: true)  : "");
        if (_copyFields.LongitudeDmm)
            Add(ap.Longitude.HasValue ? GpsToDmm(ap.Longitude.Value, isLat: false) : "");

        if (_copyFields.BasicTransferRates)  Add("");   // not yet in model
        if (_copyFields.OtherTransferRates)  Add("");   // not yet in model

        if (_copyFields.FirstActive)  Add(ap.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"));
        if (_copyFields.LastActive)   Add(ap.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"));

        if (parts.Count == 0) return;

        System.Windows.Clipboard.SetText(string.Join("|", parts));
        StatusMessage = $"Copied {parts.Count} field(s) for {ap.Bssid}";
    }

    // Decimal degrees → Degrees Minutes Seconds  (e.g. "N 51°30'26.5\"")
    private static string GpsToDms(double dd, bool isLat)
    {
        var hem = isLat ? (dd >= 0 ? "N" : "S") : (dd >= 0 ? "E" : "W");
        var abs = Math.Abs(dd);
        var d   = (int)abs;
        var m   = (int)((abs - d) * 60);
        var s   = ((abs - d) * 60 - m) * 60;
        return FormattableString.Invariant($"{hem} {d:D2}°{m:D2}'{s:F1}\"");
    }

    // Decimal degrees → Degrees Decimal Minutes  (e.g. "N 5130.4417")
    private static string GpsToDmm(double dd, bool isLat)
    {
        var hem = isLat ? (dd >= 0 ? "N" : "S") : (dd >= 0 ? "E" : "W");
        var abs = Math.Abs(dd);
        var d   = (int)abs;
        var m   = (abs - d) * 60;
        return FormattableString.Invariant($"{hem} {d:D2}{m:F4}");
    }

    private async Task AddManufacturerAsync()
    {
        if (SelectedAccessPoint is not { } ap) return;
        var macPrefix = ap.Bssid.Length >= 8 ? ap.Bssid[..8].Replace(":", "").ToUpperInvariant() : ap.Bssid;
        var dialog = new Views.InputDialog("Add Manufacturer",
            $"Enter manufacturer name for {ap.Bssid}:", ap.Manufacturer)
        { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        await _databaseService.UpsertManufacturerAsync(macPrefix, dialog.Value.Trim());
        ap.Manufacturer = dialog.Value.Trim();
        StatusMessage = $"Manufacturer updated for {ap.Bssid}";
    }

    private async Task AddLabelAsync()
    {
        if (SelectedAccessPoint is not { } ap) return;
        var dialog = new Views.InputDialog("Add Label",
            $"Enter label for {ap.Ssid} ({ap.Bssid}):", ap.Label)
        { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        await _databaseService.UpsertLabelAsync(ap.Bssid, dialog.Value.Trim());
        ap.Label = dialog.Value.Trim();
        StatusMessage = $"Label updated for {ap.Bssid}";
    }

    private void OpenGeonames()
    {
        // Mirrors _GeonamesInfo() in Vistumbler.au3 — shows stored geonames fields from the AP record.
        // The fields (CountryCode, CountryName, AdminName, etc.) are populated by the WifiDB locate API
        // and will be added to the model in a future update.  For now show what is available.
        if (SelectedAccessPoint is not { } ap) return;
        var msg = $"BSSID: {ap.Bssid}\nSSID: {ap.Ssid}\n\n" +
                  "Country Code: Not Available\n" +
                  "Country Name: Not Available\n" +
                  "Admin Code: Not Available\n" +
                  "Admin Name: Not Available\n" +
                  "Admin2 Name: Not Available\n" +
                  "Area Name: Not Available\n" +
                  "Accuracy (miles): Not Available\n" +
                  "Accuracy (km): Not Available\n\n" +
                  "(Geonames data is populated after a successful WifiDB locate lookup.)";
        MessageBox.Show(msg, "Geonames Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Shared HttpClient — reused across calls (best practice)
    private static readonly System.Net.Http.HttpClient _httpClient = new();

    private async Task FindApInWifiDb()
    {
        // Mirrors _LocateAPInWifidb() in Vistumbler.au3:
        // POSTs to https://api.wifidb.net/import.php and shows the result.
        if (SelectedAccessPoint is not { } ap) return;

        const string apiUrl = "https://api.wifidb.net/import.php";
        StatusMessage = $"Searching WifiDB for {ap.Bssid}\u2026";

        try
        {
            using var content = new System.Net.Http.MultipartFormDataContent();
            content.Add(new System.Net.Http.StringContent(ap.Ssid),                "ssid");
            content.Add(new System.Net.Http.StringContent(ap.Bssid),               "mac");
            content.Add(new System.Net.Http.StringContent(ap.RadioType),           "radio");
            content.Add(new System.Net.Http.StringContent(ap.Channel.ToString()),  "chan");
            content.Add(new System.Net.Http.StringContent(ap.AuthenticationDisplay), "auth");
            content.Add(new System.Net.Http.StringContent(ap.EncryptionDisplay),   "encry");

            var response = await _httpClient.PostAsync(apiUrl, content);
            var body     = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(body))
            {
                MessageBox.Show("No results returned from WifiDB.", "Find AP in WifiDB",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(body, $"WifiDB — {ap.Bssid}",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            StatusMessage = $"WifiDB lookup complete for {ap.Bssid}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WifiDB request failed:\n{ex.Message}", "WifiDB Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "WifiDB lookup failed";
        }
    }

    private void OpenSettingsWindow() => OpenSettingsWindowAt(0);

    private void OpenSettingsWindowAt(int tabIndex = 0)
    {
        var vm = _serviceProvider.GetRequiredService<SettingsViewModel>();
        vm.SelectedTabIndex = tabIndex;
        var window = new Views.SettingsWindow(vm) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private void OpenSignalHistoryWindow()
    {
        if (SelectedAccessPoint == null) return;
        var window = new Views.SignalHistoryWindow(SelectedAccessPoint)
        {
            Owner = Application.Current.MainWindow
        };
        window.Show();
    }

    private void Exit()
    {
        Application.Current.Shutdown();
    }

    // ── Treeview selection handler ────────────────────────────────────────────

    /// <summary>
    /// When a leaf (AP) node is selected in the treeview, sync the DataGrid selection.
    /// </summary>
    partial void OnSelectedTreeviewNodeChanged(TreeNodeViewModel? value)
    {
        if (value?.AccessPoint is { } ap)
            SelectedAccessPoint = ap;
    }

    // ── Treeview manipulation helpers ─────────────────────────────────────────

    /// <summary>
    /// Adds an AP to all five category trees.
    /// Must be called on the UI thread (inside Dispatcher.Invoke).
    /// </summary>
    private void AddApToTreeview(AccessPointViewModel ap)
    {
        AddApToCategory(_authRoot,       ap.AuthenticationDisplay, ap);
        AddApToCategory(_channelRoot,    $"{ap.Channel:000}",      ap);
        AddApToCategory(_encryptionRoot, ap.EncryptionDisplay,     ap);
        AddApToCategory(_networkRoot,    ap.NetworkTypeDisplay,    ap);
        AddApToCategory(_ssidRoot,       string.IsNullOrEmpty(ap.Ssid) ? "<hidden>" : ap.Ssid, ap);
    }

    private static void AddApToCategory(TreeNodeViewModel root, string groupKey, AccessPointViewModel ap)
    {
        var group = root.Children.FirstOrDefault(c => c.GroupKey == groupKey);
        if (group == null)
        {
            group = new TreeNodeViewModel { Name = groupKey, GroupKey = groupKey };
            // Insert in sorted order
            int insertAt = 0;
            while (insertAt < root.Children.Count &&
                   string.Compare(root.Children[insertAt].GroupKey, groupKey, StringComparison.OrdinalIgnoreCase) < 0)
                insertAt++;
            root.Children.Insert(insertAt, group);
        }

        // Avoid duplicate leaves (e.g. after a reload)
        if (!group.Children.Any(c => c.AccessPoint?.Bssid == ap.Bssid))
        {
            group.Children.Add(new TreeNodeViewModel
            {
                Name        = string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : $"{ap.Ssid} ({ap.Bssid})",
                GroupKey    = groupKey,
                AccessPoint = ap
            });
        }
    }

    /// <summary>
    /// Updates an existing AP's leaf nodes in-place.
    /// Only moves a leaf to a different group when the category key actually changed
    /// (e.g. the channel or auth type was updated). For normal signal updates nothing
    /// is moved, preventing constant visual reordering on every scan loop.
    /// Must be called on the UI thread.
    /// </summary>
    private void UpdateApInTreeview(AccessPointViewModel ap)
    {
        UpdateApInCategory(_authRoot,       ap.AuthenticationDisplay,                                ap);
        UpdateApInCategory(_channelRoot,    $"{ap.Channel:000}",                                    ap);
        UpdateApInCategory(_encryptionRoot, ap.EncryptionDisplay,                                   ap);
        UpdateApInCategory(_networkRoot,    ap.NetworkTypeDisplay,                                  ap);
        UpdateApInCategory(_ssidRoot,       string.IsNullOrEmpty(ap.Ssid) ? "<hidden>" : ap.Ssid,  ap);
    }

    private static void UpdateApInCategory(TreeNodeViewModel root, string newGroupKey, AccessPointViewModel ap)
    {
        // Locate the existing leaf for this AP
        TreeNodeViewModel? existingGroup = null;
        TreeNodeViewModel? existingLeaf  = null;

        foreach (var group in root.Children)
        {
            var leaf = group.Children.FirstOrDefault(c => c.AccessPoint?.Bssid == ap.Bssid);
            if (leaf != null)
            {
                existingGroup = group;
                existingLeaf  = leaf;
                break;
            }
        }

        if (existingLeaf == null)
        {
            // AP not yet in this tree – just add it
            AddApToCategory(root, newGroupKey, ap);
            return;
        }

        // Always keep the display name current (SSID can be discovered after first scan)
        existingLeaf.Name = string.IsNullOrEmpty(ap.Ssid) ? ap.Bssid : $"{ap.Ssid} ({ap.Bssid})";

        // Group key unchanged – nothing structural to do
        if (existingGroup!.GroupKey == newGroupKey)
            return;

        // Category value changed – move the leaf to the correct group
        existingGroup.Children.Remove(existingLeaf);
        if (existingGroup.Children.Count == 0)
            root.Children.Remove(existingGroup);

        AddApToCategory(root, newGroupKey, ap);
    }

    private void RemoveApFromTreeview(AccessPointViewModel ap)
    {
        RemoveApFromCategory(_authRoot,       ap);
        RemoveApFromCategory(_channelRoot,    ap);
        RemoveApFromCategory(_encryptionRoot, ap);
        RemoveApFromCategory(_networkRoot,    ap);
        RemoveApFromCategory(_ssidRoot,       ap);
    }

    private static void RemoveApFromCategory(TreeNodeViewModel root, AccessPointViewModel ap)
    {
        foreach (var group in root.Children.ToList())
        {
            var leaf = group.Children.FirstOrDefault(c => c.AccessPoint?.Bssid == ap.Bssid);
            if (leaf != null)
            {
                group.Children.Remove(leaf);
                if (group.Children.Count == 0)
                    root.Children.Remove(group);
                return;
            }
        }
    }

    /// <summary>Clears all group nodes from every category root.</summary>
    private void ClearTreeview()
    {
        _authRoot.Children.Clear();
        _channelRoot.Children.Clear();
        _encryptionRoot.Children.Clear();
        _networkRoot.Children.Clear();
        _ssidRoot.Children.Clear();
    }

    private async void OnAccessPointsDetected(object? sender, AccessPointsDetectedEventArgs e)
    {
        var startTime = DateTime.Now;

        foreach (var ap in e.AccessPoints)
        {
            // Get or add manufacturer
            var macPrefix = ap.Bssid.Substring(0, Math.Min(8, ap.Bssid.Length));
            ap.Manufacturer = await _databaseService.GetManufacturerAsync(macPrefix);

            // Get or add label
            var label = await _databaseService.GetLabelAsync(ap.Bssid);
            if (label != null)
                ap.Label = label;

            // Update or add to database
            var apId = await _databaseService.UpsertAccessPointAsync(ap);
            ap.ApId = apId;

            // Add signal history if GPS is active
            if (IsGpsActive && _gpsService.CurrentGpsData != null)
            {
                var gpsId = await _databaseService.AddGpsDataAsync(_gpsService.CurrentGpsData);

                var histEntry = new SignalHistory
                {
                    ApId = apId,
                    GpsId = gpsId,
                    Signal = ap.Signal ?? 0,
                    Rssi = ap.Rssi ?? 0,
                    Timestamp = DateTime.Now
                };
                await _databaseService.AddSignalHistoryAsync(histEntry);

                // Keep the VM's in-memory history up-to-date for the graph
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var apVm = AccessPoints.FirstOrDefault(x => x.Bssid == ap.Bssid);
                    apVm?.AddSignalHistoryEntry(histEntry);
                });
            }
            else
            {
                // Still record a history entry for the graph even without GPS
                var histEntry = new SignalHistory
                {
                    ApId = apId,
                    Signal = ap.Signal ?? 0,
                    Rssi = ap.Rssi ?? 0,
                    Timestamp = DateTime.Now
                };
                await _databaseService.AddSignalHistoryAsync(histEntry);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var apVm = AccessPoints.FirstOrDefault(x => x.Bssid == ap.Bssid);
                    apVm?.AddSignalHistoryEntry(histEntry);
                });
            }

            // Update UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existingAp = AccessPoints.FirstOrDefault(x => x.Bssid == ap.Bssid);
                if (existingAp != null)
                {
                    existingAp.UpdateFrom(ap);
                    UpdateApInTreeview(existingAp);
                }
                else
                {
                    var newVm = new AccessPointViewModel(ap);
                    AccessPoints.Add(newVm);
                    AddApToTreeview(newVm);
                }
            });
        }

        // Update counts and timing
        Application.Current.Dispatcher.Invoke(() =>
        {
            ActiveApCount = AccessPoints.Count(ap => ap.IsActive);
            LoopTime = (DateTime.Now - startTime).TotalMilliseconds;
        });
    }

    private void OnScanError(object? sender, ScanErrorEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"Scan error: {e.ErrorMessage}";
        });
    }

    private void OnGpsDataReceived(object? sender, GpsDataReceivedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            CurrentLatitude = $"{e.GpsData.Latitude:F6}";
            CurrentLongitude = $"{e.GpsData.Longitude:F6}";
        });
    }

    private void OnGpsError(object? sender, GpsErrorEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusMessage = $"GPS error: {e.ErrorMessage}";
            IsGpsActive = false;
        });
    }

    private void OpenImportWindow()
    {
        var window = _serviceProvider.GetRequiredService<Views.ImportWindow>();
        if (window.DataContext == null || !(window.DataContext is ImportViewModel))
        {
            window.DataContext = _serviceProvider.GetRequiredService<ImportViewModel>();
        }
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
        
        LoadAccessPointsFromDatabase();
    }

    private async void LoadAccessPointsFromDatabase()
    {
        StatusMessage = "Reloading from database...";
        try
        {
            var aps = await _databaseService.GetAllAccessPointsAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                AccessPoints.Clear();
                ClearTreeview();
                foreach (var ap in aps)
                {
                    var vm = new AccessPointViewModel(ap);
                    AccessPoints.Add(vm);
                    AddApToTreeview(vm);
                }
                ActiveApCount = AccessPoints.Count(ap => ap.IsActive);
                StatusMessage = $"Loaded {AccessPoints.Count} access points.";
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error reloading database: {ex.Message}";
        }
    }

    private List<AccessPoint> GetAccessPointsModels()
    {
        var list = new List<AccessPoint>();
        foreach (var vm in AccessPoints)
        {
            list.Add(new AccessPoint
            {
                ApId = vm.ApId,
                Bssid = vm.Bssid,
                Ssid = vm.Ssid,
                Manufacturer = vm.Manufacturer,
                Label = vm.Label,
                NetworkType = vm.NetworkType,
                Authentication = vm.Authentication,
                Encryption = vm.Encryption,
                RadioType = vm.RadioType,
                Channel = vm.Channel,
                Signal = vm.Signal,
                HighestSignal = vm.HighestSignal,
                Rssi = vm.Rssi,
                HighestRssi = vm.HighestRssi,
                FirstSeen = vm.FirstSeen,
                LastSeen = vm.LastSeen,
                Latitude = vm.Latitude,
                Longitude = vm.Longitude,
                IsActive = vm.IsActive
            });
        }
        return list;
    }

    private async Task ExportVs1()
    {
        var dialog = new SaveFileDialog { Filter = "Vistumbler VS1 (*.vs1)|*.vs1" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToVs1Async(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to VS1";
        }
    }

    private async Task ExportVsz()
    {
        var dialog = new SaveFileDialog { Filter = "Vistumbler VSZ (*.vsz)|*.vsz" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToVszAsync(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to VSZ";
        }
    }

    private async Task ExportCsv()
    {
        var dialog = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToCsvAsync(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to CSV";
        }
    }

    private async Task ExportKml()
    {
        var dialog = new SaveFileDialog { Filter = "KML Files (*.kml)|*.kml" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToKmlAsync(dialog.FileName, GetAccessPointsModels(), new ExportOptions());
            StatusMessage = "Exported to KML";
        }
    }

    private async Task ExportGpx()
    {
        var dialog = new SaveFileDialog { Filter = "GPX Files (*.gpx)|*.gpx" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToGpxAsync(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to GPX";
        }
    }

    private async Task ExportNs1()
    {
        var dialog = new SaveFileDialog { Filter = "NetStumbler Files (*.ns1)|*.ns1" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToNs1Async(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to NS1";
        }
    }

    private async Task ExportNetXml()
    {
        var dialog = new SaveFileDialog { Filter = "NetXML Files (*.netxml)|*.netxml" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToNetXmlAsync(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to NetXML";
        }
    }

    private async Task ExportKismetDb()
    {
        var dialog = new SaveFileDialog { Filter = "KismetDB Files (*.kismet)|*.kismet" };
        if (dialog.ShowDialog() == true)
        {
            await _exportService.ExportToKismetDbAsync(dialog.FileName, GetAccessPointsModels());
            StatusMessage = "Exported to KismetDB";
        }
    }

    private async Task UpdateManufacturersAsync()
    {
        const string ouiUrl = "https://standards-oui.ieee.org/oui/oui.txt";
        StatusMessage = "Downloading IEEE OUI data...";
        try
        {
            var response = await _httpClient.GetAsync(ouiUrl);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            StatusMessage = "Parsing OUI data...";
            var entries = new List<(string MacPrefix, string Manufacturer)>();
            foreach (var line in content.Split('\n'))
            {
                if (!line.Contains("(base 16)")) continue;
                var parts = line.Split(new[] { "(base 16)" }, StringSplitOptions.None);
                if (parts.Length < 2) continue;
                var prefix = parts[0].Trim();
                var manu   = parts[1].Trim();
                if (prefix.Length == 6)
                    entries.Add((prefix, manu));
            }

            StatusMessage = $"Updating {entries.Count} manufacturer entries...";
            await _databaseService.BulkUpsertManufacturersAsync(entries);
            StatusMessage = $"Updated {entries.Count} manufacturers.";
            MessageBox.Show(
                $"Successfully updated {entries.Count} manufacturer entries from the IEEE OUI database.",
                "Update Manufacturers", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = "Manufacturer update failed.";
            MessageBox.Show(
                $"Failed to update manufacturers:\n{ex.Message}",
                "Update Manufacturers", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
