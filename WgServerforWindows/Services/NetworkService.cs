using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Microsoft.WindowsAPICodePack.Net;
using WgAPI;
using WgAPI.Commands;
using WgServerforWindows.Cli.Options;
using WgServerforWindows.Models;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class NetworkService : INetworkService
    {
        private readonly WireGuardExe _wireGuardExe;

        public NetworkService()
        {
            _wireGuardExe = new WireGuardExe();
        }

        public int GetInterfaceIndex(string interfaceName)
        {
            var iface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(i => i.Name == interfaceName);
            
            return iface?.GetIPProperties()?.GetIPv4Properties()?.Index ?? -1;
        }

        public bool IsNatSupported()
        {
            _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    "-NoProfile if ((Get-Command New-NetNat).Parameters.ContainsKey('InternalIPInterfaceAddressPrefix')) { exit 0 } else { exit 1 }"),
                out int exitCode);

            if (exitCode == 0) return true;

            _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    "-NoProfile Get-Help New-NetNat -Parameter InternalIPInterfaceAddressPrefix"),
                out exitCode);

            return exitCode == 0;
        }

        public bool CheckNatRule(string natName, string expectedRange)
        {
            string output = _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    $"-NoProfile Get-NetNat -Name {natName}"),
                out int exitCode);

            return exitCode == 0 && output.Contains(expectedRange);
        }

        public bool CheckInterfaceIp(string interfaceName, string expectedIp)
        {
            int index = GetInterfaceIndex(interfaceName);
            if (index == -1) return false;

            string output = _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    $"-NoProfile Get-NetIPAddress -InterfaceIndex {index}"),
                out int exitCode);

            return exitCode == 0 && output.Contains(expectedIp);
        }

        public void SetInterfaceIp(string interfaceName, string ipAddress, int prefixLength)
        {
            int index = GetInterfaceIndex(interfaceName);
            if (index == -1) throw new Exception($"Interface {interfaceName} not found.");

            string output = _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    $"-NoProfile New-NetIPAddress -IPAddress {ipAddress} -PrefixLength {prefixLength} -InterfaceIndex {index}"),
                out int exitCode);

            if (exitCode != 0) throw new Exception(output);
        }

        public void RemoveInterfaceIp(string interfaceName)
        {
            int index = GetInterfaceIndex(interfaceName);
            if (index == -1) return;

            _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    $"-NoProfile Remove-NetIPAddress -InterfaceIndex {index} -Confirm:$false"),
                out _);
        }

        public void CreateNatRule(string natName, string internalIpInterfaceAddressPrefix)
        {
            string output = _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    $"-NoProfile New-NetNat -Name {natName} -InternalIPInterfaceAddressPrefix {internalIpInterfaceAddressPrefix}"),
                out int exitCode);

            if (exitCode != 0) throw new Exception(output);
        }

        public void RemoveNatRule(string natName)
        {
            _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    $"-NoProfile Remove-NetNat -Name {natName} -Confirm:$false"),
                out _);
        }

        public bool EnableHyperV()
        {
            _wireGuardExe.ExecuteCommand(new WireGuardCommand(string.Empty, WhichExe.Custom,
                    "powershell.exe",
                    "-NoProfile Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -NoRestart"),
                out int exitCode);

            return exitCode == 0;
        }

        // ICS related
        public bool IsIcsEnabled(string interfaceName)
        {
            try
            {
                Type type = Type.GetTypeFromProgID("HNetCfg.HNetShare");
                if (type == null) return false;

                dynamic manager = Activator.CreateInstance(type);
                foreach (dynamic connection in manager.EnumEveryConnection)
                {
                    dynamic props = manager.NetConnectionProps[connection];
                    if (props.Name == interfaceName)
                    {
                        dynamic sharingCfg = manager.INetSharingConfigurationForINetConnection[connection];
                        return sharingCfg.SharingEnabled && sharingCfg.SharingConnectionType == 1; // 1 = ICSTYPE_PUBLIC (sharing from) or 0 = ICSTYPE_PRIVATE (sharing to)
                        // Actually, we want to know if it's being shared TO.
                    }
                }
            }
            catch
            {
                // Fallback or ignore
            }
            return false;
        }

        public void EnableIcs(string networkToShare, string networkToReceive)
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.HNetShare");
            if (type == null) throw new Exception("ICS Manager not found.");

            dynamic manager = Activator.CreateInstance(type);
            dynamic publicConn = null;
            dynamic privateConn = null;

            foreach (dynamic connection in manager.EnumEveryConnection)
            {
                dynamic props = manager.NetConnectionProps[connection];
                if (props.Name == networkToShare) publicConn = connection;
                if (props.Name == networkToReceive) privateConn = connection;
            }

            if (publicConn == null) throw new Exception($"Public connection {networkToShare} not found.");
            if (privateConn == null) throw new Exception($"Private connection {networkToReceive} not found.");

            dynamic publicSharingCfg = manager.INetSharingConfigurationForINetConnection[publicConn];
            dynamic privateSharingCfg = manager.INetSharingConfigurationForINetConnection[privateConn];

            publicSharingCfg.DisableSharing();
            privateSharingCfg.DisableSharing();

            publicSharingCfg.EnableSharing(0); // 0 = tagSHARINGCONNECTIONTYPE.ICSSHARINGTYPE_PUBLIC
            privateSharingCfg.EnableSharing(1); // 1 = tagSHARINGCONNECTIONTYPE.ICSSHARINGTYPE_PRIVATE
        }

        public void DisableIcs(string networkToReceive)
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.HNetShare");
            if (type == null) return;

            dynamic manager = Activator.CreateInstance(type);
            foreach (dynamic connection in manager.EnumEveryConnection)
            {
                dynamic props = manager.NetConnectionProps[connection];
                dynamic sharingCfg = manager.INetSharingConfigurationForINetConnection[connection];
                if (sharingCfg.SharingEnabled)
                {
                    sharingCfg.DisableSharing();
                }
            }
        }

        public List<string> GetSharedNetworks()
        {
            List<string> result = new List<string>();
            try
            {
                Type type = Type.GetTypeFromProgID("HNetCfg.HNetShare");
                if (type == null) return result;

                dynamic manager = Activator.CreateInstance(type);
                foreach (dynamic connection in manager.EnumEveryConnection)
                {
                    dynamic props = manager.NetConnectionProps[connection];
                    dynamic sharingCfg = manager.INetSharingConfigurationForINetConnection[connection];
                    if (sharingCfg.SharingEnabled && sharingCfg.SharingConnectionType == 0) // ICSSHARINGTYPE_PUBLIC
                    {
                        result.Add(props.Name);
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return result;
        }

        // Tunnel Service related
        public bool IsTunnelServiceInstalled(string serviceName)
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(nic => nic.Name == serviceName);
        }

        public void InstallTunnelService(string confPath)
        {
            _wireGuardExe.ExecuteCommand(new WgAPI.Commands.InstallTunnelServiceCommand(confPath));
        }

        public void UninstallTunnelService(string serviceName)
        {
            _wireGuardExe.ExecuteCommand(new WgAPI.Commands.UninstallTunnelServiceCommand(serviceName));
        }

        public bool IsPortInUse(int port, bool udp = true)
        {
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            if (udp)
            {
                return ipProperties.GetActiveUdpListeners().Any(e => e.Port == port);
            }
            else
            {
                return ipProperties.GetActiveTcpListeners().Any(e => e.Port == port);
            }
        }

        public int GetNetworkCategory(string interfaceName, TimeSpan? timeout = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            do
            {
                if (NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i => i.Name == interfaceName) is { } networkInterface)
                {
                    if (NetworkListManager.GetNetworks(NetworkConnectivityLevels.All).FirstOrDefault(n => n.Connections.Any(c => c.AdapterId == new Guid(networkInterface.Id))) is { } network)
                    {
                        return (int)network.Category;
                    }
                }
            } while (stopwatch.ElapsedMilliseconds < (timeout?.TotalMilliseconds ?? 0));

            return -1; // Not found
        }

        public void SetNetworkCategory(string interfaceName, int category)
        {
            if (NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i => i.Name == interfaceName) is { } networkInterface)
            {
                if (NetworkListManager.GetNetworks(NetworkConnectivityLevels.All).FirstOrDefault(n => n.Connections.Any(c => c.AdapterId == new Guid(networkInterface.Id))) is { } network)
                {
                    network.Category = (NetworkCategory)category;
                }
            }
        }

        private const string RestartInternetSharingTaskUniqueName = "WS4W Restart Internet Sharing (b17f2530-acc7-42d6-ad05-ab57b923356f)";

        public bool IsPersistentIcsEnabled()
        {
            try
            {
                // First, check whether the service is set to start automatically
                using ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\cimv2", "select * from win32_service where name = 'SharedAccess'");
                var service = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                if (service != null)
                {
                    bool isAutomatic = service.Properties["StartMode"].Value as string == "Automatic" ||
                                       service.Properties["StartMode"].Value as string == "Auto";

                    if (isAutomatic)
                    {
                        // Now check whether the special registry entry exists
                        RegistryKey sharedAccessKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\SharedAccess");
                        int? value = sharedAccessKey?.GetValue("EnableRebootPersistConnection") as int?;
                        if (value == 1)
                        {
                            // Finally, verify that the task exists and that all of the parameters are correct.
                            return IsTaskEnabled(RestartInternetSharingTaskUniqueName, Path.Combine(AppContext.BaseDirectory, "ws4w.exe"), typeof(RestartInternetSharingCommand).GetVerb());
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return false;
        }

        public void SetPersistentIcs(bool enable)
        {
            using ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\cimv2", "select * from win32_service where name = 'SharedAccess'");
            var service = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
            if (service != null)
            {
                var parameters = service.GetMethodParameters("ChangeStartMode");
                parameters["StartMode"] = enable ? "Automatic" : "Manual";
                service.InvokeMethod("ChangeStartMode", parameters, null);
            }

            RegistryKey sharedAccessKey = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\SharedAccess", writable: true)
                                          ?? Registry.LocalMachine.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\SharedAccess", writable: true);

            if (sharedAccessKey is null)
            {
                throw new Exception("There was an error setting the SharedAccess registry key.");
            }

            sharedAccessKey.SetValue("EnableRebootPersistConnection", enable ? 1 : 0);

            if (enable)
            {
                // Create/update a Scheduled Task that disables/enables internet sharing on boot.
                CreateBootTask(RestartInternetSharingTaskUniqueName, Path.Combine(AppContext.BaseDirectory, "ws4w.exe"), typeof(RestartInternetSharingCommand).GetVerb(), GlobalAppSettings.Instance.BootTaskDelay);
            }
            else
            {
                // Disable the task
                DisableTask(RestartInternetSharingTaskUniqueName);
            }
        }

        public string GetServerStatus(string serviceName)
        {
            return _wireGuardExe.ExecuteCommand(new ShowCommand(serviceName));
        }

        public void SyncConfiguration(string serviceName, string confPath)
        {
            _wireGuardExe.ExecuteCommand(new SyncConfigurationCommand(serviceName, confPath), out int exitCode);
            if (exitCode != 0)
            {
                throw new Exception($"Failed to sync configuration for service {serviceName}. Exit code: {exitCode}");
            }
        }

        // WireGuard Exe
        public bool IsWireGuardInstalled()
        {
            return _wireGuardExe.Exists;
        }

        public void UninstallWireGuard()
        {
            _wireGuardExe.ExecuteCommand(new UninstallCommand());
        }

        // Registry/System Settings
        public string GetScopeAddress()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters", writable: false);
            return key?.GetValue("ScopeAddress")?.ToString() ?? string.Empty;
        }

        public void SetScopeAddress(string scopeAddress)
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters", writable: true);
            key?.SetValue("ScopeAddress", scopeAddress);
        }

        // Scheduled Tasks
        public bool IsTaskEnabled(string taskName, string expectedPath, string expectedArgsPrefix)
        {
            var task = TaskService.Instance.FindTask(taskName);
            return task is { Enabled: true }
                   && task.Definition.Triggers.FirstOrDefault() is BootTrigger
                   && task.Definition.Actions.FirstOrDefault() is ExecAction action
                   && action.Path == expectedPath
                   && action.Arguments.StartsWith(expectedArgsPrefix);
        }

        public void CreateBootTask(string taskName, string path, string args, TimeSpan delay)
        {
            TaskDefinition td = TaskService.Instance.NewTask();
            td.Actions.Add(new ExecAction(path, args));
            td.Triggers.Add(new BootTrigger { Delay = delay });
            TaskService.Instance.RootFolder.RegisterTaskDefinition(taskName, td, TaskCreation.CreateOrUpdate, "SYSTEM", null, TaskLogonType.ServiceAccount);
        }

        public void DisableTask(string taskName)
        {
            if (TaskService.Instance.FindTask(taskName) is { } task)
            {
                task.Enabled = false;
            }
        }

        public async Task<int> EstimateMtuAsync(string host, Action<int, string> progressCallback)
        {
            int bestMtu = 1500;
            int low = 576;
            int high = 1500;

            progressCallback?.Invoke(0, $"Starting MTU estimation for {host}...");

            using (Ping pingSender = new Ping())
            {
                PingOptions options = new PingOptions(64, true); // Don't fragment

                while (low <= high)
                {
                    int mid = (low + high) / 2;
                    int payloadSize = mid - 28; // IP + ICMP headers

                    progressCallback?.Invoke((int)((1 - (double)(high - low) / (1500 - 576)) * 100), $"Testing MTU {mid}...");

                    try
                    {
                        byte[] buffer = new byte[payloadSize];
                        PingReply reply = await pingSender.SendPingAsync(host, 1000, buffer, options);

                        if (reply.Status == IPStatus.Success)
                        {
                            bestMtu = mid;
                            low = mid + 1;
                        }
                        else
                        {
                            high = mid - 1;
                        }
                    }
                    catch
                    {
                        high = mid - 1;
                    }
                }
            }

            progressCallback?.Invoke(100, $"Optimal MTU found: {bestMtu}");
            return bestMtu;
        }
    }
}
