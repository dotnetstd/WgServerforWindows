using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using GalaSoft.MvvmLight;
using Jot;
using Jot.Storage;

namespace WgServerforWindows.Models
{
    /// <summary>
    /// Defines application-wide settings which will be persisted across sessions
    /// </summary>
    public class AppSettings : ObservableObject
    {
        #region Singleton member

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static AppSettings Instance { get; } = new AppSettings();

        #endregion

        #region Private constructor

        /// <summary>
        /// Constructor
        /// </summary>
        private AppSettings()
        {
            // Set up Window tracking
            Tracker.Configure<Window>()
                .Id(w => w.Name, new Size(SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))
                .Properties(w => new { w.Top, w.Width, w.Height, w.Left, w.WindowState })
                .PersistOn(nameof(Window.Closing))
                .StopTrackingOn(nameof(Window.Closing))
                .WhenPersistingProperty((w, p) => p.Cancel = (p.Property == nameof(w.WindowState) && w.WindowState == WindowState.Minimized) ||
                                                             (w.WindowState == WindowState.Maximized && (p.Property == nameof(w.Top) || p.Property == nameof(w.Left) || p.Property == nameof(w.Height) || p.Property == nameof(w.Width))));
        }

        #endregion

        #region Private fields

        private bool _isLoading = false;

        #endregion

        #region Public methods

        public void Load()
        {
            _isLoading = true;
            
            // Set up AppSettings tracking
            Tracker.Configure<AppSettings>()
                .Property(a => a.CustomServerConfigDirectory)
                .Property(a => a.CustomClientConfigDirectory)
                .Property(a => a.ClientConfigurationExpansionStates)
                .Property(a => a.IsDynamicIpSyncEnabled)
                .Property(a => a.IsAutoStartEnabled)
                .Property(a => a.IsAutoEnableNatOnStartup)
                .Property(a => a.IsPublicIpCheckOnStartup)
                .Property(a => a.IsAutoLoginEnabled)
                .Property(a => a.AutoLoginUsername)
                .Property(a => a.AutoLoginPassword)
                .Track(this);
                
            _isLoading = false;
        }

        public void Save()
        {
            Tracker.Persist(this);
        }

        #endregion

        #region Public properties

        /// <summary>
        /// The parent directory of the server configuration files
        /// </summary>
        public string CustomServerConfigDirectory
        {
            get => _customServerConfigDirectory;
            set => Set(nameof(CustomServerConfigDirectory), ref _customServerConfigDirectory, value);
        }
        private string _customServerConfigDirectory;

        /// <summary>
        /// The parent directory of the client configuration files
        /// </summary>
        public string CustomClientConfigDirectory
        {
            get => _customClientConfigDirectory;
            set => Set(nameof(CustomClientConfigDirectory), ref _customClientConfigDirectory, value);
        }
        private string _customClientConfigDirectory;

        /// <summary>
        /// Tracks whether each client configuration is expanded in the UI or not
        /// </summary>
        public Dictionary<string, bool> ClientConfigurationExpansionStates = new Dictionary<string, bool>();

        /// <summary>
        /// Whether dynamic IP sync is enabled
        /// </summary>
        public bool IsDynamicIpSyncEnabled
        {
            get => _isDynamicIpSyncEnabled;
            set
            {
                if (Set(nameof(IsDynamicIpSyncEnabled), ref _isDynamicIpSyncEnabled, value))
                {
                    Save();
                }
            }
        }
        private bool _isDynamicIpSyncEnabled;

        /// <summary>
        /// Whether the application should start automatically with Windows
        /// </summary>
        public bool IsAutoStartEnabled
        {
            get => _isAutoStartEnabled;
            set
            {
                if (Set(nameof(IsAutoStartEnabled), ref _isAutoStartEnabled, value))
                {
                    UpdateAutoStart(value);
                    Save();
                }
            }
        }
        private bool _isAutoStartEnabled;

        /// <summary>
        /// Whether to automatically enable NAT routing on application startup (when prerequisites are satisfied)
        /// </summary>
        public bool IsAutoEnableNatOnStartup
        {
            get => _isAutoEnableNatOnStartup;
            set
            {
                if (Set(nameof(IsAutoEnableNatOnStartup), ref _isAutoEnableNatOnStartup, value))
                {
                    Save();
                }
            }
        }
        private bool _isAutoEnableNatOnStartup;

        /// <summary>
        /// Whether to automatically perform a public IP check and sync endpoint on startup
        /// </summary>
        public bool IsPublicIpCheckOnStartup
        {
            get => _isPublicIpCheckOnStartup;
            set
            {
                if (Set(nameof(IsPublicIpCheckOnStartup), ref _isPublicIpCheckOnStartup, value))
                {
                    Save();
                }
            }
        }
        private bool _isPublicIpCheckOnStartup;

