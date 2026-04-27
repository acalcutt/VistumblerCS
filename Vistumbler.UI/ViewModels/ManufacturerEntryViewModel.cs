using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.UI.ViewModels;

public partial class ManufacturerEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _macPrefix = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    public ManufacturerEntryViewModel() { }

    public ManufacturerEntryViewModel(string macPrefix, string manufacturer)
    {
        _macPrefix    = macPrefix;
        _manufacturer = manufacturer;
    }
}
