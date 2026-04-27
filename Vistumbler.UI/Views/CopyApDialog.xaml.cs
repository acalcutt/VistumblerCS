using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views;

public partial class CopyApDialog : Window
{
    public CopyApDialog(CopyFieldSelection fields)
    {
        InitializeComponent();
        DataContext = fields;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)     => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
