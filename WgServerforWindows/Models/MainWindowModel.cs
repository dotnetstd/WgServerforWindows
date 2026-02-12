using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SharpConfig;
using WgServerforWindows.Controls;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public class MainWindowModel : ObservableObject
    {
        public AppSettings Settings => AppSettings.Instance;

        public List<PrerequisiteItem> PrerequisiteItems { get; set; } = new List<PrerequisiteItem>();

        public List<PrerequisiteItem> TunnelItems => PrerequisiteItems.Where(i => i is not SettingsPrerequisite).ToList();
        public List<PrerequisiteItem> SettingsItems => PrerequisiteItems.Where(i => i is SettingsPrerequisite).ToList();

        public MainWindowModel(INetworkService networkService)
        {
            // Never put quotes around config file values
            Configuration.OutputRawStringValues = true;

            var wireGuardExePrerequisite = new WireGuardExePrerequisite(networkService);
            var openServerConfigDirectorySubCommand = new OpenServerConfigDirectorySubCommand();
            var changeServerConfigDirectorySubCommand = new ChangeServerConfigDirectorySubCommand();
            var serverConfigurationPrerequisite = new ServerConfigurationPrerequisite(networkService, openServerConfigDirectorySubCommand, changeServerConfigDirectorySubCommand);
            var openClientConfigDirectorySubCommand = new OpenClientConfigDirectorySubCommand();
            var changeClientConfigDirectorySubCommand = new ChangeClientConfigDirectorySubCommand();
            var clientConfigurationsPrerequisite = new ClientConfigurationsPrerequisite(networkService, openClientConfigDirectorySubCommand, changeClientConfigDirectorySubCommand);
            var tunnelServiceNameSubCommand = new TunnelServiceNameSubCommand();
            var tunnelServicePrerequisite = new TunnelServicePrerequisite(networkService, tunnelServiceNameSubCommand);
            var privateNetworkTaskSubCommand = new PrivateNetworkTaskSubCommand(networkService);
            var privateNetworkPrerequisite = new PrivateNetworkPrerequisite(networkService, privateNetworkTaskSubCommand);
            var netIpAddressTaskSubCommand = new NewNetIpAddressTaskSubCommand(networkService);
            var netNatRangeSubCommand = new NetNatRangeSubCommand();
            var newNetNatPrerequisite = new NewNetNatPrerequisite(networkService, netIpAddressTaskSubCommand, netNatRangeSubCommand);
            var internetSharingPrerequisite = new InternetSharingPrerequisite(networkService);
            var persistentInternetSharingPrerequisite = new PersistentInternetSharingPrerequisite(networkService);
            var serverStatusPrerequisite = new ServerStatusPrerequisite(networkService);
            var bootTaskDelaySubCommand = new BootTaskDelaySubCommand();
            var settingsPrerequisite = new SettingsPrerequisite(bootTaskDelaySubCommand);

            // -- Set up interdependencies --

            // Can't uninstall WireGuard while Tunnel is installed
            wireGuardExePrerequisite.CanConfigureFunc = () => tunnelServicePrerequisite.Fulfilled == false;

            // Can't resolve or configure server or client unless WireGuard is installed
            serverConfigurationPrerequisite.CanResolveFunc = clientConfigurationsPrerequisite.CanResolveFunc =
            serverConfigurationPrerequisite.CanConfigureFunc = clientConfigurationsPrerequisite.CanConfigureFunc = () => wireGuardExePrerequisite.Fulfilled;
            
            // Can't rename the tunnel service if it's already installed
            tunnelServiceNameSubCommand.CanConfigureFunc = () => tunnelServicePrerequisite.Fulfilled == false;

            // Can't install tunnel until WireGuard exe is installed and server is configured
            tunnelServicePrerequisite.CanResolveFunc = () =>
                wireGuardExePrerequisite.Fulfilled && serverConfigurationPrerequisite.Fulfilled;

            // Can't uninstall the tunnel while internet sharing is enabled
            tunnelServicePrerequisite.CanConfigureFunc = () => internetSharingPrerequisite.Fulfilled == false && newNetNatPrerequisite.Fulfilled == false;
            
            // Can't enable private network unless tunnel is installed, and private network must not be informational
            privateNetworkPrerequisite.CanResolveFunc = () => tunnelServicePrerequisite.Fulfilled &&
                                                              privateNetworkPrerequisite.IsInformational == false;

            // Can't configure private network if it's only information (e.g., on a domain)
            privateNetworkPrerequisite.CanConfigureFunc = () => privateNetworkPrerequisite.IsInformational == false;

            // Can't enable/disable automatic private network if it's not already enabled.
            privateNetworkTaskSubCommand.CanResolveFunc = privateNetworkTaskSubCommand.CanConfigureFunc = () => privateNetworkPrerequisite.Fulfilled;

            // Can't enable internet sharing unless tunnel is installed
            internetSharingPrerequisite.CanResolveFunc = () => tunnelServicePrerequisite.Fulfilled;

            // Can't view server status unless tunnel is installed
            serverStatusPrerequisite.CanConfigureFunc = () => tunnelServicePrerequisite.Fulfilled;

            // Can't open server or folders unless they exist
            openServerConfigDirectorySubCommand.CanConfigureFunc = () => Directory.Exists(ServerConfigurationPrerequisite.ServerConfigDirectory);
            openClientConfigDirectorySubCommand.CanConfigureFunc = () => Directory.Exists(ClientConfigurationsPrerequisite.ClientConfigDirectory);

            // Add the prereqs to the Model
            PrerequisiteItems.Add(wireGuardExePrerequisite);
            PrerequisiteItems.Add(serverConfigurationPrerequisite);
            PrerequisiteItems.Add(clientConfigurationsPrerequisite);
            PrerequisiteItems.Add(tunnelServicePrerequisite);
            PrerequisiteItems.Add(privateNetworkPrerequisite);

            if (newNetNatPrerequisite.IsSupported)
            {
                internetSharingPrerequisite.CanResolveFunc = () => tunnelServicePrerequisite.Fulfilled && !newNetNatPrerequisite.Fulfilled;
                persistentInternetSharingPrerequisite.CanResolveFunc = () => !newNetNatPrerequisite.Fulfilled;
                newNetNatPrerequisite.CanResolveFunc = () => serverConfigurationPrerequisite.Fulfilled
                                                             && tunnelServicePrerequisite.Fulfilled
                                                             && !internetSharingPrerequisite.Fulfilled
                                                             && !persistentInternetSharingPrerequisite.Fulfilled;

                netIpAddressTaskSubCommand.CanResolveFunc = netIpAddressTaskSubCommand.CanConfigureFunc = () => newNetNatPrerequisite.Fulfilled;
                netNatRangeSubCommand.CanConfigureFunc = () => serverConfigurationPrerequisite.Fulfilled;

                var natPrerequisiteGroup = new NatPrerequisiteGroup(newNetNatPrerequisite, internetSharingPrerequisite, persistentInternetSharingPrerequisite);

                if (internetSharingPrerequisite.Fulfilled || persistentInternetSharingPrerequisite.Fulfilled)
                {
                    natPrerequisiteGroup.SelectedChildIndex = 1;
                }

                PrerequisiteItems.Add(natPrerequisiteGroup);
            }
            else
            {
                PrerequisiteItems.Add(internetSharingPrerequisite);
                PrerequisiteItems.Add(persistentInternetSharingPrerequisite);
            }

            PrerequisiteItems.Add(serverStatusPrerequisite);
            PrerequisiteItems.Add(settingsPrerequisite);

            // If one of the prereqs changes, check the validity of all of them.
            // Do this recursively.
            PrerequisiteItems.ForEach(AddPrerequisiteItemFulfilledChangedHandler);
        }

        private void AddPrerequisiteItemFulfilledChangedHandler(PrerequisiteItem prerequisiteItem)
        {
            prerequisiteItem.PropertyChanged += PrerequisiteItemFulfilledChanged;
            prerequisiteItem.Children.ToList().ForEach(AddPrerequisiteItemFulfilledChangedHandler);
            prerequisiteItem.SubCommands.ToList().ForEach(AddPrerequisiteItemFulfilledChangedHandler);
        }

        private void RemovePrerequisiteItemFulfilledChangedHandler(PrerequisiteItem prerequisiteItem)
        {
            prerequisiteItem.PropertyChanged -= PrerequisiteItemFulfilledChanged;
            prerequisiteItem.Children.ToList().ForEach(RemovePrerequisiteItemFulfilledChangedHandler);
            prerequisiteItem.SubCommands.ToList().ForEach(RemovePrerequisiteItemFulfilledChangedHandler);
        }

        private void PrerequisiteItemFulfilledChanged(object sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Unsubscribe before invoking on everyone
                PrerequisiteItems.ForEach(RemovePrerequisiteItemFulfilledChangedHandler);

                WaitCursor.SetOverrideCursor(Cursors.Wait);

                if (sender is PrerequisiteItem senderItem && e.PropertyName == nameof(PrerequisiteItem.Fulfilled))
                {
                    // Now invoke on all but the sender
                    PrerequisiteItems.Where(i => i != senderItem).ToList().ForEach(prerequisiteItem =>
                    {
                        void RaisePropertiesChanged(PrerequisiteItem i)
                        {
                            i.RaisePropertyChanged(nameof(i.Fulfilled));
                            i.RaisePropertyChanged(nameof(i.IsInformational));
                            i.RaisePropertyChanged(nameof(i.CanConfigure));
                            i.RaisePropertyChanged(nameof(i.CanResolve));

                            i.Children.Where(i2 => i2 != senderItem).ToList().ForEach(RaisePropertiesChanged);
                            i.SubCommands.Where(i2 => i2 != senderItem).ToList().ForEach(RaisePropertiesChanged);
                        }

                        RaisePropertiesChanged(prerequisiteItem);
                    });
                }

                WaitCursor.SetOverrideCursor(null);

                // Now we can resubscribe to all
                PrerequisiteItems.ForEach(AddPrerequisiteItemFulfilledChangedHandler);
            });
        }
    }
}
