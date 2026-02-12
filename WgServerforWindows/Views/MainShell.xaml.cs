using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using WgServerforWindows.Models;

namespace WgServerforWindows.Views
{
    public partial class MainShell : Window
    {
        public MainShell(MainShellViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            AppSettings.Instance.Tracker.Track(this);
        }
    }
}
