using System.Reflection;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Extensions.DependencyInjection;

namespace WgServerforWindows.Controls
{
    /// <summary>
    /// Interaction logic for SplashScreen.xaml
    /// </summary>
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Use BeginInvoke to put it at the end of the message queue.
            // In other words, let the splash screen have full priority in loading first.
            Dispatcher.BeginInvoke(() =>
            {
                WaitCursor.SetOverrideCursor(null);
                WaitCursor.IgnoreOverrideCursor = true;
                App.Current.Services.GetService<Views.MainShell>().Show();
            });
        }

        public string Version => Assembly.GetEntryAssembly().GetName().Version.ToString();
    }
}
