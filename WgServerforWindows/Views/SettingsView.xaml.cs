using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WgServerforWindows.Models;
using WgServerforWindows.Services.Interfaces;

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

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.PasswordBox passwordBox)
            {
                AppSettings.Instance.AutoLoginPassword = passwordBox.Password;
            }
        }

        private void ValidateCredentialsButton_Click(object sender, RoutedEventArgs e)
        {
            string username = AppSettings.Instance.AutoLoginUsername;
            string password = AppSettings.Instance.AutoLoginPassword;

            // Validate input
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("请输入有效的用户名。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("请输入有效的密码。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate credentials
            try
            {
                using (var context = new System.DirectoryServices.AccountManagement.PrincipalContext(
                    System.DirectoryServices.AccountManagement.ContextType.Machine))
                {
                    bool isValid = context.ValidateCredentials(username, password);

                    if (isValid)
                    {
                        MessageBox.Show("凭据验证成功！", "验证成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("输入的用户名或密码无效，请检查并重新输入。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"验证过程中发生错误: {ex.Message}", "验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnableAutoDesktopLoginButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowModel)DataContext;
            viewModel.OperationLogService.Log("尝试启用自动进入桌面功能");

            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                viewModel.OperationLogService.Log("启用自动进入桌面失败: 权限不足");
                MessageBox.Show("请以管理员权限运行此应用程序以启用自动进入桌面功能。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get current username
            string currentUsername = Environment.UserName;
            string password = string.Empty;

            // Prompt for password
            var passwordWindow = new Window
            {
                Title = "输入密码",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            var stackPanel = new StackPanel { Margin = new Thickness(20) };
            stackPanel.Children.Add(new TextBlock { Text = $"请输入用户 '{currentUsername}' 的密码以启用自动进入桌面:", Margin = new Thickness(0, 0, 0, 10) });
            var passwordBox = new System.Windows.Controls.PasswordBox { Width = 300, Margin = new Thickness(0, 0, 0, 10) };
            stackPanel.Children.Add(passwordBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "确定", Width = 100, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new Button { Content = "取消", Width = 100 };
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);

            passwordWindow.Content = stackPanel;

            bool? dialogResult = false;
            okButton.Click += (s, e2) => { password = passwordBox.Password; dialogResult = true; passwordWindow.Close(); };
            cancelButton.Click += (s, e2) => { dialogResult = false; passwordWindow.Close(); };

            passwordWindow.ShowDialog();

            if (dialogResult != true || string.IsNullOrWhiteSpace(password))
            {
                viewModel.OperationLogService.Log("启用自动进入桌面取消: 用户未输入密码");
                return;
            }

            // Validate credentials
            try
            {
                using (var context = new System.DirectoryServices.AccountManagement.PrincipalContext(
                    System.DirectoryServices.AccountManagement.ContextType.Machine))
                {
                    bool isValid = context.ValidateCredentials(currentUsername, password);

                    if (!isValid)
                    {
                        viewModel.OperationLogService.Log("启用自动进入桌面失败: 密码验证失败");
                        MessageBox.Show("输入的密码无效，请检查并重新输入。", "验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                viewModel.OperationLogService.Log($"启用自动进入桌面失败: 验证过程中发生错误 - {ex.Message}");
                MessageBox.Show($"验证过程中发生错误: {ex.Message}", "验证失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Modify registry to enable auto-login
            try
            {
                string winlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
                
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(winlogonKey, true))
                {
                    if (key == null)
                    {
                        throw new Exception("无法打开Winlogon注册表键。");
                    }

                    // Enable auto-login
                    key.SetValue("AutoAdminLogon", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("DefaultUserName", currentUsername);
                    key.SetValue("DefaultPassword", password);
                    
                    // Add DefaultDomainName if not exists
                    if (key.GetValue("DefaultDomainName") == null)
                    {
                        key.SetValue("DefaultDomainName", Environment.MachineName);
                    }

                    // Set AutoLogonCount to prevent some system limitations
                    key.SetValue("AutoLogonCount", 9999, Microsoft.Win32.RegistryValueKind.DWord);
                    
                    // Enable ForceAutoLogon for better compatibility
                    key.SetValue("ForceAutoLogon", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }

                viewModel.OperationLogService.Log($"自动进入桌面功能已成功启用，用户: {currentUsername}");
                MessageBox.Show("自动进入桌面功能已成功启用！系统重启后将自动登录并进入桌面。", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                string errorMessage = $"启用自动进入桌面失败: {ex.Message}";
                viewModel.OperationLogService.Log(errorMessage);
                if (ex is UnauthorizedAccessException)
                {
                    errorMessage += "\n\n请确保以管理员权限运行此应用程序。";
                }
                MessageBox.Show(errorMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenNetplwizButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowModel)DataContext;
            viewModel.OperationLogService.Log("尝试打开用户账户设置(netplwiz)");
            
            try
            {
                // Open netplwiz utility
                System.Diagnostics.Process.Start("netplwiz");
                viewModel.OperationLogService.Log("用户账户设置(netplwiz)已成功打开");
            }
            catch (Exception ex)
            {
                viewModel.OperationLogService.Log($"打开用户账户设置失败: {ex.Message}");
                MessageBox.Show($"无法打开用户账户设置: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreAutoDesktopLoginButton_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = (MainWindowModel)DataContext;
            viewModel.OperationLogService.Log("尝试还原自动进入桌面设置");

            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                viewModel.OperationLogService.Log("还原自动进入桌面设置失败: 权限不足");
                MessageBox.Show("请以管理员权限运行此应用程序以还原设置。", "权限不足", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Confirm action
            var result = MessageBox.Show("确定要还原自动进入桌面设置吗？这将禁用自动登录功能。", "确认还原", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                viewModel.OperationLogService.Log("还原自动进入桌面设置取消: 用户未确认");
                return;
            }

            // Modify registry to disable auto-login
            try
            {
                string winlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
                
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(winlogonKey, true))
                {
                    if (key == null)
                    {
                        throw new Exception("无法打开Winlogon注册表键。");
                    }

                    // Disable auto-login
                    try { key.DeleteValue("AutoAdminLogon", false); } catch { }
                    try { key.DeleteValue("DefaultUserName", false); } catch { }
                    try { key.DeleteValue("DefaultPassword", false); } catch { }
                    try { key.DeleteValue("AutoLogonCount", false); } catch { }
                    try { key.DeleteValue("ForceAutoLogon", false); } catch { }
                }

                viewModel.OperationLogService.Log("自动进入桌面设置已成功还原");
                MessageBox.Show("自动进入桌面设置已成功还原！系统重启后将需要手动登录。", "操作成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                string errorMessage = $"还原设置失败: {ex.Message}";
                viewModel.OperationLogService.Log(errorMessage);
                if (ex is UnauthorizedAccessException)
                {
                    errorMessage += "\n\n请确保以管理员权限运行此应用程序。";
                }
                MessageBox.Show(errorMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool IsRunningAsAdministrator()
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
    }
}