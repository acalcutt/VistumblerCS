using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.ViewModels;

public enum ImportType
{
    VistumblerFile,
    VistumblerDetailedCsv,
    Netstumbler,
    KismetFiles,   // Covers both .kismet (KismetDB) and .netxml — auto-detected by extension
    WardriveAndroid,
    WigleCsv
}

public partial class ImportViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IDatabaseService _databaseService;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private ImportType _selectedImportType;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private string _progressLabel = "Progress: Ready";

    [ObservableProperty]
    private string _minutesLabel = "Minutes:";

    [ObservableProperty]
    private string _lineTotalLabel = "Line/Total:";

    [ObservableProperty]
    private string _linesMinLabel = "Lines/Min:";

    [ObservableProperty]
    private string _newApsLabel = "New APs:";

    [ObservableProperty]
    private string _estTimeLabel = "Estimated Time Remaining:";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isImporting;

    // To allow closing the window from ViewModel, usually we use an interface or event. 
    // Ideally we'd use a DialogService, but for simplicity we can use an Action or specific logic in View.
    // For this task, I'll expose a CloseAction.
    public Action? CloseAction { get; set; }

    public ImportViewModel(IImportService importService, IDatabaseService databaseService)
    {
        _importService = importService;
        _databaseService = databaseService;
        _selectedImportType = ImportType.VistumblerFile;
    }

    [RelayCommand]
    private void Browse()
    {
        var openFileDialog = new OpenFileDialog();
        openFileDialog.Filter = GetFilterForType(SelectedImportType);
        
        if (openFileDialog.ShowDialog() == true)
        {
            FilePath = openFileDialog.FileName;
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            ProgressLabel = "Progress: Please select a valid file.";
            return;
        }

        IsImporting = true;
        ProgressLabel = "Progress: Loading";
        ProgressValue = 0;

        try
        {
            List<AccessPoint> importedAps = new();

            switch (SelectedImportType)
            {
                case ImportType.VistumblerFile:
                    if (Path.GetExtension(FilePath).Equals(".vsz", StringComparison.OrdinalIgnoreCase))
                    {
                        importedAps = await _importService.ImportFromVszAsync(FilePath);
                    }
                    else
                    {
                        importedAps = await _importService.ImportFromVs1Async(FilePath);
                    }
                    break;
                case ImportType.Netstumbler:
                    importedAps = await _importService.ImportFromNs1Async(FilePath);
                    break;
                case ImportType.VistumblerDetailedCsv:
                    importedAps = await _importService.ImportFromCsvAsync(FilePath);
                    break;
                case ImportType.WigleCsv:
                    importedAps = await _importService.ImportFromCsvAsync(FilePath);
                    break;
                case ImportType.WardriveAndroid:
                    importedAps = await _importService.ImportFromCsvAsync(FilePath);
                    break;
                case ImportType.KismetFiles:
                    // Auto-detect by extension: .kismet = KismetDB, .netxml = NetXML
                    if (Path.GetExtension(FilePath).Equals(".netxml", StringComparison.OrdinalIgnoreCase))
                        importedAps = await _importService.ImportFromNetXmlAsync(FilePath);
                    else
                        importedAps = await _importService.ImportFromKismetDbAsync(FilePath);
                    break;
            }

            if (importedAps != null && importedAps.Count > 0)
            {
                ProgressLabel = $"Progress: Saving {importedAps.Count} APs...";
                ProgressValue = 50;
                int savedCount = 0;
                foreach (var ap in importedAps)
                {
                   savedCount += await _databaseService.UpsertAccessPointAsync(ap);
                }
                ProgressValue = 100;
                ProgressLabel = "Progress: Done";
                NewApsLabel = $"New APs: {savedCount}";
                LineTotalLabel = $"Line/Total: {importedAps.Count}/{importedAps.Count}";
            }
            else
            {
                ProgressLabel = "Progress: No APs found";
            }

        }
        catch (Exception ex)
        {
            ProgressLabel = $"Progress: Error — {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private bool CanImport()
    {
        return !IsImporting;
    }

    [RelayCommand]
    private void Close()
    {
        CloseAction?.Invoke();
    }

    private string GetFilterForType(ImportType type)
    {
        switch (type)
        {
            case ImportType.VistumblerFile:
                return "Vistumbler Files (*.vs1;*.vsz)|*.vs1;*.vsz|All files (*.*)|*.*";
            case ImportType.Netstumbler:
                return "NetStumbler Files (*.ns1;*.txt)|*.ns1;*.txt|All files (*.*)|*.*";
            case ImportType.VistumblerDetailedCsv:
            case ImportType.WardriveAndroid:
            case ImportType.WigleCsv:
                return "CSV Files (*.csv)|*.csv|All files (*.*)|*.*";
            case ImportType.KismetFiles:
                return "Kismet Files (*.kismet;*.netxml)|*.kismet;*.netxml|All files (*.*)|*.*";
            default:
                return "All files (*.*)|*.*";
        }
    }
    
    partial void OnSelectedImportTypeChanged(ImportType value)
    {
        // Optionally clear FilePath or update filter if Browe is called again
    }
}
