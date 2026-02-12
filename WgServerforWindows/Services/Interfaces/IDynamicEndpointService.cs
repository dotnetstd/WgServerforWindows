using System;
using System.Threading.Tasks;

namespace WgServerforWindows.Services.Interfaces
{
    public interface IDynamicEndpointService
    {
        event Action<string> OnIpChanged;

        /// <summary>
        /// Detects the current public IPv6 address.
        /// </summary>
        Task<string> GetPublicIpv6Async();

        /// <summary>
        /// Starts the periodic monitoring service.
        /// </summary>
        void StartMonitoring(int intervalSeconds = 300);

        /// <summary>
        /// Stops the monitoring service.
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Updates the endpoint in the WireGuard configuration or DDNS service.
        /// </summary>
        Task<bool> UpdateEndpointAsync(string newIp);
    }
}
