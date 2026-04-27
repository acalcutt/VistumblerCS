using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.UI.ViewModels;

public partial class LabelEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _bssid = string.Empty;

    [ObservableProperty]
    private string _label = string.Empty;

    public LabelEntryViewModel() { }

    public LabelEntryViewModel(string bssid, string label)
    {
        _bssid = bssid;
        _label = label;
    }
}
