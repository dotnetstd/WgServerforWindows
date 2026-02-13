using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WgServerforWindows.Models;

namespace WgServerforWindows.Views
{
    public partial class SettingsView : UserControl
    {
        private bool _isInitialized = false;

        public SettingsView(MainWindowModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Initialize language selection
            _isInitialized = false;
            var currentLang = GlobalAppSettings.Instance.Language;
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            _isInitialized = true;

            // Ensure settings are saved when leaving or property changes
            Unloaded += (s, e) => AppSettings.Instance.Save();
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedLang = selectedItem.Tag.ToString();
                if (selectedLang != GlobalAppSettings.Instance.Language)
                {
                    GlobalAppSettings.Instance.Language = selectedLang;
                    GlobalAppSettings.Instance.Save();

                    var result = MessageBox.Show(
                        Properties.Resources.RestartRequiredText,
                        Properties.Resources.RestartRequiredTitle,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
                        Application.Current.Shutdown();
                    }
                }
            }
        }
    }
}