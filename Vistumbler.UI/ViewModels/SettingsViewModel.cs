using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace Vistumbler.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _comPort = "COM4";

    [ObservableProperty]
    private int _baudRate = 4800;

    [ObservableProperty]
    private int _refreshInterval = 1000;

    [ObservableProperty]
    private bool _autoScan = false;

    [ObservableProperty]
    private bool _playSound = true;

    [ObservableProperty]
    private int _timeBeforeMarkingDead = 2;

    [ObservableProperty]
    private string _saveDirectory = string.Empty;

    public SettingsViewModel()
    {
        SaveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Vistumbler");
    }
}
