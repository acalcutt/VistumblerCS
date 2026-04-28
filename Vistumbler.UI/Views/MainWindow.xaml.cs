using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Vistumbler.Core.Services;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class MainWindow : Window
{
    private double _savedHeight;
    private double _savedMinHeight;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Settings.PropertyChanged += OnSettingsChanged;
        ApplyColumnSettings(viewModel.Settings);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _savedHeight = Height;
        _savedMinHeight = MinHeight;

        // Rebuild the Interface menu when the adapter list or active adapter changes
        viewModel.AvailableAdapters.CollectionChanged += (_, _) => RebuildInterfaceMenu();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveAdapter))
                RebuildInterfaceMenu();
        };
    }

    // ── Interface menu (dynamic adapter items) ─────────────────────────────────

    /// <summary>
    /// Rebuilds the adapter items in the Interface menu. The first two items
    /// (Refresh Interfaces + Separator) are declared in XAML and kept in place.
    /// </summary>
    private void RebuildInterfaceMenu()
    {
        var vm = (MainViewModel)DataContext!;

        // Remove old adapter items (everything after the XAML separator at index 1)
        while (InterfaceMenu.Items.Count > 2)
            InterfaceMenu.Items.RemoveAt(2);

        foreach (var adapter in vm.AvailableAdapters)
        {
            var item = new MenuItem
            {
                Header      = adapter.Name,
                IsCheckable = true,
                IsChecked   = adapter == vm.ActiveAdapter
            };
            item.Click += (_, _) => vm.SelectAdapterCommand.Execute(adapter);
            InterfaceMenu.Items.Add(item);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsMinimalGuiMode)) return;
        var vm = (MainViewModel)DataContext!;
        if (vm.IsMinimalGuiMode)
        {
            _savedHeight = Height;
            _savedMinHeight = MinHeight;
            MinHeight = 0;
            SizeToContent = SizeToContent.Height;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            MinHeight = _savedMinHeight;
            Height = _savedHeight;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SettingsViewModel s)
            ApplyColumnSettings(s);
    }

    private void ApplyColumnSettings(SettingsViewModel s)
    {
        ApplyCol(ColLineNumber,         s.ShowLineNumber,         s.LineNumberWidth);
        ApplyCol(ColActive,             s.ShowActive,             s.ActiveWidth);
        ApplyCol(ColSsid,               s.ShowSsid,               s.SsidWidth);
        ApplyCol(ColMacAddress,         s.ShowMacAddress,         s.MacAddressWidth);
        ApplyCol(ColSignal,             s.ShowSignal,             s.SignalWidth);
        ApplyCol(ColHighSignal,         s.ShowHighSignal,         s.HighSignalWidth);
        ApplyCol(ColRssi,               s.ShowRssi,               s.RssiWidth);
        ApplyCol(ColHighRssi,           s.ShowHighRssi,           s.HighRssiWidth);
        ApplyCol(ColAuthentication,     s.ShowAuthentication,     s.AuthenticationWidth);
        ApplyCol(ColEncryption,         s.ShowEncryption,         s.EncryptionWidth);
        ApplyCol(ColRadioType,          s.ShowRadioType,          s.RadioTypeWidth);
        ApplyCol(ColNetworkType,        s.ShowNetworkType,        s.NetworkTypeWidth);
        ApplyCol(ColChannel,            s.ShowChannel,            s.ChannelWidth);
        ApplyCol(ColFrequency,          s.ShowFrequency,          s.FrequencyWidth);
        ApplyCol(ColManufacturer,       s.ShowManufacturer,       s.ManufacturerWidth);
        ApplyCol(ColLabel,              s.ShowLabel,              s.LabelWidth);
        ApplyCol(ColLatitude,           s.ShowLatitude,           s.LatitudeWidth);
        ApplyCol(ColLongitude,          s.ShowLongitude,          s.LongitudeWidth);
        ApplyCol(ColLatitudeDdmmss,     s.ShowLatitudeDdmmss,     s.LatitudeDdmmssWidth);
        ApplyCol(ColLongitudeDdmmss,    s.ShowLongitudeDdmmss,    s.LongitudeDdmmssWidth);
        ApplyCol(ColLatitudeDdmmmm,     s.ShowLatitudeDdmmmm,     s.LatitudeDdmmmmWidth);
        ApplyCol(ColLongitudeDdmmmm,    s.ShowLongitudeDdmmmm,    s.LongitudeDdmmmmWidth);
        ApplyCol(ColBasicTransferRates, s.ShowBasicTransferRates, s.BasicTransferRatesWidth);
        ApplyCol(ColOtherTransferRates, s.ShowOtherTransferRates, s.OtherTransferRatesWidth);
        ApplyCol(ColFirstActive,        s.ShowFirstActive,        s.FirstActiveWidth);
        ApplyCol(ColLastActive,         s.ShowLastActive,         s.LastActiveWidth);
    }

    private static void ApplyCol(DataGridColumn col, bool visible, int width)
    {
        col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        col.Width = new DataGridLength(width);
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is TreeNodeViewModel node)
            vm.SelectedTreeviewNode = node;
    }
}
