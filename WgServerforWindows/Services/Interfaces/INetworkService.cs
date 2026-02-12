using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WgServerforWindows.Services.Interfaces
{
    public interface INetworkService
    {
        int GetInterfaceIndex(string interfaceName);
        bool IsNatSupported();
        bool CheckNatRule(string natName, string expectedRange);
        bool CheckInterfaceIp(string interfaceName, string expectedIp);
        void SetInterfaceIp(string interfaceName, string ipAddress, int prefixLength);
        void RemoveInterfaceIp(string interfaceName);
        void CreateNatRule(string natName, string internalIpInterfaceAddressPrefix);
        void RemoveNatRule(string natName);
        bool EnableHyperV();

        // ICS related
        bool IsIcsEnabled(string interfaceName);
        void EnableIcs(string networkToShare, string networkToReceive);
        void DisableIcs(string networkToReceive);
        List<string> GetSharedNetworks();

        // Tunnel Service related
        bool IsTunnelServiceInstalled(string serviceName);
        void InstallTunnelService(string confPath);
        void UninstallTunnelService(string serviceName);
        bool IsPortInUse(int port, bool udp = true);

        // Network Category
        int GetNetworkCategory(string interfaceName, TimeSpan? timeout = null);
        void SetNetworkCategory(string interfaceName, int category);

        // Persistent ICS
        bool IsPersistentIcsEnabled();
        void SetPersistentIcs(bool enable);

        // Server Status
        string GetServerStatus(string serviceName);
        void SyncConfiguration(string serviceName, string confPath);

        // WireGuard Exe
        bool IsWireGuardInstalled();
        void UninstallWireGuard();

        // Registry/System Settings
        string GetScopeAddress();
        void SetScopeAddress(string scopeAddress);

        // Scheduled Tasks
        bool IsTaskEnabled(string taskName, string expectedPath, string expectedArgsPrefix);
        void CreateBootTask(string taskName, string path, string args, TimeSpan delay);
        void DisableTask(string taskName);

        // MTU Testing
        Task<int> EstimateMtuAsync(string host, Action<int, string> progressCallback);
    }
}
