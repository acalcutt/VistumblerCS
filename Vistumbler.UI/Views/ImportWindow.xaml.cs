using System.Windows;
using Vistumbler.UI.ViewModels;

namespace Vistumbler.UI.Views
{
    public partial class ImportWindow : Window
    {
        public ImportWindow(ImportViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            
            // Wire up the close action
            if (viewModel.CloseAction == null)
            {
                viewModel.CloseAction = new System.Action(this.Close);
            }
        }
    }
}
