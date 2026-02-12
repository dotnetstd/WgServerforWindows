using System.Windows.Controls;
using WgServerforWindows.Models;

namespace WgServerforWindows.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView(DashboardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
