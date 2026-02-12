using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Windows.Input;
using Humanizer;
// using NETCONLib; // Removed for .NET Core compatibility
using WgServerforWindows.Controls;
using WgServerforWindows.Properties;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public class InternetSharingPrerequisite : PrerequisiteItem
    {
        #region PrerequisiteItem members

        private readonly INetworkService _networkService;

        public InternetSharingPrerequisite(INetworkService networkService) : base
        (
            title: Resources.InternetSharingTitle,
            successMessage: Resources.InternetSharingSuccess,
            errorMessage: Resources.InternetSharingError,
            resolveText: Resources.InternetSharingResolve,
            configureText: Resources.InternetSharingConfigure
        )
        {
            _networkService = networkService;
        }

        public override BooleanTimeCachedProperty Fulfilled => _fulfilled ??= new BooleanTimeCachedProperty(TimeSpan.FromSeconds(1), () =>
        {
            return _networkService.IsIcsEnabled(GlobalAppSettings.Instance.TunnelServiceName);
        });
        private BooleanTimeCachedProperty _fulfilled;

        public override void Resolve()
        {
            Resolve(default);
        }

        public void Resolve(string networkToShare)
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            if (string.IsNullOrEmpty(networkToShare))
            {
                // If no network is specified, we can't really resolve it.
                // However, the UI should probably handle the selection.
            }
            else
            {
                try
                {
                    _networkService.EnableIcs(networkToShare, GlobalAppSettings.Instance.TunnelServiceName);
                }
                catch
                {
                    // Error handling is handled by the base class via Refresh/Fulfilled
                }
            }

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        public override void Configure()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            _networkService.DisableIcs(GlobalAppSettings.Instance.TunnelServiceName);

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        /// <summary>
        /// Returns the network(s) (if any) that is/are currently being shared.
        /// </summary>
        public List<string> GetSharedNetworks()
        {
            return _networkService.GetSharedNetworks();
        }

        public override string Category => Resources.InternetConnectionSharing;

        #endregion
    }
}
