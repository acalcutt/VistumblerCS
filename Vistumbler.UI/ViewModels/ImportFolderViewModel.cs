using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Vistumbler.Core.Services;

namespace Vistumbler.UI.ViewModels;

public partial class ImportFolderViewModel : ViewModelBase
{
    private readonly IImportService _importService;
    private readonly IDatabaseService _databaseService;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private ImportType _selectedImportType = ImportType.VistumblerFile;

    [ObservableProperty]
    private string _extensionHint = "*.vs1, *.vsz";

    [ObservableProperty]
    private double _progressValue = 0;

    [ObservableProperty]
    private string _progressLabel = "Ready — select a folder and file type then click Import.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isImporting;

    public Action? CloseAction { get; set; }

    public ImportFolderViewModel(IImportService importService, IDatabaseService databaseService)
    {
        _importService = importService;
        _databaseService = databaseService;
    }

    partial void OnSelectedImportTypeChanged(ImportType value)
    {
        ExtensionHint = GetExtensionHint(value);
    }

    [RelayCommand]
    private void Browse()
    {
        // OpenFolderDialog is available in .NET 8+ (Microsoft.Win32)
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the folder containing files to import",
        };
        if (dialog.ShowDialog() == true)
        {
            FolderPath = dialog.FolderName;
            ProgressLabel = $"Folder selected: {FolderPath}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            ProgressLabel = "Please select a valid folder first.";
            return;
        }

        var extensions = GetExtensions(SelectedImportType);
        var files = extensions
            .SelectMany(ext => Directory.GetFiles(FolderPath, ext, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            ProgressLabel = $"No matching files found ({ExtensionHint}).";
            return;
        }

        IsImporting = true;
        ProgressValue = 0;
        int totalNew = 0;
        int totalAps = 0;

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            ProgressLabel = $"Importing {i + 1}/{files.Count}: {Path.GetFileName(file)}";
            ProgressValue = (i / (double)files.Count) * 100;

            try
            {
                var aps = await ImportFileAsync(file);
                totalAps += aps.Count;
                foreach (var ap in aps)
                    totalNew += await _databaseService.UpsertAccessPointAsync(ap);
            }
            catch (Exception ex)
            {
                ProgressLabel = $"Error on {Path.GetFileName(file)}: {ex.Message}";
                await Task.Delay(800);
            }
        }

        ProgressValue = 100;
        ProgressLabel = $"Done — {files.Count} files, {totalAps} APs read, {totalNew} new/updated.";
        IsImporting = false;
    }

    private bool CanImport() => !IsImporting;

    [RelayCommand]
    private void Close() => CloseAction?.Invoke();

    private async Task<List<Vistumbler.Core.Models.AccessPoint>> ImportFileAsync(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return SelectedImportType switch
        {
            ImportType.VistumblerFile =>
                ext == ".vsz"
                    ? await _importService.ImportFromVszAsync(filePath)
                    : await _importService.ImportFromVs1Async(filePath),
            ImportType.VistumblerDetailedCsv => await _importService.ImportFromCsvAsync(filePath),
            ImportType.WigleCsv              => await _importService.ImportFromCsvAsync(filePath),
            ImportType.WardriveAndroid        => await _importService.ImportFromCsvAsync(filePath),
            ImportType.Netstumbler            => await _importService.ImportFromNs1Async(filePath),
            ImportType.KismetFiles =>
                ext == ".netxml"
                    ? await _importService.ImportFromNetXmlAsync(filePath)
                    : await _importService.ImportFromKismetDbAsync(filePath),
            _ => [],
        };
    }

    private static string[] GetExtensions(ImportType type) => type switch
    {
        ImportType.VistumblerFile        => ["*.vs1", "*.vsz"],
        ImportType.VistumblerDetailedCsv => ["*.csv"],
        ImportType.WigleCsv              => ["*.csv"],
        ImportType.WardriveAndroid       => ["*.db3"],
        ImportType.Netstumbler           => ["*.ns1", "*.txt"],
        ImportType.KismetFiles           => ["*.kismet", "*.netxml"],
        _                                => ["*.*"],
    };

    private static string GetExtensionHint(ImportType type) => type switch
    {
        ImportType.VistumblerFile        => "*.vs1, *.vsz",
        ImportType.VistumblerDetailedCsv => "*.csv",
        ImportType.WigleCsv              => "*.csv",
        ImportType.WardriveAndroid       => "*.db3",
        ImportType.Netstumbler           => "*.ns1, *.txt",
        ImportType.KismetFiles           => "*.kismet, *.netxml",
        _                                => "*.*",
    };
}
