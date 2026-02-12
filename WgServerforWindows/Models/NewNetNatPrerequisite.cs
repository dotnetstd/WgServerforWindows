using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;
using SharpConfig;
using WgServerforWindows.Controls;
using WgServerforWindows.Properties;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public class NewNetNatPrerequisite : PrerequisiteItem
    {
        #region PrerequisiteItem members

        public NewNetNatPrerequisite(INetworkService networkService) : this(networkService, new NewNetIpAddressTaskSubCommand(networkService), new NetNatRangeSubCommand()) { }

        public NewNetNatPrerequisite(INetworkService networkService, NewNetIpAddressTaskSubCommand newNetIpAddressTaskSubCommand, NetNatRangeSubCommand netNatRangeSubCommand) : base
        (
            title: Resources.NewNatName,
            successMessage: Resources.NewNetSuccess,
            errorMessage: Resources.NewNetError,
            resolveText: Resources.NewNatResolve,
            configureText: Resources.NewNatConfigure
        )
        {
            _networkService = networkService;
            _newNetIpAddressTaskSubCommand = newNetIpAddressTaskSubCommand;
            SubCommands.Add(_newNetIpAddressTaskSubCommand);
            SubCommands.Add(netNatRangeSubCommand);
        }

        public override BooleanTimeCachedProperty Fulfilled => _fulfilled ??= new BooleanTimeCachedProperty(TimeSpan.FromSeconds(1), () =>
        {
            if (!File.Exists(ServerConfigurationPrerequisite.ServerDataPath))
            {
                // The server config doesn't exist yet.
                // We can't even evaluate what the NAT should be.
                return false;
            }

            bool result = true;
            var serverConfiguration = new ServerConfiguration().Load<ServerConfiguration>(Configuration.LoadFromFile(ServerConfigurationPrerequisite.ServerDataPath));

            // Verify the NAT rule exists and is correct
            result &= _networkService.CheckNatRule(_netNatName, GetDesiredAddressRange(serverConfiguration));

            // Verify the interface's IP address is correct
            result &= _networkService.CheckInterfaceIp(GlobalAppSettings.Instance.TunnelServiceName, serverConfiguration.IpAddress);

            return result;
        });
        private BooleanTimeCachedProperty _fulfilled;

        public override void Resolve()
        {
            Resolve(default);
        }

        public void Resolve(string serverDataPath)
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            var serverConfiguration = new ServerConfiguration().Load<ServerConfiguration>(Configuration.LoadFromFile(serverDataPath ?? ServerConfigurationPrerequisite.ServerDataPath));

            try
            {
                // Remove any pre-existing IP addresses on this interface, ignore errors
                _networkService.RemoveInterfaceIp(GlobalAppSettings.Instance.TunnelServiceName);

                // Assign the IP address to the interface
                _networkService.SetInterfaceIp(GlobalAppSettings.Instance.TunnelServiceName, serverConfiguration.IpAddress, int.Parse(serverConfiguration.Subnet));

                // Remove any existing NAT routing rule, ignore errors
                _networkService.RemoveNatRule(_netNatName);

                // Create the NAT routing rule
                _networkService.CreateNatRule(_netNatName, GetDesiredAddressRange(serverConfiguration));

                // If we get here, we know NAT routing succeeded

                // Invoke our subcommand
                _newNetIpAddressTaskSubCommand.Resolve(serverDataPath);
            }
            catch (Exception ex)
            {
                // If we get here, likely New-NetNat failed.
                // Windows is telling us that New-NetNat is unsupported. Ask the user if they want to try enabling Hyper-V.
                var res = MessageBox.Show(Resources.PromptForHyperV, Resources.WS4W, MessageBoxButton.YesNo);

                if (res == MessageBoxResult.Yes)
                {
                    // Let's try to enabled Hyper-V.
                    if (_networkService.EnableHyperV())
                    {
                        // Seems to have installed successfully. Prompt for reboot
                        MessageBox.Show(Resources.PromptForHyperVReboot, Resources.WS4W, MessageBoxButton.OK);
                    }
                    else
                    {
                        WaitCursor.SetOverrideCursor(null);

                        // If we get here, the Hyper-V install failed for some reason (e.g., Windows Home). Recommend ICS.
                        new UnhandledErrorWindow
                        {
                            DataContext = new UnhandledErrorWindowModel
                            {
                                Title = Resources.Error,
                                Text = Resources.HyperVErrorNatRoutingNotSupported,
                                Exception = ex
                            }
                        }.ShowDialog();
                    }
                }
                else
                {
                    WaitCursor.SetOverrideCursor(null);

                    // If we get here, the user chose not to install Hyper-V. Recommend ICS.
                    new UnhandledErrorWindow
                    {
                        DataContext = new UnhandledErrorWindowModel
                        {
                            Title = Resources.Error,
                            Text = Resources.NatRoutingNotSupported,
                            Exception = ex
                        }
                    }.ShowDialog();
                }
            }

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        public override void Configure()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            // Delete the NAT rule
            _networkService.RemoveNatRule(_netNatName);

            // Invoke our subcommand
            _newNetIpAddressTaskSubCommand.Configure();

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        public override string Category => Resources.NetworkAddressTranslation;

        #endregion

        #region Private methods

        private string GetDesiredAddressRange(ServerConfiguration serverConfiguration)
        {
            if (!string.IsNullOrEmpty(GlobalAppSettings.Instance.CustomNetNatRange))
            {
                return GlobalAppSettings.Instance.CustomNetNatRange;
            }
            else
            {
                return serverConfiguration.AddressProperty.Value;
            }
        }

        #endregion

        #region Public properties

        public bool IsSupported
        {
            get
            {
                return _networkService.IsNatSupported();
            }
        }

        #endregion

        #region Private readonly

        private readonly string _netNatName = "wg_server_nat";

        private readonly NewNetIpAddressTaskSubCommand _newNetIpAddressTaskSubCommand;

        private readonly INetworkService _networkService;

        #endregion
    }
}
