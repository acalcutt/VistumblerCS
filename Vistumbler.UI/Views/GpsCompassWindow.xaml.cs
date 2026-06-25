using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class GpsCompassWindow : Window
{
    public GpsCompassWindow(GpsDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
