using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using WgServerforWindows.Cli.Options;
using WgServerforWindows.Controls;
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

        private NotifyIcon _notifyIcon;

        public App()
        {
            Services = ConfigureServices();
            DispatcherUnhandledException += Application_DispatcherUnhandledException;
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // ViewModels
            services.AddSingleton<MainWindowModel>();
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
            services.AddSingleton<INetworkService, NetworkService>();

            return services.BuildServiceProvider();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
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
