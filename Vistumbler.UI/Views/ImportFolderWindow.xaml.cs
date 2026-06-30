using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class ImportFolderWindow : Window
{
    public ImportFolderWindow(ImportFolderViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseAction = Close;
    }
}
