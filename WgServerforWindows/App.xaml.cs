using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Drawing;
using System.Windows.Forms;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using SharpConfig;
using WgServerforWindows.Cli.Options;
using WgServerforWindows.Controls;
using WgServerforWindows.Extensions;
using WgServerforWindows.Models;
using WgServerforWindows.Views;
using WgServerforWindows.Services.Interfaces;
using WgServerforWindows.Services;
using SplashScreen = WgServerforWindows.Controls.SplashScreen;

namespace WgServerforWindows
{
    public partial class App : System.Windows.Application, IDisposable
    {
        public IServiceProvider Services { get; private set; }

        public new static App Current => (App)System.Windows.Application.Current;

        private static System.Threading.Mutex? _mutex;
        private NotifyIcon _notifyIcon;
        private IOperationLogService _operationLogService;
        private BackgroundTaskManager _backgroundTaskManager;
        private bool _disposed = false;

        public App()
        {
            try
            {
                // Initialize Services first
                Services = ConfigureServices();
                
                // Get the operation log service
                _operationLogService = Services.GetService<IOperationLogService>();
                
                try
                {
                    AppSettings.Instance.Load();
                    _operationLogService.Log("Application settings loaded successfully");
                }
                catch (Exception ex)
                {
                    _operationLogService.LogException("Failed to load application settings", ex, LogLevel.Warning);
                }
                
                // Register exception handlers
                DispatcherUnhandledException += Application_DispatcherUnhandledException;
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Critical error during application initialization: {ex}");
                System.Windows.MessageBox.Show($"Critical error during application initialization: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                if (e.ExceptionObject is Exception ex)
                {
                    _operationLogService?.LogException("FATAL: Unhandled domain exception - Application will terminate", ex, LogLevel.Error);
                    _operationLogService?.Flush();
                }
            }
            catch
            {
                WriteEmergencyLog("CurrentDomain_UnhandledException", e.ExceptionObject?.ToString() ?? "Unknown error");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                _operationLogService?.LogException("Unobserved task exception", e.Exception, LogLevel.Error);
                _operationLogService?.Flush();
                e.SetObserved();
            }
            catch
            {
                WriteEmergencyLog("TaskScheduler_UnobservedTaskException", e.Exception?.ToString() ?? "Unknown error");
            }
        }

        private static void WriteEmergencyLog(string source, string message)
        {
            try
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WS4W", "Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                
                string emergencyLogFile = Path.Combine(logDirectory, "emergency.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [EMERGENCY] [{source}] {message}{Environment.NewLine}";
                File.AppendAllText(emergencyLogFile, logEntry, System.Text.Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // ViewModels
            services.AddSingleton<MainWindowModel>(sp => new MainWindowModel(
                sp.GetRequiredService<INetworkService>(),
                sp.GetRequiredService<IOperationLogService>()
            ));
            services.AddSingleton<MainShellViewModel>();
            services.AddSingleton<DashboardViewModel>();
            services.AddTransient<LogsViewModel>();
            services.AddTransient<MtuWizardViewModel>();

            // Views
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainShell>();
            services.AddSingleton<DashboardView>();
            services.AddSingleton<TunnelsView>();
            services.AddSingleton<LogsView>();
            services.AddSingleton<SettingsView>();
            services.AddTransient<MtuWizardWindow>();

            // Register Services
            services.AddSingleton<IToastService, ToastService>();
            services.AddSingleton<IDynamicEndpointService, DynamicEndpointService>();
            services.AddSingleton<ILogService, LogService>();
            services.AddSingleton<IOperationLogService, OperationLogService>();
            services.AddSingleton<INetworkService, NetworkService>();
            services.AddSingleton<BackgroundTaskManager>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            _operationLogService.LogMethodEntry(nameof(OnStartup));
            
            try
            {
                bool createdNew;
                _mutex = new System.Threading.Mutex(true, "WgServerforWindows", out createdNew);

                if (!createdNew)
                {
                    _operationLogService.Log("Application already running, shutting down this instance");
                    System.Windows.MessageBox.Show("WgServerforWindows is already running.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    Current.Shutdown();
                    return;
                }

                // Load language setting
                LoadLanguageSetting();

                _operationLogService.Log("Application started");
                
                // Initialize prerequisites
                InitializePrerequisites();

                base.OnStartup(e);

                bool startMinimized = e.Args.Contains("--minimized");
                var args = e.Args.Where(a => a != "--minimized").ToArray();

                // Initialize NotifyIcon
                InitializeNotifyIcon();

                if (args.Any())
                {
                    HandleCommandLineArgs(args);
                }
                else
                {
                    ShowMainWindow(startMinimized);
                }

                // Start background task manager
                StartBackgroundTasks();
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error during application startup", ex);
                System.Windows.MessageBox.Show($"Error during startup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            
            _operationLogService.LogMethodExit(nameof(OnStartup));
        }

        private void LoadLanguageSetting()
        {
            var language = GlobalAppSettings.Instance.Language;
            if (!string.IsNullOrEmpty(language))
            {
                try
                {
                    var culture = new System.Globalization.CultureInfo(language);
                    Thread.CurrentThread.CurrentCulture = culture;
                    Thread.CurrentThread.CurrentUICulture = culture;
                    _operationLogService.Log($"Language set to: {language}");
                }
                catch (Exception ex)
                {
                    _operationLogService.LogException($"Failed to set language: {language}", ex, LogLevel.Warning);
                }
            }
        }

        private void InitializePrerequisites()
        {
            _operationLogService.LogMethodEntry(nameof(InitializePrerequisites));
            
            try
            {
                // Auto enable NAT on startup (if enabled and prerequisites are satisfied)
                if (AppSettings.Instance.IsAutoEnableNatOnStartup)
                {
                    InitializeNat();
                }

                // Auto check and sync public IP on startup
                if (AppSettings.Instance.IsPublicIpCheckOnStartup)
                {
                    InitializePublicIpCheck();
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error initializing prerequisites", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(InitializePrerequisites));
        }

        private void InitializeNat()
        {
            _operationLogService.LogMethodEntry(nameof(InitializeNat));
            
            try
            {
                var networkService = Services.GetService<INetworkService>();
                var serverConfigurationPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                var tunnelServicePrerequisite = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
                var persistentInternetSharingPrerequisite = new PersistentInternetSharingPrerequisite(networkService);
                var newNetNatPrerequisite = new NewNetNatPrerequisite(networkService);

                _operationLogService.Log("Checking if NAT should be auto-enabled on startup");

                if (newNetNatPrerequisite.IsSupported
                    && serverConfigurationPrerequisite.Fulfilled
                    && tunnelServicePrerequisite.Fulfilled
                    && !internetSharingPrerequisite.Fulfilled
                    && !persistentInternetSharingPrerequisite.Fulfilled
                    && !newNetNatPrerequisite.Fulfilled)
                {
                    _operationLogService.Log("Auto-enabling NAT on startup");
                    try
                    {
                        newNetNatPrerequisite.Resolve(null, false);
                        _operationLogService.Log("NAT auto-enabled successfully");
                    }
                    catch (Exception ex)
                    {
                        _operationLogService.LogException("Failed to auto-enable NAT", ex, LogLevel.Error);
                    }
                }
                else
                {
                    _operationLogService.Log($"NAT auto-enable skipped: IsSupported={newNetNatPrerequisite.IsSupported}, ServerConfig={serverConfigurationPrerequisite.Fulfilled}, TunnelService={tunnelServicePrerequisite.Fulfilled}, InternetSharing={internetSharingPrerequisite.Fulfilled}, PersistentInternetSharing={persistentInternetSharingPrerequisite.Fulfilled}, NetNat={newNetNatPrerequisite.Fulfilled}");
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error in NAT initialization", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(InitializeNat));
        }

        private void InitializePublicIpCheck()
        {
            _operationLogService.LogMethodEntry(nameof(InitializePublicIpCheck));
            
            try
            {
                var networkService = Services.GetService<INetworkService>();
                var dynamicEndpointService = Services.GetService<IDynamicEndpointService>();
                var serverConfigurationPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                var tunnelServicePrerequisite = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                var newNetNatPrerequisite = new NewNetNatPrerequisite(networkService);
                var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
                var persistentInternetSharingPrerequisite = new PersistentInternetSharingPrerequisite(networkService);
                
                _operationLogService.Log("Checking if public IP should be auto-checked on startup");
                
                bool isServerConfigValid = serverConfigurationPrerequisite.Fulfilled;
                bool isTunnelServiceValid = tunnelServicePrerequisite.Fulfilled;
                bool isNatValid = newNetNatPrerequisite.IsSupported ? newNetNatPrerequisite.Fulfilled : (internetSharingPrerequisite.Fulfilled || persistentInternetSharingPrerequisite.Fulfilled);
                
                _operationLogService.Log($"Configuration check results: ServerConfigValid={isServerConfigValid}, TunnelServiceValid={isTunnelServiceValid}, NatValid={isNatValid}");
                
                if (isServerConfigValid && isTunnelServiceValid && isNatValid)
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var delay = GlobalAppSettings.Instance.BootTaskDelay;
                            if (delay > TimeSpan.Zero)
                            {
                                _operationLogService.Log($"Waiting for {delay.TotalSeconds} seconds before checking public IP");
                                await System.Threading.Tasks.Task.Delay(delay);
                            }
                            
                            _operationLogService.Log("Checking public IP address");
                            var currentIp = await dynamicEndpointService.GetPublicIpv6Async();
                            
                            if (!string.IsNullOrEmpty(currentIp))
                            {
                                _operationLogService.Log($"Public IP address found: {currentIp}");
                                _operationLogService.Log("Updating server endpoint with new public IP");
                                await dynamicEndpointService.UpdateEndpointAsync(currentIp);
                                _operationLogService.Log("Server endpoint updated successfully");
                            }
                            else
                            {
                                _operationLogService.Log("No public IP address found", LogLevel.Warning);
                            }
                            
                            // Auto save and sync configuration
                            await AutoSaveAndSyncConfiguration(networkService);
                        }
                        catch (Exception ex)
                        {
                            _operationLogService.LogException("Error during public IP check and sync", ex);
                        }
                    });
                }
                else
                {
                    _operationLogService.Log("Public IP auto-check skipped: prerequisites not met");
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error in public IP check initialization", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(InitializePublicIpCheck));
        }

        private async System.Threading.Tasks.Task AutoSaveAndSyncConfiguration(INetworkService networkService)
        {
            _operationLogService.LogMethodEntry(nameof(AutoSaveAndSyncConfiguration));
            
            try
            {
                _operationLogService.Log("Auto saving and syncing server configuration");
                
                // Load current server configuration
                var serverConfig = new ServerConfiguration().Load<ServerConfiguration>(Configuration.LoadFromFile(ServerConfigurationPrerequisite.ServerDataPath));
                
                // Save to data file
                serverConfig.ToConfiguration().SaveToFile(ServerConfigurationPrerequisite.ServerDataPath);
                _operationLogService.Log("Server configuration saved to data file");
                
                // Save to WG config file
                var wgConfig = serverConfig.ToConfiguration<ServerConfiguration>();
                
                // Merge client configurations if they exist
                if (Directory.Exists(ClientConfigurationsPrerequisite.ClientDataDirectory))
                {
                    foreach (string clientFile in Directory.GetFiles(ClientConfigurationsPrerequisite.ClientDataDirectory, "*.conf"))
                    {
                        try
                        {
                            var clientConfig = new ClientConfiguration(null).Load<ClientConfiguration>(Configuration.LoadFromFile(clientFile));
                            if (clientConfig.IsEnabledProperty.Value == true.ToString())
                            {
                                wgConfig = wgConfig.Merge(clientConfig.ToConfiguration<ServerConfiguration>());
                            }
                        }
                        catch (Exception ex)
                        {
                            _operationLogService.LogException($"Failed to load client config: {clientFile}", ex, LogLevel.Warning);
                        }
                    }
                    _operationLogService.Log("Client configurations merged");
                }
                
                wgConfig.SaveToFile(ServerConfigurationPrerequisite.ServerWGPath);
                _operationLogService.Log("Server configuration saved to WG file");
                
                // Sync configuration to tunnel service
                using (TemporaryFile tempFile = new TemporaryFile(ServerConfigurationPrerequisite.ServerWGPath, ServerConfigurationPrerequisite.ServerWGPathWithCustomTunnelName))
                {
                    networkService.SyncConfiguration(GlobalAppSettings.Instance.TunnelServiceName, tempFile.NewFilePath);
                    _operationLogService.Log("Server configuration synced to tunnel service");
                }
                
                _operationLogService.Log("Auto save and sync completed successfully");
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error during auto save and sync", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(AutoSaveAndSyncConfiguration));
        }

        private void InitializeNotifyIcon()
        {
            _operationLogService.LogMethodEntry(nameof(InitializeNotifyIcon));
            
            try
            {
                _notifyIcon = new NotifyIcon
                {
                    Icon = new Icon(GetResourceStream(new Uri("pack://application:,,,/Images/logo.ico")).Stream),
                    Visible = true,
                    Text = "WS4W"
                };
                _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();
                _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
                _notifyIcon.ContextMenuStrip.Items.Add(WgServerforWindows.Properties.Resources.Dashboard, null, (s, args) => ShowMainWindow());
                _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
                _notifyIcon.ContextMenuStrip.Items.Add(WgServerforWindows.Properties.Resources.Close, null, (s, args) => 
                {
                    var mainShell = App.Current.Services.GetService<MainShell>();
                    mainShell.Shutdown();
                    Shutdown();
                });
                
                _operationLogService.Log("Notify icon initialized");
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Failed to initialize notify icon", ex, LogLevel.Warning);
            }
            
            _operationLogService.LogMethodExit(nameof(InitializeNotifyIcon));
        }

        private void HandleCommandLineArgs(string[] args)
        {
            _operationLogService.LogMethodEntry(nameof(HandleCommandLineArgs), string.Join(" ", args));
            
            try
            {
                // First, handle UI-related args
                var uiArgsParsed = Parser.Default.ParseArguments<StatusCommand, object>(args)
                    .WithParsed<StatusCommand>(Status);

                if (uiArgsParsed.Tag != ParserResultType.Parsed)
                {
                    // Otherwise, try non-UI args.
                    DispatcherUnhandledException -= Application_DispatcherUnhandledException;

                    Parser.Default.ParseArguments<RestartInternetSharingCommand, SetPathCommand, SetNetIpAddressCommand, PrivateNetworkCommand>(args)
                        .WithParsed<RestartInternetSharingCommand>(RestartInternetSharing)
                        .WithParsed<SetPathCommand>(SetPath)
                        .WithParsed<SetNetIpAddressCommand>(SetNetIpAddress)
                        .WithParsed<PrivateNetworkCommand>(PrivateNetwork);

                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error handling command line args", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(HandleCommandLineArgs));
        }

        private void ShowMainWindow(bool startMinimized = false)
        {
            _operationLogService.LogMethodEntry(nameof(ShowMainWindow), startMinimized);
            
            try
            {
                var mainShell = App.Current.Services.GetService<MainShell>();
                if (startMinimized)
                {
                    mainShell.WindowState = WindowState.Minimized;
                    mainShell.Hide();
                }
                else
                {
                    mainShell.Show();
                    if (mainShell.WindowState == WindowState.Minimized)
                    {
                        mainShell.WindowState = WindowState.Normal;
                    }
                    mainShell.Activate();
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error showing main window", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(ShowMainWindow));
        }

        private void StartBackgroundTasks()
        {
            _operationLogService.LogMethodEntry(nameof(StartBackgroundTasks));
            
            try
            {
                _backgroundTaskManager = Services.GetService<BackgroundTaskManager>();

                // Register task for checking and enabling NAT
                _backgroundTaskManager.RegisterTask(
                    "CheckAndEnableNAT",
                    async () => {
                        try
                        {
                            var networkService = Services.GetService<INetworkService>();
                            var serverConfigPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                            var tunnelServicePrereq = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                            var internetSharingPrereq = new InternetSharingPrerequisite(networkService);
                            var persistentInternetSharingPrereq = new PersistentInternetSharingPrerequisite(networkService);
                            var newNetNatPrereq = new NewNetNatPrerequisite(networkService);
                            
                            if (AppSettings.Instance.IsAutoEnableNatOnStartup &&
                                newNetNatPrereq.IsSupported &&
                                serverConfigPrerequisite.Fulfilled &&
                                tunnelServicePrereq.Fulfilled &&
                                !internetSharingPrereq.Fulfilled &&
                                !persistentInternetSharingPrereq.Fulfilled &&
                                !newNetNatPrereq.Fulfilled)
                            {
                                newNetNatPrereq.Resolve(null, false);
                                return true;
                            }
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _operationLogService.LogException("Background NAT enable failed", ex, LogLevel.Error);
                            return false;
                        }
                    },
                    TimeSpan.FromMinutes(AppSettings.Instance.NatCheckIntervalMinutes),
                    AppSettings.Instance.MaxTaskRetries
                );

                // Register task for checking public IP
                _backgroundTaskManager.RegisterTask(
                    "CheckPublicIP",
                    async () => {
                        try
                        {
                            var networkService = Services.GetService<INetworkService>();
                            var dynamicEndpointService = Services.GetService<IDynamicEndpointService>();
                            var serverConfigPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                            var tunnelServicePrereq = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                            
                            if (AppSettings.Instance.IsPublicIpCheckOnStartup &&
                                serverConfigPrerequisite.Fulfilled &&
                                tunnelServicePrereq.Fulfilled)
                            {
                                var currentIp = await dynamicEndpointService.GetPublicIpv6Async();
                                if (!string.IsNullOrEmpty(currentIp))
                                {
                                    await dynamicEndpointService.UpdateEndpointAsync(currentIp);
                                    return true;
                                }
                                return false;
                            }
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _operationLogService.LogException("Background public IP check failed", ex, LogLevel.Error);
                            return false;
                        }
                    },
                    TimeSpan.FromMinutes(AppSettings.Instance.PublicIpCheckIntervalMinutes),
                    AppSettings.Instance.MaxTaskRetries
                );

                // Register task for saving and syncing configuration
                _backgroundTaskManager.RegisterTask(
                    "ConfigurationSync",
                    async () => {
                        try
                        {
                            var networkService = Services.GetService<INetworkService>();
                            var serverConfigPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                            var tunnelServicePrereq = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                            
                            if (serverConfigPrerequisite.Fulfilled && tunnelServicePrereq.Fulfilled)
                            {
                                await AutoSaveAndSyncConfiguration(networkService);
                                return true;
                            }
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _operationLogService.LogException("Background configuration sync failed", ex, LogLevel.Error);
                            return false;
                        }
                    },
                    TimeSpan.FromMinutes(AppSettings.Instance.ConfigurationSyncIntervalMinutes),
                    AppSettings.Instance.MaxTaskRetries
                );

                _backgroundTaskManager.Start();
                _operationLogService.Log("Background tasks started");
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Failed to start background tasks", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(StartBackgroundTasks));
        }

        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                _operationLogService?.LogException("Unhandled dispatcher exception", e.Exception, LogLevel.Error);
                _operationLogService?.Flush();
                
                var result = System.Windows.MessageBox.Show(
                    $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDo you want to continue?\n\nThe error has been logged to: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\WS4W\\Logs",
                    "Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                
                if (result == MessageBoxResult.Yes)
                {
                    e.Handled = true;
                }
            }
            catch (Exception logEx)
            {
                WriteEmergencyLog("Application_DispatcherUnhandledException", $"Original: {e.Exception}\nLogging error: {logEx}");
                System.Windows.MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _operationLogService?.LogMethodEntry(nameof(OnExit));
            
            try
            {
                _backgroundTaskManager?.Stop();
                _backgroundTaskManager?.Dispose();
                
                _notifyIcon?.Dispose();
                _mutex?.Dispose();
                
                _operationLogService?.Log("Application exited normally");
                _operationLogService?.Flush();
            }
            catch (Exception ex)
            {
                _operationLogService?.LogException("Error during application exit", ex);
                _operationLogService?.Flush();
            }
            
            base.OnExit(e);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                
                try
                {
                    _backgroundTaskManager?.Dispose();
                    _notifyIcon?.Dispose();
                    _mutex?.Dispose();
                    _operationLogService?.Flush();
                    (_operationLogService as IDisposable)?.Dispose();
                }
                catch
                {
                }
            }
        }

        #region Command Line Handlers

        public static void Status(StatusCommand o)
        {
            try
            {
                // Check if the status window is already showing.
                foreach (Process existingProcess in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Where(p => p.Id != Process.GetCurrentProcess().Id))
                {
                    // Get the process's command-line args to see if it was run with the "status" flag.
                    ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(@"root/cimv2", $"select CommandLine from Win32_Process where ProcessId = '{existingProcess.Id}'");
                    foreach (var ws4wInstance in managementObjectSearcher.Get().OfType<ManagementObject>())
                    {
                        if (ws4wInstance.GetPropertyValue("CommandLine")?.ToString() is { } commandLine)
                        {
                            int substringIndex = commandLine.LastIndexOf('"') + 2;  
                            string arguments = substringIndex <= commandLine.Length ? commandLine.Substring(commandLine.LastIndexOf('"') + 2) : string.Empty;       
                            if (arguments == typeof(StatusCommand).GetVerb())       
                            {
                                SetForegroundWindow(existingProcess.MainWindowHandle);
                                Environment.Exit(0);
                            }
                        }
                    }
                }

                // Otherwise, show the status window.
                var networkService = App.Current.Services.GetService<INetworkService>();
                new ServerStatusPrerequisite(networkService).Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Status command: {ex}");
            }
        }

        public static void RestartInternetSharing(RestartInternetSharingCommand o)
        {
            try
            {
                var networkService = App.Current.Services.GetService<INetworkService>();
                var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
                var persistentInternetSharingPrerequisite = new PersistentInternetSharingPrerequisite(networkService);
                var newNetNatPrereq = new NewNetNatPrerequisite(networkService);

                string networkToShare = o.NetworkToShare;

                // If Internet Sharing is already enabled, disable it first.
                if (internetSharingPrerequisite.Fulfilled)
                {
                    Console.WriteLine(WgServerforWindows.Properties.Resources.DisablingInternetSharing);
                    internetSharingPrerequisite.Configure();
                }

                // Now enable it.
                Console.WriteLine(WgServerforWindows.Properties.Resources.EnablingInternetSharing, networkToShare);
                internetSharingPrerequisite.Resolve(networkToShare);

                int result = internetSharingPrerequisite.Fulfilled ? 0 : 1;

                Environment.Exit(result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RestartInternetSharing command: {ex}");
                Environment.Exit(1);
            }
        }

        public static void SetPath(SetPathCommand o)
        {
            try
            {
                string pathEnvVar = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);

                if (string.IsNullOrEmpty(pathEnvVar))
                {
                    Console.WriteLine(Cli.Options.Properties.Resources.CantLoadPath);
                    Environment.Exit(1);
                }

                string pwd = AppContext.BaseDirectory;

                if (string.IsNullOrEmpty(pwd))
                {
                    Console.WriteLine(Cli.Options.Properties.Resources.CantLoadPwd);
                    Environment.Exit(1);
                }

                if (pathEnvVar.Contains(pwd) == false)
                {
                    pathEnvVar = $"{pathEnvVar};{pwd}";
                    Environment.SetEnvironmentVariable("PATH", pathEnvVar, EnvironmentVariableTarget.Machine);
                    Console.WriteLine(Cli.Options.Properties.Resources.AddedPwdToPath, pwd);
                }
                else
                {
                    Console.WriteLine(Cli.Options.Properties.Resources.FoundPwdInPath, pwd);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SetPath command: {ex}");
                Environment.Exit(1);
            }
        }

        public static void SetNetIpAddress(SetNetIpAddressCommand o)
        {
            try
            {
                Thread.Sleep(TimeSpan.FromSeconds(10));
                var networkService = App.Current.Services.GetService<INetworkService>();
                new NewNetNatPrerequisite(networkService).Resolve(o.ServerDataPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SetNetIpAddress command: {ex}");
            }
        }

        public static void PrivateNetwork(PrivateNetworkCommand o)
        {
            try
            {
                Thread.Sleep(TimeSpan.FromSeconds(10));
                var networkService = App.Current.Services.GetService<INetworkService>();
                new PrivateNetworkPrerequisite(networkService).Resolve();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PrivateNetwork command: {ex}");
            }
        }

        #endregion

        #region P/Invoke

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        #endregion
    }
}
