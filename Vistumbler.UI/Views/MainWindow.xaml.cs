using System.Windows;
using System.Windows.Controls;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel vm && e.NewValue is TreeNodeViewModel node)
            vm.SelectedTreeviewNode = node;
    }
}
