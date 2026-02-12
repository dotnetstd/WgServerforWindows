using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using SharpConfig;
using WgServerforWindows.Models;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class DynamicEndpointService : IDynamicEndpointService
    {
        private readonly HttpClient _httpClient;
        private readonly IToastService _toastService;
        private readonly INetworkService _networkService;
        private System.Timers.Timer _timer;
        private string _lastIp;

        public event Action<string> OnIpChanged;

        public DynamicEndpointService(IToastService toastService, INetworkService networkService)
        {
            _httpClient = new HttpClient();
            _toastService = toastService;
            _networkService = networkService;
        }

        public void StartMonitoring(int intervalSeconds = 300)
        {
            StopMonitoring();
            _timer = new System.Timers.Timer(intervalSeconds * 1000);
            _timer.Elapsed += async (s, e) => await CheckForChangesAsync();
            _timer.AutoReset = true;
            _timer.Start();

            // Initial check
            Task.Run(CheckForChangesAsync);
        }

        public void StopMonitoring()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }

        private async Task CheckForChangesAsync()
        {
            var currentIp = await GetPublicIpv6Async();
            if (!string.IsNullOrEmpty(currentIp) && currentIp != _lastIp)
            {
                var oldIp = _lastIp;
                _lastIp = currentIp;
                
                if (!string.IsNullOrEmpty(oldIp))
                {
                    // Only update and notify if it's not the first detection
                    await UpdateEndpointAsync(currentIp);
                }
                
                OnIpChanged?.Invoke(currentIp);
            }
        }

        public async Task<string> GetPublicIpv6Async()
        {
            try
            {
                // Method 1: Query local interfaces for Global Unicast Address
                var ipv6 = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Where(a => !IPAddress.IsLoopback(a.Address) && !a.Address.IsIPv6LinkLocal && !a.Address.IsIPv6SiteLocal)
                    .Select(a => a.Address.ToString())
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(ipv6)) return ipv6;

                // Method 2: Fallback to external API
                // Use a short timeout for the external query
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _httpClient.GetStringAsync("https://api64.ipify.org", cts.Token);
                if (IPAddress.TryParse(response.Trim(), out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return address.ToString();
                }
            }
            catch
            {
                // Log error or ignore
            }

            return null;
        }

        public async Task<bool> UpdateEndpointAsync(string newIp)
        {
            try
            {
                string configPath = ServerConfigurationPrerequisite.ServerDataPath;
                if (!File.Exists(configPath)) return false;

                // Load configuration
                var config = Configuration.LoadFromFile(configPath);
                var serverConfiguration = new ServerConfiguration().Load<ServerConfiguration>(config);

                // Update endpoint host
                serverConfiguration.EndpointProperty.Host = newIp;

                // Save configuration
                serverConfiguration.Save(config);
                config.SaveToFile(configPath);

                // Sync with tunnel service if running
                _networkService.SyncConfiguration(GlobalAppSettings.Instance.TunnelServiceName, configPath);

                // Notify user
                _toastService.Show($"Public IP changed to {newIp}. Server configuration updated.", ToastType.Info, "IP Sync");

                return true;
            }
            catch (Exception ex)
            {
                _toastService.Show($"Failed to update server endpoint: {ex.Message}", ToastType.Error, "IP Sync Error");
                return false;
            }
        }
    }
}
