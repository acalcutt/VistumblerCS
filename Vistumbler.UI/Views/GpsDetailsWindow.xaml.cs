using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class GpsDetailsWindow : Window
{
    public GpsDetailsWindow(GpsDetailsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
