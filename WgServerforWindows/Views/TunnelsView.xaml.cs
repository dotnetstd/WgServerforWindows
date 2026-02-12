using System.Windows.Controls;
using WgServerforWindows.Models;

namespace WgServerforWindows.Views
{
    public partial class TunnelsView : UserControl
    {
        public TunnelsView(MainWindowModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
