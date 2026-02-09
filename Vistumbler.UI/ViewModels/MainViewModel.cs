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
        ExportNetXmlCommand = new AsyncRelayCommand(ExportNetXml);
        ExportKismetDbCommand = new AsyncRelayCommand(ExportKismetDb);

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
            ActiveApCount = 0;
            StatusMessage = "All access points cleared";
        }
    }

    private void Exit()
    {
        Application.Current.Shutdown();
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
                
                await _databaseService.AddSignalHistoryAsync(new SignalHistory
                {
                    ApId = apId,
                    GpsId = gpsId,
                    Signal = ap.Signal ?? 0,
                    Rssi = ap.Rssi ?? 0,
                    Timestamp = DateTime.Now
                });
            }

            // Update UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existingAp = AccessPoints.FirstOrDefault(x => x.Bssid == ap.Bssid);
                if (existingAp != null)
                {
                    existingAp.UpdateFrom(ap);
                }
                else
                {
                    AccessPoints.Add(new AccessPointViewModel(ap));
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
                foreach (var ap in aps)
                {
                    AccessPoints.Add(new AccessPointViewModel(ap));
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
}
