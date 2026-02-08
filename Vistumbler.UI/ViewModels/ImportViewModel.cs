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
    Netstumbler,
    VistumblerDetailedCsv,
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
            StatusMessage = "Please select a valid file.";
            return;
        }

        IsImporting = true;
        StatusMessage = "Importing...";

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
                case ImportType.WigleCsv:
                case ImportType.WardriveAndroid:
                     // Assuming ImportFromCsvAsync handles all these or we treat them as generic CSV for now.
                    importedAps = await _importService.ImportFromCsvAsync(FilePath);
                    break;
            }

            if (importedAps != null && importedAps.Count > 0)
            {
                StatusMessage = $"Imported {importedAps.Count} APs. Saving to database...";
                int savedCount = 0;
                foreach (var ap in importedAps)
                {
                   savedCount += await _databaseService.UpsertAccessPointAsync(ap);
                }
                StatusMessage = $"Successfully imported and saved {importedAps.Count} APs.";
            }
            else
            {
                StatusMessage = "No Access Points found in file.";
            }

        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
                return "Netstumbler Files (*.ns1;*.txt)|*.ns1;*.txt|All files (*.*)|*.*";
            case ImportType.VistumblerDetailedCsv:
            case ImportType.WardriveAndroid:
            case ImportType.WigleCsv:
                return "CSV Files (*.csv)|*.csv|All files (*.*)|*.*";
            default:
                return "All files (*.*)|*.*";
        }
    }
    
    partial void OnSelectedImportTypeChanged(ImportType value)
    {
        // Optionally clear FilePath or update filter if Browe is called again
    }
}
