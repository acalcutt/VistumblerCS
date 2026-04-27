using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class SignalHistoryWindow : Window
{
    public SignalHistoryWindow(AccessPointViewModel apViewModel)
    {
        InitializeComponent();
        DataContext = apViewModel;
        HeaderText.Text = $"Signal History — {apViewModel.Ssid}  ({apViewModel.Bssid})";
    }
}
