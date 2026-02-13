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

            StateChanged += MainShell_StateChanged;
            Closing += MainShell_Closing;
        }

        private void MainShell_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void MainShell_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If the user is closing the window, we actually just want to hide it
            // unless the app is shutting down.
            if (AppSettings.Instance.IsAutoStartEnabled)
            {
                e.Cancel = true;
                Hide();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            AppSettings.Instance.Tracker.Track(this);
        }
    }
}
