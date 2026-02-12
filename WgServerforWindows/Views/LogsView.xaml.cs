using System.Windows.Controls;
using WgServerforWindows.Models;

namespace WgServerforWindows.Views
{
    public partial class LogsView : UserControl
    {
        public LogsView(LogsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
