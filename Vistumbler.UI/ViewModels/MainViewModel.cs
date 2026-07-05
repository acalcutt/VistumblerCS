using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private readonly SettingsViewModel _settings;

    public SettingsViewModel Settings => _settings;

    /// <summary>Live-updating GPS detail data shared by the GPS Details and Compass windows.</summary>
    private readonly GpsDetailsViewModel _gpsDetails = new();
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
    private string _windowTitle = BuildDefaultTitle();

    private string? _currentDbPath;
    private bool    _keepSession;   // true only when "Exit (Save DB)" was chosen

    // KML Network Link
    private Timer?  _kmlTimer;
    private string? _kmlLiveFile;       // path to the data KML that is re-exported each tick
    private string? _kmlNetworkLink;    // path to the wrapper network-link KML opened in Google Earth
    [ObservableProperty] private bool _isKmlNetworkLinkActive;

    private static string BuildDefaultTitle()
    {
        var ver = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+')[0]   // strip git hash suffix
            ?? "?";
        return $"Vistumbler CS v{ver} - By TechIdiots LLC";
    }

    private void UpdateWindowTitle()
    {
        var base_ = BuildDefaultTitle();
        WindowTitle = _currentDbPath is null
            ? base_
            : $"{base_} - ({Path.GetFileName(_currentDbPath)})";
    }

    [ObservableProperty]
    private int _activeApCount;

    [ObservableProperty]
    private string? _currentLatitude;

    [ObservableProperty]
    private string? _currentLongitude;

    [ObservableProperty]
    private double _loopTime;

    private DateTime _lastScanTime = DateTime.MinValue;

    // ── Adapter / Interface selection ─────────────────────────────────────────
    public ObservableCollection<WiFiAdapter> AvailableAdapters { get; } = new();

    [ObservableProperty]
    private WiFiAdapter? _activeAdapter;

    partial void OnActiveAdapterChanged(WiFiAdapter? value)
    {
        if (value != null)
            _wifiScanner.SetActiveAdapter(value.Id);
    }

    // Manufacturer cache: keyed by normalized 6-char MAC prefix (e.g. "AABBCC").
    // Populated on first AP scan; cleared and re-populated when user updates manufacturers.
    private readonly Dictionary<string, string> _manufacturerCache = new(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeMacPrefix(string bssid)
    {
        var hex = System.Text.RegularExpressions.Regex.Replace(bssid, "[^0-9A-Fa-f]", "");
        return hex.Length >= 6 ? hex[..6].ToUpperInvariant() : hex.ToUpperInvariant();
    }

    // ── Graph ────────────────────────────────────────────────────────

    [ObservableProperty]
    private GraphMode _graphMode = GraphMode.Hidden;

    [ObservableProperty]
    private bool _useRssiInGraphs = false;

    [ObservableProperty]
    private bool _graphDeadTime = true;

    [ObservableProperty]
    private bool _showTreeView = true;

    [ObservableProperty]
    private bool _isMinimalGuiMode = false;

    public bool IsGraphVisible        => !IsMinimalGuiMode && GraphMode is GraphMode.Line or GraphMode.Bar;
    public bool IsMapVisible          => !IsMinimalGuiMode && GraphMode == GraphMode.Map;
    public bool IsTreeViewVisible     => !IsMinimalGuiMode && ShowTreeView;
    public bool IsContentVisible      => !IsMinimalGuiMode;
    public bool IsGraphButtonsEnabled => !IsMinimalGuiMode;
    public bool IsTreeToggleEnabled   => !IsMinimalGuiMode;

    partial void OnGraphModeChanged(GraphMode value)
    {
        OnPropertyChanged(nameof(IsGraphVisible));
        OnPropertyChanged(nameof(IsMapVisible));
    }

    partial void OnShowTreeViewChanged(bool value)
        => OnPropertyChanged(nameof(IsTreeViewVisible));

    partial void OnIsMinimalGuiModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsContentVisible));
        OnPropertyChanged(nameof(IsGraphVisible));
        OnPropertyChanged(nameof(IsMapVisible));
        OnPropertyChanged(nameof(IsTreeViewVisible));
        OnPropertyChanged(nameof(IsGraphButtonsEnabled));
        OnPropertyChanged(nameof(IsTreeToggleEnabled));
    }

    partial void OnSelectedAccessPointChanged(AccessPointViewModel? value)
    {
        ((RelayCommand)OpenSignalHistoryCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CopyApInfoCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddManufacturerCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddLabelCommand).NotifyCanExecuteChanged();
        ((RelayCommand)OpenGeonamesCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)FindApInWifiDbCommand).NotifyCanExecuteChanged();
    }

    public ICommand SelectAdapterCommand { get; }
    public ICommand RefreshInterfacesCommand { get; }
    public ICommand StartScanCommand { get; }
    public ICommand StopScanCommand { get; }
    public ICommand ToggleScanCommand { get; }
    public ICommand StartGpsCommand { get; }
    public ICommand StopGpsCommand { get; }
    public ICommand ToggleGpsCommand { get; }
    public ICommand SaveAndClearCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ExitSaveDbCommand { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand OpenImportWindowCommand { get; }
    public ICommand OpenImportFolderCommand { get; }
    public ICommand ToggleKmlNetworkLinkCommand { get; }
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
    public ICommand ToggleGraphOffCommand { get; }
    public ICommand ToggleMapCommand { get; }
    public ICommand ToggleUseRssiCommand { get; }
    public ICommand ToggleGraphDeadTimeCommand { get; }
    public ICommand ToggleTreeViewCommand { get; }
    public ICommand ToggleListViewCommand { get; }
    // ── Options-menu toggle commands ──────────────────────────────
    public ICommand ToggleAutoRefreshNetworksCommand { get; }
    public ICommand ToggleAutoRecoveryCommand        { get; }
    public ICommand ToggleAutoSaveAndClearCommand    { get; }
    public ICommand ToggleAutoKmlCommand             { get; }
    public ICommand ToggleAutoScanOnLaunchCommand    { get; }
    public ICommand TogglePlaySoundCommand           { get; }
    public ICommand TogglePlayGpsSoundCommand        { get; }
    public ICommand ToggleSpeakSignalCommand         { get; }
    public ICommand TogglePlayMidiCommand            { get; }
    public ICommand ToggleSaveGpsWhenNoApsCommand    { get; }
    public ICommand ToggleDownloadImagesCommand      { get; }
    public ICommand ToggleCameraTriggerCommand       { get; }
    public ICommand TogglePortableModeCommand        { get; }
    public ICommand ToggleDebugModeCommand           { get; }
    // ── View-menu toggle commands ─────────────────────────────────────────
    public ICommand ToggleAutoSortCommand            { get; }
    public ICommand ToggleAutoSelectCommand          { get; }
    public ICommand ToggleAutoSelectHighSignalCommand { get; }
    public ICommand ToggleAddNewApsToTopCommand      { get; }
    public ICommand ToggleAutoScrollToBottomCommand  { get; }
    public ICommand ToggleBatchListviewInsertCommand { get; }
    // Debug submenu
    public ICommand ToggleDebugComCommand            { get; }
    // Edit menu
    public ICommand SortTreeCommand                  { get; }
    public ICommand SelectConnectedApCommand         { get; }
    // Filters
    public ObservableCollection<FilterRecord> AvailableFilters { get; } = new();
    public ICommand SelectFilterCommand              { get; }
    public ICommand AddRemoveFiltersCommand          { get; }
    public ICommand RefreshFiltersCommand            { get; }
    public ICommand OpenSignalHistoryCommand { get; }
    public ICommand Open24GHzGraphCommand { get; }
    public ICommand Open5GHzGraphCommand  { get; }
    public ICommand Open6GHzGraphCommand  { get; }

    // ── Context-menu commands (right-click on AP row) ─────────────────────
    public ICommand CopyApInfoCommand { get; }
    public ICommand AddManufacturerCommand { get; }
    public ICommand AddLabelCommand { get; }
    public ICommand OpenGeonamesCommand { get; }
    public ICommand FindApInWifiDbCommand { get; }

    // ── Extra-menu / WifiDB commands ──────────────────────────────────────
    public ICommand OpenGpsDetailsCommand { get; }
    public ICommand OpenGpsCompassCommand { get; }
    public ICommand OpenSaveFolderCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }
    public ICommand UploadToWifiDbCommand { get; }
    public ICommand ShowAboutCommand { get; }

    public MainViewModel(
        IWiFiScannerService wifiScanner,
        IGpsService gpsService,
        IDatabaseService databaseService,
        IImportService importService,
        IExportService exportService,
        IServiceProvider serviceProvider,
        SettingsViewModel settingsViewModel)
    {
        _wifiScanner = wifiScanner;
        _gpsService = gpsService;
        _databaseService = databaseService;
        _importService = importService;
        _exportService = exportService;
        _serviceProvider = serviceProvider;
        _settings = settingsViewModel;

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
        StopScanCommand  = new RelayCommand(StopScan, () => IsScanning);
        ToggleScanCommand = new AsyncRelayCommand(
            () => IsScanning ? Task.Run(StopScan) : StartScanAsync(),
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        StartGpsCommand = new AsyncRelayCommand(StartGpsAsync, () => !IsGpsActive);
        StopGpsCommand  = new RelayCommand(StopGps, () => IsGpsActive);
        ToggleGpsCommand = new AsyncRelayCommand(() => IsGpsActive ? Task.Run(StopGps) : StartGpsAsync());
        SaveAndClearCommand = new AsyncRelayCommand(SaveAndClearAsync);
        ClearAllCommand = new AsyncRelayCommand(ClearAllAsync);
        ExitCommand       = new AsyncRelayCommand(ExitAsync);
        ExitSaveDbCommand = new AsyncRelayCommand(ExitSaveDbAsync);
        OpenImportWindowCommand = new RelayCommand(OpenImportWindow);
        OpenImportFolderCommand = new RelayCommand(OpenImportFolderWindow);
        ToggleKmlNetworkLinkCommand = new RelayCommand(ToggleKmlNetworkLink);
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
        ToggleGraph1Command        = new RelayCommand(() => GraphMode = GraphMode == GraphMode.Line ? GraphMode.Hidden : GraphMode.Line);
        ToggleGraph2Command        = new RelayCommand(() => GraphMode = GraphMode == GraphMode.Bar  ? GraphMode.Hidden : GraphMode.Bar);
        ToggleGraphOffCommand      = new RelayCommand(() => GraphMode = GraphMode.Hidden);
        ToggleMapCommand           = new RelayCommand(() => GraphMode = GraphMode == GraphMode.Map  ? GraphMode.Hidden : GraphMode.Map);
        ToggleUseRssiCommand       = new RelayCommand(() => UseRssiInGraphs = !UseRssiInGraphs);
        ToggleGraphDeadTimeCommand = new RelayCommand(() => GraphDeadTime   = !GraphDeadTime);
        ToggleTreeViewCommand      = new RelayCommand(() => ShowTreeView     = !ShowTreeView);
        ToggleListViewCommand      = new RelayCommand(() => IsMinimalGuiMode = !IsMinimalGuiMode);
        ToggleAutoRefreshNetworksCommand = new RelayCommand(() => _settings.AutoRefreshNetworks    = !_settings.AutoRefreshNetworks);
        ToggleAutoRecoveryCommand        = new RelayCommand(() => _settings.AutoRecovery           = !_settings.AutoRecovery);
        ToggleAutoSaveAndClearCommand    = new RelayCommand(() => _settings.AutoSaveAndClear       = !_settings.AutoSaveAndClear);
        ToggleAutoKmlCommand             = new RelayCommand(() => _settings.AutoKml                = !_settings.AutoKml);
        ToggleAutoScanOnLaunchCommand    = new RelayCommand(() => _settings.AutoScanOnLaunch       = !_settings.AutoScanOnLaunch);
        TogglePlaySoundCommand           = new RelayCommand(() => _settings.PlaySound              = !_settings.PlaySound);
        TogglePlayGpsSoundCommand        = new RelayCommand(() => _settings.PlayGpsSound           = !_settings.PlayGpsSound);
        ToggleSpeakSignalCommand         = new RelayCommand(() => _settings.SpeakSignal            = !_settings.SpeakSignal);
        TogglePlayMidiCommand            = new RelayCommand(() => _settings.PlayMidiForActiveAps   = !_settings.PlayMidiForActiveAps);
        ToggleSaveGpsWhenNoApsCommand    = new RelayCommand(() => _settings.SaveGpsWhenNoApsActive = !_settings.SaveGpsWhenNoApsActive);
        ToggleDownloadImagesCommand      = new RelayCommand(() => _settings.DownloadImages         = !_settings.DownloadImages);
        ToggleCameraTriggerCommand       = new RelayCommand(() => _settings.EnableCameraTrigger    = !_settings.EnableCameraTrigger);
        TogglePortableModeCommand        = new RelayCommand(() => _settings.PortableMode           = !_settings.PortableMode);
        ToggleDebugModeCommand           = new RelayCommand(() => _settings.DebugMode              = !_settings.DebugMode);
        // View-menu toggles
        ToggleAutoSortCommand            = new RelayCommand(() => _settings.AutoSort               = !_settings.AutoSort);
        ToggleAutoSelectCommand          = new RelayCommand(() => _settings.AutoSelectConnectedAp  = !_settings.AutoSelectConnectedAp);
        ToggleAutoSelectHighSignalCommand = new RelayCommand(() => _settings.AutoSelectHighSignal  = !_settings.AutoSelectHighSignal);
        ToggleAddNewApsToTopCommand      = new RelayCommand(() => _settings.AddNewApsToTop         = !_settings.AddNewApsToTop);
        ToggleAutoScrollToBottomCommand  = new RelayCommand(() => _settings.AutoScrollToBottom     = !_settings.AutoScrollToBottom);
        ToggleBatchListviewInsertCommand = new RelayCommand(() => _settings.BatchListviewInsert    = !_settings.BatchListviewInsert);
        // Debug submenu
        ToggleDebugComCommand            = new RelayCommand(() => _settings.DebugCom               = !_settings.DebugCom);
        // Edit menu
        SortTreeCommand       = new RelayCommand(SortTree);
        SelectConnectedApCommand = new RelayCommand(SelectConnectedAp);
        // Filters
        SelectFilterCommand   = new RelayCommand<FilterRecord?>(SelectFilter);
        AddRemoveFiltersCommand = new AsyncRelayCommand(OpenAddRemoveFiltersAsync);
        RefreshFiltersCommand  = new AsyncRelayCommand(LoadFiltersAsync);
        OpenSignalHistoryCommand = new RelayCommand(OpenSignalHistoryWindow, () => SelectedAccessPoint != null);
        Open24GHzGraphCommand = new RelayCommand(() => OpenChannelGraph(Controls.GraphBand.TwoPointFourGHz));
        Open5GHzGraphCommand  = new RelayCommand(() => OpenChannelGraph(Controls.GraphBand.FiveGHz));
        Open6GHzGraphCommand  = new RelayCommand(() => OpenChannelGraph(Controls.GraphBand.SixGHz));
        CopyApInfoCommand     = new RelayCommand(CopyApInfo,         () => SelectedAccessPoint != null);
        AddManufacturerCommand = new AsyncRelayCommand(AddManufacturerAsync, () => SelectedAccessPoint != null);
        AddLabelCommand       = new AsyncRelayCommand(AddLabelAsync,        () => SelectedAccessPoint != null);
        OpenGeonamesCommand   = new RelayCommand(OpenGeonames,       () => SelectedAccessPoint != null);
        FindApInWifiDbCommand = new AsyncRelayCommand(FindApInWifiDb, () => SelectedAccessPoint != null);
        OpenGpsDetailsCommand = new RelayCommand(OpenGpsDetailsWindow);
        OpenGpsCompassCommand = new RelayCommand(OpenGpsCompassWindow);
        OpenSaveFolderCommand = new RelayCommand(OpenSaveFolder);
        ExportSettingsCommand = new RelayCommand(ExportSettings);
        ImportSettingsCommand = new RelayCommand(ImportSettings);
        UploadToWifiDbCommand = new AsyncRelayCommand(UploadToWifiDb);
        SelectAdapterCommand    = new RelayCommand<WiFiAdapter>(SelectAdapter);
        RefreshInterfacesCommand = new AsyncRelayCommand(LoadAdaptersAsync);
        ShowAboutCommand   = new RelayCommand(ShowAbout);
        NewSessionCommand  = new RelayCommand(StartNewSession);
    }

    /// <summary>
    /// Launch a new independent instance of the application with a fresh session.
    /// The current window and DB are left open and unaffected.
    /// </summary>
    private static void StartNewSession()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = exe,
            Arguments       = "--new-session",
            UseShellExecute = false,
        });
    }

    /// <summary>Sessions folder — all timestamped .db files live here.</summary>
    public static string SessionsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "Vistumbler", "sessions");

    /// <summary>Generate a new timestamped session path without creating the file.</summary>
    public static string NewSessionPath() =>
        Path.Combine(SessionsFolder,
                     DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".db");

    /// <summary>
    /// Return all .db files in the sessions folder that are not currently
    /// locked by another process, sorted newest-first.
    /// </summary>
    public static List<string> FindExistingSessions()
    {
        var dir = SessionsFolder;
        if (!Directory.Exists(dir)) return [];

        var result = new List<string>();
        foreach (var f in new DirectoryInfo(dir).GetFiles("*.db")
                          .OrderByDescending(fi => fi.LastWriteTime))
        {
            // Skip files locked by another instance
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                result.Add(f.FullName);
            }
            catch (IOException) { /* in use — skip */ }
        }
        return result;
    }

    private async void InitializeDatabaseAsync() { /* replaced by InitializeWithPathAsync */ }

    /// <summary>
    /// Called by App.OnStartup after the session picker has resolved a path.
    /// Initializes the database and loads startup data.
    /// </summary>
    public async Task InitializeWithPathAsync(string dbPath)
    {
        try
        {
            await _databaseService.InitializeAsync(dbPath);
            _currentDbPath = dbPath;
            UpdateWindowTitle();
            StatusMessage = "Database initialized";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Database error: {ex.Message}";
        }

        await LoadAdaptersAsync();
        await LoadFiltersAsync();
    }

    private async Task LoadAdaptersAsync()
    {
        var adapters = await _wifiScanner.GetAvailableAdaptersAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            var previousId = ActiveAdapter?.Id;
            AvailableAdapters.Clear();
            foreach (var a in adapters)
                AvailableAdapters.Add(a);

            // Re-select previously selected adapter, or default to the first one
            ActiveAdapter = AvailableAdapters.FirstOrDefault(a => a.Id == previousId)
                         ?? AvailableAdapters.FirstOrDefault();
        });
    }

    private void SelectAdapter(WiFiAdapter? adapter)
    {
        if (adapter != null)
            ActiveAdapter = adapter;
    }

    // ── Filters ───────────────────────────────────────────────────────────

    public async Task LoadFiltersAsync()
    {
        var filters = await _databaseService.GetAllFiltersAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            AvailableFilters.Clear();
            foreach (var f in filters) AvailableFilters.Add(f);
        });
    }

    private void SelectFilter(FilterRecord? filter)
    {
        // Selecting the already-active filter deselects it (toggle off)
        var newId = (filter != null && _settings.ActiveFilterId != filter.FiltId) ? filter.FiltId : -1;
        _settings.ActiveFilterId = newId;
    }

    private Task OpenAddRemoveFiltersAsync()
    {
        // TODO: open the Add/Remove Filters dialog when it is built
        System.Windows.MessageBox.Show(
            "The Add/Remove Filters dialog is not yet implemented.",
            "Filters", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    // ── Edit-menu helpers ─────────────────────────────────────────────────

    private void SortTree()
    {
        // Sort alphabetically by SSID within each treeview root
        foreach (var root in TreeviewRoots)
        {
            var sorted = root.Children.OrderBy(c => c.Name).ToList();
            root.Children.Clear();
            foreach (var c in sorted) root.Children.Add(c);
        }
    }

    private void SelectConnectedAp()
    {
        // Find the AP whose BSSID matches the currently connected network
        // (placeholder — requires NativeWifi connected-network query)
        StatusMessage = "Select Connected AP: not yet implemented";
    }

    private async Task StartScanAsync()
    {
        try
        {
            IsScanning = true;
            _scanCancellationTokenSource = new CancellationTokenSource();
            StatusMessage = "Scanning for networks...";

            // Apply scan interval from settings
            _wifiScanner.ScanIntervalMs = _settings.RefreshLoopTimeMs;

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
            var s = _settings;

            var parity = s.Parity switch
            {
                "Odd"   => System.IO.Ports.Parity.Odd,
                "Even"  => System.IO.Ports.Parity.Even,
                "Mark"  => System.IO.Ports.Parity.Mark,
                "Space" => System.IO.Ports.Parity.Space,
                _       => System.IO.Ports.Parity.None,
            };
            var stopBits = s.StopBit switch
            {
                "1.5" => System.IO.Ports.StopBits.OnePointFive,
                "2"   => System.IO.Ports.StopBits.Two,
                _     => System.IO.Ports.StopBits.One,
            };

            var config = new GpsConfiguration
            {
                Source   = s.GpsSource,
                ComPort  = $"COM{s.ComPortNumber}",
                BaudRate = int.TryParse(s.BaudRate, out int baud) ? baud : 4800,
                DataBits = int.TryParse(s.DataBit,  out int db)   ? db   : 8,
                Parity   = parity,
                StopBits = stopBits,
            };

            IsGpsActive   = true;
            StatusMessage = s.GpsSource == Core.Enums.GpsSourceType.WindowsLocation
                ? "Starting GPS (Windows Location API)..."
                : $"Starting GPS on COM{s.ComPortNumber}...";

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

    private async Task SaveAndClearAsync()
    {
        // Prompt for save file, then clear — mirrors AutoIt _AutoSaveAndClear
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save & Clear — choose export file",
            Filter = "Vistumbler Files (*.vs1)|*.vs1|Vistumbler Zip (*.vsz)|*.vsz|All files (*.*)|*.*",
            DefaultExt = ".vs1"
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        var aps  = GetAccessPointsModels();

        try
        {
            if (path.EndsWith(".vsz", StringComparison.OrdinalIgnoreCase))
                await _exportService.ExportToVszAsync(path, aps);
            else
                await _exportService.ExportToVs1Async(path, aps);

            await _databaseService.ClearAllAccessPointsAsync();
            AccessPoints.Clear();
            ClearTreeview();
            ActiveApCount = 0;
            StatusMessage = $"Saved & cleared — {aps.Count} APs written to {System.IO.Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save & Clear failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        _manufacturerCache[NormalizeMacPrefix(ap.Bssid)] = dialog.Value.Trim();
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

    private void OpenGpsDetailsWindow()
    {
        var window = new Views.GpsDetailsWindow(_gpsDetails) { Owner = Application.Current.MainWindow };
        window.Show();
    }

    private void OpenGpsCompassWindow()
    {
        var window = new Views.GpsCompassWindow(_gpsDetails) { Owner = Application.Current.MainWindow };
        window.Show();
    }

    private void OpenSaveFolder()
    {
        var dir = _settings.SaveDir;
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Vistumbler");
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open save folder:\n{ex.Message}", "Open Save Folder",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSettings()
    {
        var dialog = new SaveFileDialog
        {
            Filter   = "INI Files (*.ini)|*.ini|All Files (*.*)|*.*",
            FileName = "vistumbler_settings.ini"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _settings.ExportSettingsTo(dialog.FileName);
            StatusMessage = $"Settings exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export settings:\n{ex.Message}", "Export Settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportSettings()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "INI Files (*.ini)|*.ini|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _settings.ImportSettingsFrom(dialog.FileName);
            StatusMessage = "Settings imported. Some changes may require a restart.";
            MessageBox.Show(
                "Settings imported successfully. Some changes may require restarting Vistumbler to take full effect.",
                "Import Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not import settings:\n{ex.Message}", "Import Settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task UploadToWifiDb()
    {
        // Mirrors _AddToYourWDB() / _UploadFileToWifiDB() in Vistumbler.au3:
        // exports the current APs to a VS1 or CSV file and POSTs it (multipart/form-data)
        // to {WifiDbApiUrl}import.php with the user's credentials.
        var models = GetAccessPointsModels();
        if (models.Count == 0)
        {
            MessageBox.Show("There are no access points to upload.", "Upload to WifiDB",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var defaultTitle = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var dialog = new Views.WifiDbUploadWindow(_settings.WifiDbUser, _settings.WifiDbApiKey, defaultTitle)
        {
            Owner = Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true) return;

        var user     = string.IsNullOrWhiteSpace(dialog.UserName) ? "Unknown" : dialog.UserName;
        var apiKey   = dialog.ApiKey;
        var fileType = dialog.FileType;            // "VS1" or "CSV"
        var apiBase  = string.IsNullOrWhiteSpace(_settings.WifiDbApiUrl)
                           ? "https://api.wifidb.net/"
                           : _settings.WifiDbApiUrl;
        if (!apiBase.EndsWith('/')) apiBase += "/";
        var apiUrl = apiBase + "import.php";

        // Persist any edited credentials back to settings.
        if (user != "Unknown") _settings.WifiDbUser = user;
        _settings.WifiDbApiKey = apiKey;

        StatusMessage = "Exporting APs for WifiDB upload\u2026";

        var ext      = fileType == "CSV" ? "csv" : "vs1";
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"WDB_Export_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}");
        var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_VS.{(fileType == "CSV" ? "CSV" : "VS1")}";

        try
        {
            if (fileType == "CSV")
                await _exportService.ExportToCsvAsync(tempPath, models);
            else
                await _exportService.ExportToVs1Async(tempPath, models);

            var fileBytes = await File.ReadAllBytesAsync(tempPath);

            using var content = new System.Net.Http.MultipartFormDataContent();
            if (!string.IsNullOrWhiteSpace(apiKey))
                content.Add(new System.Net.Http.StringContent(apiKey), "apikey");
            content.Add(new System.Net.Http.StringContent(user), "username");
            if (!string.IsNullOrWhiteSpace(dialog.OtherUsers))
                content.Add(new System.Net.Http.StringContent(dialog.OtherUsers), "otherusers");
            if (!string.IsNullOrWhiteSpace(dialog.UploadTitle))
                content.Add(new System.Net.Http.StringContent(dialog.UploadTitle), "title");
            if (!string.IsNullOrWhiteSpace(dialog.Notes))
                content.Add(new System.Net.Http.StringContent(dialog.Notes), "notes");

            var fileContent = new System.Net.Http.ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") { CharSet = "UTF-8" };
            content.Add(fileContent, "file", fileName);

            StatusMessage = "Uploading APs to WifiDB\u2026";
            var response = await _httpClient.PostAsync(apiUrl, content);
            var body     = await response.Content.ReadAsStringAsync();

            MessageBox.Show(
                string.IsNullOrWhiteSpace(body) ? "Upload complete (no response body)." : body,
                "WifiDB Upload Result", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusMessage = "WifiDB upload complete";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"WifiDB upload failed:\n{ex.Message}", "WifiDB Upload Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "WifiDB upload failed";
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
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

    private void OpenChannelGraph(Controls.GraphBand band)
    {
        var window = new Views.ChannelGraphWindow(_accessPoints, UseRssiInGraphs, band)
        {
            Owner = Application.Current.MainWindow
        };
        window.Show();
    }

    private async Task ExitAsync()
    {
        Application.Current.Shutdown();
        await Task.CompletedTask;
    }

    private async Task ExitSaveDbAsync()
    {
        _keepSession = true;   // tell CloseSessionAsync to leave the file alone
        Application.Current.Shutdown();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Called from App.OnExit on every exit path (X button, menu items, etc.).
    /// Closes the DB connection then deletes the session file unless the user
    /// explicitly chose "Exit (Save DB)". On a crash OnExit does not run,
    /// so the file survives for recovery.
    /// </summary>
    public async Task CloseSessionAsync()
    {
        await _databaseService.CloseAsync();

        if (!_keepSession && _currentDbPath != null && File.Exists(_currentDbPath))
        {
            // Retry briefly: the OS may take a moment to release the SQLite
            // file handle after the pool is cleared.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Delete(_currentDbPath);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(50);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(50);
                }
            }
        }
    }

    private void ShowAbout()
    {
        var asm     = Assembly.GetEntryAssembly()!;
        var ver     = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion.Split('+')[0] ?? "?";
        var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Vistumbler";
        var company = asm.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "TechIdiots LLC";
        var copy    = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;
        var built   = File.GetLastWriteTime(asm.Location).ToString("yyyy-MM-dd");

        MessageBox.Show(
            $"{product}\n" +
            $"Version: {ver}\n" +
            $"Publisher: {company}\n" +
            $"{copy}\n" +
            $"Build date: {built}",
            $"About {product}",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        var scanTime = DateTime.Now;
        if (_lastScanTime != DateTime.MinValue)
        {
            Application.Current.Dispatcher.Invoke(() =>
                LoopTime = (scanTime - _lastScanTime).TotalMilliseconds);
        }
        _lastScanTime = scanTime;

        foreach (var ap in e.AccessPoints)
        {
            // Get manufacturer from cache; only hit the DB the first time this prefix is seen
            var macPrefix = NormalizeMacPrefix(ap.Bssid);
            if (!_manufacturerCache.TryGetValue(macPrefix, out var manufacturer))
            {
                manufacturer = await _databaseService.GetManufacturerAsync(macPrefix);
                _manufacturerCache[macPrefix] = manufacturer;
            }
            ap.Manufacturer = manufacturer;

            // Get or add label
            var label = await _databaseService.GetLabelAsync(ap.Bssid);
            if (label != null)
                ap.Label = label;

            // Stamp current GPS position onto the AP before upsert
            if (IsGpsActive && _gpsService.CurrentGpsData is { } gpsSnap)
            {
                ap.Latitude  = gpsSnap.Latitude;
                ap.Longitude = gpsSnap.Longitude;
            }

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
                    GpsData = _gpsService.CurrentGpsData,
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
                    newVm.LineNumber = AccessPoints.Count + 1;
                    AccessPoints.Add(newVm);
                    AddApToTreeview(newVm);
                }
            });
        }

        // Mark APs dead if they haven't been seen within the timeout window
        var deadThreshold = TimeSpan.FromSeconds(_settings.TimeBeforeMarkingDeadS);
        var now = DateTime.Now;
        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var apVm in AccessPoints)
            {
                if (apVm.IsActive && (now - apVm.LastSeen) > deadThreshold)
                {
                    apVm.IsActive = false;
                    apVm.Signal   = 0;
                    apVm.Rssi     = 0;
                }
            }
        });

        // Update counts
        Application.Current.Dispatcher.Invoke(() =>
        {
            ActiveApCount = AccessPoints.Count(ap => ap.IsActive);
            FireLiveApGeoJson();
        });
    }

    // ── Live AP GeoJSON ───────────────────────────────────────────────────────

    /// <summary>
    /// Fired on the UI thread after each GPS fix with raw position values,
    /// suitable for updating a map location indicator.
    /// </summary>
    public event EventHandler<GpsLocationEventArgs>? GpsLocationUpdated;

    /// <summary>
    /// Fired on the UI thread after each scan cycle with the current GeoJSON
    /// FeatureCollection for all active APs that have GPS coordinates.
    /// </summary>
    public event EventHandler<string>? LiveApGeoJsonUpdated;

    private void FireLiveApGeoJson()
    {
        if (LiveApGeoJsonUpdated == null) return;

        var sb = new System.Text.StringBuilder();
        sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");
        bool first = true;
        foreach (var ap in AccessPoints)
        {
            if (ap.Latitude == null || ap.Longitude == null) continue;
            if (!first) sb.Append(',');
            first = false;

            int sectype = ap.Authentication switch
            {
                Vistumbler.Core.Models.AuthenticationType.Open  => 1,
                _ => ap.Encryption == Vistumbler.Core.Models.EncryptionType.WEP ? 2 : 3,
            };

            // Fold active/dead + sectype into a single 1..6 style index the map's live
            // layer colors from: active APs 1/2/3, this session's dead APs 4/5/6.
            int styidx = ap.IsActive ? sectype : sectype + 3;

            sb.Append("{\"type\":\"Feature\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[");
            sb.Append(ap.Longitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(ap.Latitude.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("]},\"properties\":{\"bssid\":\"");
            sb.Append(ap.Bssid.Replace("\"", ""));
            sb.Append("\",\"ssid\":\"");
            sb.Append(ap.Ssid.Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.Append("\",\"signal\":");
            sb.Append(ap.Signal ?? 0);
            sb.Append(",\"sectype\":");
            sb.Append(sectype);
            sb.Append(",\"active\":");
            sb.Append(ap.IsActive ? 1 : 0);
            sb.Append(",\"styidx\":");
            sb.Append(styidx);
            sb.Append("}}");
        }
        sb.Append("]}");
        LiveApGeoJsonUpdated?.Invoke(this, sb.ToString());
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
            CurrentLatitude  = FormatCoordinate(e.GpsData.Latitude,  isLat: true,  _settings.GpsFormat);
            CurrentLongitude = FormatCoordinate(e.GpsData.Longitude, isLat: false, _settings.GpsFormat);
            GpsLocationUpdated?.Invoke(this, new GpsLocationEventArgs
            {
                Latitude       = e.GpsData.Latitude,
                Longitude      = e.GpsData.Longitude,
                Bearing        = (float)(e.GpsData.TrackAngle        ?? 0.0),
                AccuracyMeters = (float)(e.GpsData.HorizontalDilution ?? 10.0),
            });
            _gpsDetails.UpdateFromGpsData(e.GpsData);
        });
    }

    private static string FormatCoordinate(double deg, bool isLat, string format)
    {
        char dir   = deg >= 0 ? (isLat ? 'N' : 'E') : (isLat ? 'S' : 'W');
        double abs = Math.Abs(deg);
        int d      = (int)abs;
        double mf  = (abs - d) * 60.0;
        int m      = (int)mf;
        double s   = (mf - m) * 60.0;
        string ds  = isLat ? $"{d:D2}" : $"{d:D3}";

        return format switch
        {
            "ddmm.mmmm"  => $"{dir} {ds}{mf:00.0000}",
            "dd mm ss.s" => $"{dir} {ds} {m:D2} {s:00.0}",
            "dd mm.mmmm" => $"{dir} {ds} {mf:00.0000}",
            _            => deg.ToString("F6"),  // dd.dddddd
        };
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

    private void OpenImportFolderWindow()
    {
        var window = _serviceProvider.GetRequiredService<Views.ImportFolderWindow>();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
        LoadAccessPointsFromDatabase();
    }

    // ── KML Network Link ──────────────────────────────────────────────────────
    // Exports the current APs to a fixed live.kml every 5 seconds while active.
    // A wrapper networklink.kml (opened once in Google Earth) tells GE to poll
    // live.kml on the same interval, giving reliable auto-refresh without an
    // HTTP server or manual re-opens.
    private static string KmlFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "Vistumbler", "kml");

    private void ToggleKmlNetworkLink()
    {
        if (IsKmlNetworkLinkActive)
        {
            _kmlTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _kmlTimer?.Dispose();
            _kmlTimer = null;
            IsKmlNetworkLinkActive = false;
            StatusMessage = "KML Network Link stopped.";
            return;
        }

        try
        {
            Directory.CreateDirectory(KmlFolder);
            _kmlLiveFile    = Path.Combine(KmlFolder, "live.kml");
            _kmlNetworkLink = Path.Combine(KmlFolder, "networklink.kml");

            // Write the wrapper network-link KML once
            var kmlContent = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                             "<kml xmlns=\"http://www.opengis.net/kml/2.2\">\n" +
                             "  <NetworkLink>\n" +
                             "    <name>Vistumbler CS Live</name>\n" +
                             "    <refreshVisibility>0</refreshVisibility>\n" +
                             "    <flyToView>0</flyToView>\n" +
                             "    <Link>\n" +
                             $"      <href>{_kmlLiveFile}</href>\n" +
                             "      <refreshMode>onInterval</refreshMode>\n" +
                             "      <refreshInterval>5</refreshInterval>\n" +
                             "    </Link>\n" +
                             "  </NetworkLink>\n" +
                             "</kml>\n";
            File.WriteAllText(_kmlNetworkLink, kmlContent);

            // Export immediately then start the 5-second refresh timer
            ExportKmlLive();
            _kmlTimer = new Timer(_ => ExportKmlLive(), null, 5000, 5000);
            IsKmlNetworkLinkActive = true;
            StatusMessage = "KML Network Link active — opening in Google Earth…";

            // Open the network-link file in Google Earth (or default handler)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = _kmlNetworkLink,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"KML Network Link error: {ex.Message}";
        }
    }

    private void ExportKmlLive()
    {
        if (_kmlLiveFile == null) return;
        try
        {
            var aps = GetAccessPointsModels();
            _exportService.ExportToKmlAsync(_kmlLiveFile, aps, new Core.Services.ExportOptions())
                          .GetAwaiter().GetResult();
        }
        catch { /* best-effort — timer thread, swallow silently */ }
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
                    vm.LineNumber = AccessPoints.Count + 1;
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

            // Refresh manufacturer display for all currently loaded APs
            _manufacturerCache.Clear();
            foreach (var apVm in AccessPoints.ToList())
            {
                var prefix = NormalizeMacPrefix(apVm.Bssid);
                if (!_manufacturerCache.TryGetValue(prefix, out var mfr))
                {
                    mfr = await _databaseService.GetManufacturerAsync(prefix);
                    _manufacturerCache[prefix] = mfr;
                }
                apVm.Manufacturer = mfr;
            }

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

/// <summary>Event args for a raw GPS position fix, used to update the map location indicator.</summary>
public sealed class GpsLocationEventArgs : EventArgs
{
    public double Latitude       { get; init; }
    public double Longitude      { get; init; }
    /// <summary>Track angle / heading in degrees (0 = north, clockwise). 0 when unknown.</summary>
    public float  Bearing        { get; init; }
    /// <summary>Horizontal accuracy in metres (Windows Location API) or HDOP (serial NMEA).</summary>
    public float  AccuracyMeters { get; init; }
}
