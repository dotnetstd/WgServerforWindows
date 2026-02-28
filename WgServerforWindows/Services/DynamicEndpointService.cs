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
    public class DynamicEndpointService : IDynamicEndpointService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IToastService _toastService;
        private readonly INetworkService _networkService;
        private readonly IOperationLogService _operationLogService;
        private System.Timers.Timer _timer;
        private string _lastIp;
        private bool _disposed = false;

        public event Action<string> OnIpChanged;

        public DynamicEndpointService(IToastService toastService, INetworkService networkService, IOperationLogService operationLogService)
        {
            _toastService = toastService ?? throw new ArgumentNullException(nameof(toastService));
            _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
            _operationLogService = operationLogService ?? throw new ArgumentNullException(nameof(operationLogService));
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public void StartMonitoring(int intervalSeconds = 300)
        {
            _operationLogService.LogMethodEntry(nameof(StartMonitoring), intervalSeconds);
            
            try
            {
                StopMonitoring();
                
                if (intervalSeconds < 10)
                {
                    _operationLogService.Log($"Monitoring interval too short: {intervalSeconds}s, using minimum of 10s", LogLevel.Warning);
                    intervalSeconds = 10;
                }
                
                _timer = new System.Timers.Timer(intervalSeconds * 1000);
                _timer.Elapsed += async (s, e) => await CheckForChangesAsync();
                _timer.AutoReset = true;
                _timer.Start();

                _operationLogService.Log($"IP monitoring started with interval: {intervalSeconds}s");
                
                // Initial check
                Task.Run(CheckForChangesAsync);
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Failed to start IP monitoring", ex);
                throw;
            }
            
            _operationLogService.LogMethodExit(nameof(StartMonitoring));
        }

        public void StopMonitoring()
        {
            _operationLogService.LogMethodEntry(nameof(StopMonitoring));
            
            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Elapsed -= async (s, e) => await CheckForChangesAsync();
                    _timer.Dispose();
                    _timer = null;
                    _operationLogService.Log("IP monitoring stopped");
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error stopping IP monitoring", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(StopMonitoring));
        }

        private async Task CheckForChangesAsync()
        {
            _operationLogService.LogMethodEntry(nameof(CheckForChangesAsync));
            
            try
            {
                var currentIp = await GetPublicIpv6Async();
                
                if (!string.IsNullOrEmpty(currentIp) && currentIp != _lastIp)
                {
                    var oldIp = _lastIp;
                    _lastIp = currentIp;
                    
                    _operationLogService.Log($"IP address changed from '{oldIp}' to '{currentIp}'");
                    
                    if (!string.IsNullOrEmpty(oldIp))
                    {
                        // Only update and notify if it's not the first detection
                        await UpdateEndpointAsync(currentIp);
                    }
                    
                    OnIpChanged?.Invoke(currentIp);
                }
                else if (string.IsNullOrEmpty(currentIp))
                {
                    _operationLogService.Log("No public IP address detected", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error checking for IP changes", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(CheckForChangesAsync));
        }

        public async Task<string> GetPublicIpv6Async()
        {
            _operationLogService.LogMethodEntry(nameof(GetPublicIpv6Async));
            
            string result = null;
            
            try
            {
                // Method 1: Try external API for IPv6 first
                _operationLogService.Log("Attempting to get IPv6 address from external API", LogLevel.Debug);
                
                using var ctsV6 = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var ipv6Response = await _httpClient.GetStringAsync("https://api64.ipify.org", ctsV6.Token);
                
                if (IPAddress.TryParse(ipv6Response.Trim(), out var ipv6Address) && ipv6Address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    result = ipv6Address.ToString();
                    _operationLogService.Log($"IPv6 address obtained from external API: {result}");
                    return result;
                }

                // Method 2: Query local interfaces for Global Unicast Address (IPv6)
                _operationLogService.Log("Attempting to get IPv6 from local interfaces", LogLevel.Debug);
                
                var ipv6 = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Where(a => !IPAddress.IsLoopback(a.Address) && !a.Address.IsIPv6LinkLocal && !a.Address.IsIPv6SiteLocal)
                    .Select(a => a.Address.ToString())
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(ipv6))
                {
                    result = ipv6;
                    _operationLogService.Log($"IPv6 address obtained from local interface: {result}");
                    return result;
                }

                // Method 3: Try to get IPv4 address as fallback
                _operationLogService.Log("Attempting to get IPv4 address from external API", LogLevel.Debug);
                
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var ipv4Response = await _httpClient.GetStringAsync("https://api.ipify.org", cts.Token);
                
                if (IPAddress.TryParse(ipv4Response.Trim(), out var ipv4Address) && ipv4Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result = ipv4Address.ToString();
                    _operationLogService.Log($"IPv4 address obtained from external API: {result}");
                    return result;
                }
            }
            catch (HttpRequestException ex)
            {
                _operationLogService.LogException("HTTP request failed while getting public IP", ex, LogLevel.Warning);
            }
            catch (TaskCanceledException)
            {
                _operationLogService.Log("HTTP request timed out while getting public IP", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Unexpected error while getting public IP", ex, LogLevel.Warning);
            }

            _operationLogService.LogMethodExit(nameof(GetPublicIpv6Async), result ?? "null");
            return result;
        }

        public async Task<bool> UpdateEndpointAsync(string newIp)
        {
            _operationLogService.LogMethodEntry(nameof(UpdateEndpointAsync), newIp);
            
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(newIp))
                {
                    _operationLogService.Log("Cannot update endpoint: IP address is null or empty", LogLevel.Error);
                    return false;
                }
                
                // Ensure configuration file exists
                ServerConfigurationPrerequisite.EnsureConfigFile();
                
                string configPath = ServerConfigurationPrerequisite.ServerDataPath;
                if (!File.Exists(configPath))
                {
                    string errorMsg = "Server configuration file does not exist";
                    _operationLogService.Log(errorMsg, LogLevel.Error);
                    _toastService.Show("服务器配置文件不存在，请先创建配置。", ToastType.Error, "IP Sync Error");
                    return false;
                }

                // Check if tunnel service is installed
                if (!_networkService.IsTunnelServiceInstalled(GlobalAppSettings.Instance.TunnelServiceName))
                {
                    string errorMsg = "Tunnel service is not installed";
                    _operationLogService.Log(errorMsg, LogLevel.Error);
                    _toastService.Show("隧道服务未安装，请先安装服务。", ToastType.Error, "IP Sync Error");
                    return false;
                }

                _operationLogService.Log($"Loading configuration from: {configPath}");
                
                // Load configuration
                var config = Configuration.LoadFromFile(configPath);
                var serverConfiguration = new ServerConfiguration().Load<ServerConfiguration>(config);

                // Update endpoint host
                string oldEndpoint = serverConfiguration.EndpointProperty.Host;
                serverConfiguration.EndpointProperty.Host = newIp;
                
                _operationLogService.Log($"Updating endpoint from '{oldEndpoint}' to '{newIp}'");

                // Save configuration
                serverConfiguration.Save(config);
                config.SaveToFile(configPath);
                
                _operationLogService.Log($"Configuration saved to: {configPath}");

                // Sync with tunnel service if running
                try
                {
                    _operationLogService.Log($"Syncing configuration to tunnel service: {GlobalAppSettings.Instance.TunnelServiceName}");
                    _networkService.SyncConfiguration(GlobalAppSettings.Instance.TunnelServiceName, configPath);
                    _operationLogService.Log("Configuration synced successfully");
                }
                catch (Exception syncEx)
                {
                    _operationLogService.LogException("Failed to sync configuration to tunnel service", syncEx, LogLevel.Error);
                    
                    string errorMessage = $"同步配置失败: {syncEx.Message}";
                    
                    // Check if service is stopped
                    try
                    {
                        string status = _networkService.GetServerStatus(GlobalAppSettings.Instance.TunnelServiceName);
                        if (status.Contains("Stopped"))
                        {
                            errorMessage += "\n\n隧道服务当前处于停止状态，请先启动服务。";
                            _operationLogService.Log("Tunnel service is stopped", LogLevel.Warning);
                        }
                    }
                    catch (Exception statusEx)
                    {
                        _operationLogService.LogException("Failed to get tunnel service status", statusEx, LogLevel.Warning);
                    }
                    
                    _toastService.Show(errorMessage, ToastType.Error, "IP Sync Error");
                    return false;
                }

                // Notify user
                _toastService.Show($"Public IP changed to {newIp}. Server configuration updated.", ToastType.Info, "IP Sync");
                _operationLogService.Log($"Endpoint updated successfully to: {newIp}");
                
                return true;
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Failed to update server endpoint", ex);
                _toastService.Show($"Failed to update server endpoint: {ex.Message}", ToastType.Error, "IP Sync Error");
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                StopMonitoring();
                _httpClient?.Dispose();
            }
        }
    }
}
