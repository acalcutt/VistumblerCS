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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            await vm.LoadManufacturersAsync();
            await vm.LoadLabelsAsync();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.SaveSettings();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.SaveSettings();
    }

    // ── Browse helpers ────────────────────────────────────────────────────
    private void DirBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || DataContext is not SettingsViewModel vm) return;
        var tag = fe.Tag as string;

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Directory" };
        if (dialog.ShowDialog() != true) return;
        var folder = dialog.FolderName.TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;

        switch (tag)
        {
            case "SaveDir":             vm.SaveDir             = folder; break;
            case "SaveDirAuto":         vm.SaveDirAuto         = folder; break;
            case "SaveDirAutoRecovery": vm.SaveDirAutoRecovery = folder; break;
            case "SaveDirKml":          vm.SaveDirKml          = folder; break;
        }
    }

    private void GpsLogBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title  = "Select GPS Log Location",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = System.IO.Path.GetFileName(vm.GpsLogLocation)
        };
        if (dialog.ShowDialog() == true)
            vm.GpsLogLocation = dialog.FileName;
    }

    private void GoogleEarthBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select Google Earth Executable",
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            vm.GoogleEarthExe = dialog.FileName;
    }

    private void LangBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select Language File",
            Filter = "INI Files (*.ini)|*.ini|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            vm.LanguageImportPath = dialog.FileName;
    }

    private void CamScriptBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select Camera Trigger Script",
            Filter = "Executables & Scripts (*.exe;*.bat;*.ps1)|*.exe;*.bat;*.ps1|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            vm.CameraTriggerScript = dialog.FileName;
    }

    private void ColorBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || DataContext is not SettingsViewModel vm) return;
        var tag = fe.Tag as string;

        string currentHex = tag switch
        {
            "BackgroundColorHex"     => vm.BackgroundColorHex,
            "ControlColorHex"        => vm.ControlColorHex,
            "FontColorHex"           => vm.FontColorHex,
            "ButtonActiveColorHex"   => vm.ButtonActiveColorHex,
            "ButtonInactiveColorHex" => vm.ButtonInactiveColorHex,
            _ => "FFFFFF"
        };

        var dialog = new Views.InputDialog("Choose Color", "Enter hex color (RRGGBB):", currentHex)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        var hex = dialog.Value.Trim().TrimStart('#');
        if (hex.Length != 6) return;
        switch (tag)
        {
            case "BackgroundColorHex":     vm.BackgroundColorHex     = hex; break;
            case "ControlColorHex":        vm.ControlColorHex        = hex; break;
            case "FontColorHex":           vm.FontColorHex           = hex; break;
            case "ButtonActiveColorHex":   vm.ButtonActiveColorHex   = hex; break;
            case "ButtonInactiveColorHex": vm.ButtonInactiveColorHex = hex; break;
        }
    }
}