        /// <summary>
        /// Whether auto-login is enabled
        /// </summary>
        public bool IsAutoLoginEnabled
        {
            get => _isAutoLoginEnabled;
            set
            {
                if (Set(nameof(IsAutoLoginEnabled), ref _isAutoLoginEnabled, value))
                {
                    if (!_isLoading)
                    {
                        UpdateAutoLogin(value);
                    }
                    Save();
                }
            }
        }
        private bool _isAutoLoginEnabled;

        /// <summary>
        /// Auto-login username
        /// </summary>
        public string AutoLoginUsername
        {
            get => _autoLoginUsername ?? string.Empty;
            set
            {
                if (Set(nameof(AutoLoginUsername), ref _autoLoginUsername, value))
                {
                    Save();
                }
            }
        }
        private string _autoLoginUsername;

        /// <summary>
        /// Auto-login password
        /// </summary>
        public string AutoLoginPassword
        {
            get => _autoLoginPassword ?? string.Empty;
            set
            {
                if (Set(nameof(AutoLoginPassword), ref _autoLoginPassword, value))
                {
                    Save();
                }
            }
        }
        private string _autoLoginPassword;

        private void UpdateAutoStart(bool enable)
        {
            string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
            string appName = "WS4W";
            string appPath = $"\"{Environment.ProcessPath}\" --minimized";

            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (enable)
                    {
                        key.SetValue(appName, appPath);
                    }
                    else
                    {
                        key.DeleteValue(appName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update auto-start: {ex.Message}");
            }
        }

        private bool ValidateCredentials(string username, string password)
        {
            try
            {
                using (var context = new System.DirectoryServices.AccountManagement.PrincipalContext(
                    System.DirectoryServices.AccountManagement.ContextType.Machine))
                {
                    return context.ValidateCredentials(username, password);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"凭据验证失败: {ex.Message}");
                return false;
            }
        }

        private void UpdateAutoLogin(bool enable)
        {
            string winlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(winlogonKey, true))
                {
                    if (key == null)
                    {
                        throw new Exception("无法打开Winlogon注册表键。请确保以管理员权限运行此应用程序。");
                    }
                    
                    if (enable)
                    {
                        // 验证输入
                        if (string.IsNullOrWhiteSpace(AutoLoginUsername))
                        {
                            throw new Exception("请输入有效的用户名。");
                        }
                        
                        if (string.IsNullOrWhiteSpace(AutoLoginPassword))
                        {
                            throw new Exception("请输入有效的密码。");
                        }
                        
                        // 验证凭据有效性
                        if (!ValidateCredentials(AutoLoginUsername, AutoLoginPassword))
                        {
                            throw new Exception("输入的用户名或密码无效，请检查并重新输入。");
                        }
                        
                        // 启用自动登录
                        key.SetValue("AutoAdminLogon", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        key.SetValue("DefaultUserName", AutoLoginUsername);
                        key.SetValue("DefaultPassword", AutoLoginPassword);
                        
                        // 添加DefaultDomainName（如果不存在）
                        if (key.GetValue("DefaultDomainName") == null)
                        {
                            try
                            {
                                key.SetValue("DefaultDomainName", Environment.MachineName);
                            }
                            catch { /* 忽略域设置错误 */ }
                        }
                        
                        // 确保AutoLogonCount设置（防止某些系统限制）
                        try
                        {
                            key.SetValue("AutoLogonCount", 9999, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                        catch { /* 忽略此设置错误 */ }
                        
                        // 对于Windows Server 2025，可能需要额外的设置
                        try
                        {
                            // 禁用安全登录（如果启用）
                            key.SetValue("ForceAutoLogon", 1, Microsoft.Win32.RegistryValueKind.DWord);
                        }
                        catch { /* 忽略此设置错误 */ }
                    }
                    else
                    {
                        // 禁用自动登录
                        try { key.DeleteValue("AutoAdminLogon", false); } catch { }
                        try { key.DeleteValue("DefaultUserName", false); } catch { }
                        try { key.DeleteValue("DefaultPassword", false); } catch { }
                        try { key.DeleteValue("AutoLogonCount", false); } catch { }
                        try { key.DeleteValue("ForceAutoLogon", false); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"更新自动登录设置失败: {ex.Message}";
                
                if (ex is UnauthorizedAccessException)
                {
                    errorMessage += "\n\n请确保以管理员权限运行此应用程序。";
                }
                
                System.Diagnostics.Debug.WriteLine($"Failed to update auto-login: {ex.Message}");
                MessageBox.Show(errorMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// The public tracker instance. Can be used to track things other than the <see cref="Instance"/>.
        /// </summary>
        public Tracker Tracker { get; } = new Tracker(new JsonFileStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WS4W")));


        #endregion
    }
}
