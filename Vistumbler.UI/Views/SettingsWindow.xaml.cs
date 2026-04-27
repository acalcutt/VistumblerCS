using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void BrowseSaveDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Save Directory"
        };
        if (dialog.ShowDialog() == true && DataContext is SettingsViewModel vm)
            vm.SaveDirectory = dialog.FolderName;
    }
}
