using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
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
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IServiceProvider Services { get; private set; }

        public new static App Current => (App)System.Windows.Application.Current;

        private static System.Threading.Mutex? _mutex;

        private NotifyIcon _notifyIcon;

        public App()
        {
            // 先初始化Services，确保即使AppSettings加载失败也能正常启动
            Services = ConfigureServices();
            
            try
            {
                AppSettings.Instance.Load();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载AppSettings失败: {ex.Message}");
                // 即使加载失败也继续启动
            }
            
            DispatcherUnhandledException += Application_DispatcherUnhandledException;
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

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {

            bool createdNew;
            _mutex = new System.Threading.Mutex(true, "WgServerforWindows", out createdNew);

            if (!createdNew)
            {
                // Instance already running
                System.Windows.MessageBox.Show("WgServerforWindows is already running.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }


            // Load language setting
            var language = GlobalAppSettings.Instance.Language;
            if (!string.IsNullOrEmpty(language))
            {
                try
                {
                    var culture = new System.Globalization.CultureInfo(language);
                    Thread.CurrentThread.CurrentCulture = culture;
                    Thread.CurrentThread.CurrentUICulture = culture;
                }
                catch (Exception)
                {
                    // Fallback to default if culture is invalid
                }
            }

            // 获取操作日志服务
            var operationLogService = Services.GetService<IOperationLogService>();
            operationLogService.Log("Application started");
            
            // Auto enable NAT on startup (if enabled and prerequisites are satisfied)
            if (AppSettings.Instance.IsAutoEnableNatOnStartup)
            {
                var networkService = Services.GetService<INetworkService>();
                var serverConfigurationPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                var tunnelServicePrerequisite = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
                var persistentInternetSharingPrerequisite = new PersistentInternetSharingPrerequisite(networkService);
                var newNetNatPrerequisite = new NewNetNatPrerequisite(networkService);

                operationLogService.Log("Checking if NAT should be auto-enabled on startup");

                if (newNetNatPrerequisite.IsSupported
                    && serverConfigurationPrerequisite.Fulfilled
                    && tunnelServicePrerequisite.Fulfilled
                    && !internetSharingPrerequisite.Fulfilled
                    && !persistentInternetSharingPrerequisite.Fulfilled
                    && !newNetNatPrerequisite.Fulfilled)
                {
                    operationLogService.Log("Auto-enabling NAT on startup");
                    newNetNatPrerequisite.Resolve();
                    operationLogService.Log("NAT auto-enabled successfully");
                }
                else
                {
                    operationLogService.Log("NAT auto-enable skipped: prerequisites not met");
                }
            }

            // Auto check and sync public IP on startup
            if (AppSettings.Instance.IsPublicIpCheckOnStartup)
            {
                var networkService = Services.GetService<INetworkService>();
                var dynamicEndpointService = Services.GetService<IDynamicEndpointService>();
                var serverConfigurationPrerequisite = new ServerConfigurationPrerequisite(networkService, new OpenServerConfigDirectorySubCommand(), new ChangeServerConfigDirectorySubCommand());
                var tunnelServicePrerequisite = new TunnelServicePrerequisite(networkService, new TunnelServiceNameSubCommand());
                var newNetNatPrerequisite = new NewNetNatPrerequisite(networkService);
                var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
                var persistentInternetSharingPrerequisite = new PersistentInternetSharingPrerequisite(networkService);
                
                operationLogService.Log("Checking if public IP should be auto-checked on startup");
                
                // 检查所有关键配置是否均处于正常状态
                bool isServerConfigValid = serverConfigurationPrerequisite.Fulfilled;
                bool isTunnelServiceValid = tunnelServicePrerequisite.Fulfilled;
                bool isNatValid = newNetNatPrerequisite.IsSupported ? newNetNatPrerequisite.Fulfilled : (internetSharingPrerequisite.Fulfilled || persistentInternetSharingPrerequisite.Fulfilled);
                
                operationLogService.Log($"Configuration check results: ServerConfigValid={isServerConfigValid}, TunnelServiceValid={isTunnelServiceValid}, NatValid={isNatValid}");
                
                if (isServerConfigValid && isTunnelServiceValid && isNatValid)
                {
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var delay = GlobalAppSettings.Instance.BootTaskDelay;
                        if (delay > TimeSpan.Zero)
                        {
                            operationLogService.Log($"Waiting for {delay.TotalSeconds} seconds before checking public IP");
                            await System.Threading.Tasks.Task.Delay(delay);
                        }
                        operationLogService.Log("Checking public IP address");
                        var currentIp = await dynamicEndpointService.GetPublicIpv6Async();
                        if (!string.IsNullOrEmpty(currentIp))
                        {
                            operationLogService.Log($"Public IP address found: {currentIp}");
                            operationLogService.Log("Updating server endpoint with new public IP");
                            await dynamicEndpointService.UpdateEndpointAsync(currentIp);
                            operationLogService.Log("Server endpoint updated successfully");
                        }
                        else
                        {
                            operationLogService.Log("No public IP address found");
                        }
                        
                        // Auto save and sync configuration (simulate user clicking save button)
                        operationLogService.Log("Auto saving and syncing server configuration");
                        try
                        {
                            // Load current server configuration
                            var serverConfig = new ServerConfiguration().Load<ServerConfiguration>(Configuration.LoadFromFile(ServerConfigurationPrerequisite.ServerDataPath));
                            
                            // Save to data file
                            serverConfig.ToConfiguration().SaveToFile(ServerConfigurationPrerequisite.ServerDataPath);
                            operationLogService.Log("Server configuration saved to data file");
                            
                            // Save to WG config file
                            var wgConfig = serverConfig.ToConfiguration<ServerConfiguration>();
                            
                            // Merge client configurations if they exist
                            if (Directory.Exists(ClientConfigurationsPrerequisite.ClientDataDirectory))
                            {
                                foreach (string clientFile in Directory.GetFiles(ClientConfigurationsPrerequisite.ClientDataDirectory, "*.conf"))
                                {
                                    var clientConfig = new ClientConfiguration(null).Load<ClientConfiguration>(Configuration.LoadFromFile(clientFile));
                                    if (clientConfig.IsEnabledProperty.Value == true.ToString())
                                    {
                                        wgConfig = wgConfig.Merge(clientConfig.ToConfiguration<ServerConfiguration>());
                                    }
                                }
                                operationLogService.Log("Client configurations merged");
                            }
                            
                            wgConfig.SaveToFile(ServerConfigurationPrerequisite.ServerWGPath);
                            operationLogService.Log("Server configuration saved to WG file");
                            
                            // Sync configuration to tunnel service
                            using (TemporaryFile tempFile = new TemporaryFile(ServerConfigurationPrerequisite.ServerWGPath, ServerConfigurationPrerequisite.ServerWGPathWithCustomTunnelName))
                            {
                                networkService.SyncConfiguration(GlobalAppSettings.Instance.TunnelServiceName, tempFile.NewFilePath);
                                operationLogService.Log("Server configuration synced to tunnel service");
                            }
                            
                            operationLogService.Log("Auto save and sync completed successfully");
                        }
                        catch (Exception ex)
                        {
                            operationLogService.Log($"Error during auto save and sync: {ex.Message}");
                        }
                    });
                }
                else
                {
                    operationLogService.Log("Public IP auto-check skipped: prerequisites not met");
                }
            }

            base.OnStartup(e);

            bool startMinimized = e.Args.Contains("--minimized");
            var args = e.Args.Where(a => a != "--minimized").ToArray();

            // Initialize NotifyIcon
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

            if (args.Any())
            {
                // First, handle UI-related args
                var uiArgsParsed = Parser.Default.ParseArguments<StatusCommand, object>(args)
                    .WithParsed<StatusCommand>(Status);

                if (uiArgsParsed.Tag != ParserResultType.Parsed)
                {
                    // Otherwise, try non-UI args.

                    // We don't want to handle Dispatcher exceptions in this scenario, since we are UI-less
                    DispatcherUnhandledException -= Application_DispatcherUnhandledException;

                    Parser.Default.ParseArguments<RestartInternetSharingCommand, SetPathCommand, SetNetIpAddressCommand, PrivateNetworkCommand>(args)
                        .WithParsed<RestartInternetSharingCommand>(RestartInternetSharing)
                        .WithParsed<SetPathCommand>(SetPath)
                        .WithParsed<SetNetIpAddressCommand>(SetNetIpAddress)
                        .WithParsed<PrivateNetworkCommand>(PrivateNetwork);

                    // Don't proceed to GUI if started with command-line args
                    Environment.Exit(0);
                }
            }
            else
            {
                // Otherwise, this is a normal Windowed startup.

                // Finally, do a normal startup.
                var mainShell = App.Current.Services.GetService<MainShell>();
                if (startMinimized)
                {
                    mainShell.WindowState = WindowState.Minimized;
                    mainShell.Hide(); // Hide from taskbar if starting minimized
                }
                else
                {
                    mainShell.Show();
                }
            }
        }

        private void ShowMainWindow()
        {
            var mainShell = App.Current.Services.GetService<MainShell>();
            if (mainShell.IsVisible)
            {
                if (mainShell.WindowState == WindowState.Minimized)
                {
                    mainShell.WindowState = WindowState.Normal;
                }
                mainShell.Activate();
            }
            else
            {
                mainShell.Show();
                mainShell.WindowState = WindowState.Normal;
                mainShell.Activate();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Dispose();
            }
            base.OnExit(e);
        }

        private static void RestartInternetSharing(RestartInternetSharingCommand o)
        {
            var networkService = App.Current.Services.GetService<INetworkService>();
            var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
            string networkToShare = o.NetworkToShare;

            if (string.IsNullOrEmpty(networkToShare))
            {
                // No network specified for re-sharing, retrieve the one already shared.
                List<string> sharedNetworks = internetSharingPrerequisite.GetSharedNetworks();
                networkToShare = sharedNetworks.FirstOrDefault();

                if (string.IsNullOrEmpty(networkToShare))
                {
                    Console.WriteLine(WgServerforWindows.Properties.Resources.CannotRestartInternetSharingNoNetwork);
                    Environment.Exit(1);
                }
                else if (sharedNetworks.Skip(1).Any())
                {
                    Console.WriteLine(WgServerforWindows.Properties.Resources.CannotRestartInternetSharingMultipleNetworks);
                    Environment.Exit(1);
                }
            }

            if (internetSharingPrerequisite.Fulfilled)
            {
                // Internet sharing is already enabled. Disable it, first.
                Console.WriteLine(WgServerforWindows.Properties.Resources.DisablingInternetSharing);
                internetSharingPrerequisite.Configure();
            }

            // Now enable it.
            Console.WriteLine(WgServerforWindows.Properties.Resources.EnablingInternetSharing, networkToShare);
            internetSharingPrerequisite.Resolve(networkToShare);

            int result = internetSharingPrerequisite.Fulfilled ? 0 : 1;

            Environment.Exit(result);
        }

        private static void SetPath(SetPathCommand o)
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

        public static void SetNetIpAddress(SetNetIpAddressCommand o)
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var networkService = App.Current.Services.GetService<INetworkService>();
            new NewNetNatPrerequisite(networkService).Resolve(o.ServerDataPath);
        }

        public static void PrivateNetwork(PrivateNetworkCommand o)
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var networkService = App.Current.Services.GetService<INetworkService>();
            new PrivateNetworkPrerequisite(networkService).Resolve();
        }

        public static void Status(StatusCommand o)
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

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // In case something was in progress when the error occurred
            WaitCursor.SetOverrideCursor(null);

            Exception realException = e.Exception;
            while (realException.InnerException is { } innerException)
            {
                realException = innerException;
            }

            new UnhandledErrorWindow {DataContext = new UnhandledErrorWindowModel
            {
                Title = WgServerforWindows.Properties.Resources.Error,
                Text = string.Format(WgServerforWindows.Properties.Resources.UnexpectedErrorMessage, realException.Message),
                Exception = e.Exception
            }}.ShowDialog();


            // Don't kill the app
            e.Handled = true;
        }

        #region P/Invoke

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        #endregion
    }
}
