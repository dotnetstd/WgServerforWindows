using System.Windows;
using WgServerforWindows.Models;

namespace WgServerforWindows.Views
{
    public partial class MtuWizardWindow : Window
    {
        public MtuWizardWindow(MtuWizardViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}