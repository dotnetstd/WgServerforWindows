using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WgAPI;
using WgAPI.Commands;
using WgServerforWindows.Services.Interfaces;
using WgServerforWindows.Views;

namespace WgServerforWindows.Models
{
    public partial class DashboardViewModel : ObservableObject
    {
        private readonly IDynamicEndpointService _dynamicEndpointService;
        private readonly IToastService _toastService;
        private readonly IServiceProvider _serviceProvider;
        private System.Timers.Timer _trafficTimer;
        private long _lastRx;
        private long _lastTx;
        private DateTime _lastCheck = DateTime.MinValue;

        [ObservableProperty]
        private string _publicIp = "Detecting...";

        [ObservableProperty]
        private string _serverStatus = "Running"; // Placeholder

        [ObservableProperty]
        private string _downloadSpeed = "0 B/s";

        [ObservableProperty]
        private string _uploadSpeed = "0 B/s";

        public DashboardViewModel(IDynamicEndpointService dynamicEndpointService, IToastService toastService, IServiceProvider serviceProvider)
        {
            _dynamicEndpointService = dynamicEndpointService;
            _toastService = toastService;
            _serviceProvider = serviceProvider;

            _dynamicEndpointService.OnIpChanged += DynamicEndpointService_OnIpChanged;
            
            AppSettings.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppSettings.IsDynamicIpSyncEnabled))
                {
                    if (AppSettings.Instance.IsDynamicIpSyncEnabled)
                    {
                        _dynamicEndpointService.StartMonitoring();
                    }
                    else
                    {
                        _dynamicEndpointService.StopMonitoring();
                    }
                }
            };

            if (AppSettings.Instance.IsDynamicIpSyncEnabled)
            {
                _dynamicEndpointService.StartMonitoring();
            }

            StartTrafficMonitoring();
        }

        private void StartTrafficMonitoring()
        {
            _trafficTimer = new System.Timers.Timer(2000); // 2 seconds
            _trafficTimer.Elapsed += async (s, e) => await UpdateTrafficAsync();
            _trafficTimer.AutoReset = true;
            _trafficTimer.Start();
        }

        private async Task UpdateTrafficAsync()
        {
            try
            {
                var tunnelName = GlobalAppSettings.Instance.TunnelServiceName;
                var wg = new WireGuardExe();
                var output = wg.ExecuteCommand(new ShowCommand(tunnelName, "transfer"), out int exitCode);

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    long totalRx = 0;
                    long totalTx = 0;

                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            if (long.TryParse(parts[1], out long rx)) totalRx += rx;
                            if (long.TryParse(parts[2], out long tx)) totalTx += tx;
                        }
                    }

                    var now = DateTime.Now;
                    if (_lastCheck != DateTime.MinValue)
                    {
                        var duration = (now - _lastCheck).TotalSeconds;
                        if (duration > 0)
                        {
                            var rxSpeed = (totalRx - _lastRx) / duration;
                            var txSpeed = (totalTx - _lastTx) / duration;

                            // Ensure UI thread
                            App.Current.Dispatcher.Invoke(() =>
                            {
                                DownloadSpeed = FormatSpeed(rxSpeed);
                                UploadSpeed = FormatSpeed(txSpeed);
                            });
                        }
                    }

                    _lastRx = totalRx;
                    _lastTx = totalTx;
                    _lastCheck = now;
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        private string FormatSpeed(double bytesPerSec)
        {
            string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
            int order = 0;
            while (bytesPerSec >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytesPerSec = bytesPerSec / 1024;
            }
            return $"{bytesPerSec:0.##} {sizes[order]}";
        }

        private void DynamicEndpointService_OnIpChanged(string newIp)
        {
            PublicIp = newIp;
            _toastService.Show($"Public IP changed to: {newIp}", ToastType.Info, "Network Update");
        }

        [RelayCommand]
        private async Task RefreshIp()
        {
            PublicIp = "Refreshing...";
            var ip = await _dynamicEndpointService.GetPublicIpv6Async();
            PublicIp = ip ?? "Unknown";
        }

        [RelayCommand]
        private void CopyIp()
        {
            if (!string.IsNullOrEmpty(PublicIp) && PublicIp != "Detecting..." && PublicIp != "Unknown")
            {
                Clipboard.SetText(PublicIp);
                _toastService.Show("IP copied to clipboard", ToastType.Success);
            }
        }

        [RelayCommand]
        private void OpenMtuWizard()
        {
            var wizard = _serviceProvider.GetRequiredService<MtuWizardWindow>();
            wizard.Owner = Application.Current.MainWindow;
            wizard.ShowDialog();
        }
    }
}
