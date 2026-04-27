using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.UI.ViewModels;

public partial class CameraEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    public CameraEntryViewModel() { }

    public CameraEntryViewModel(string name, string url)
    {
        _name = name;
        _url  = url;
    }
}
