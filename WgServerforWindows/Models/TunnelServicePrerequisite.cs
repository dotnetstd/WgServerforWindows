using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Input;
using SharpConfig;
using WgAPI;
using WgAPI.Commands;
using WgServerforWindows.Controls;
using WgServerforWindows.Properties;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public class TunnelServicePrerequisite : PrerequisiteItem
    {
        private readonly INetworkService _networkService;

        public TunnelServicePrerequisite(INetworkService networkService) : this(networkService, new TunnelServiceNameSubCommand())
        {
        }

        public TunnelServicePrerequisite(INetworkService networkService, TunnelServiceNameSubCommand tunnelServiceNameSubCommand) : base
        (
            title: Resources.TunnelService,
            successMessage: Resources.TunnelServiceInstalled,
            errorMessage: Resources.TunnelServiceNotInstalled,
            resolveText: Resources.InstallTunnelService,
            configureText: Resources.UninstallTunnelService
        )
        {
            _networkService = networkService;
            SubCommands.Add(tunnelServiceNameSubCommand);
        }

        public override BooleanTimeCachedProperty Fulfilled => _fulfilled ??= new BooleanTimeCachedProperty(TimeSpan.FromSeconds(1), () =>
        {
            return _networkService.IsTunnelServiceInstalled(GlobalAppSettings.Instance.TunnelServiceName);
        });
        private BooleanTimeCachedProperty _fulfilled;

        public override async void Resolve()
        {
            try
            {
                // Load the server config and check the listen port
                ServerConfiguration serverConfiguration = new ServerConfiguration().Load<ServerConfiguration>(Configuration.LoadFromFile(ServerConfigurationPrerequisite.ServerDataPath));
                string listenPort = serverConfiguration.ListenPortProperty.Value;

                if (int.TryParse(listenPort, out int listenPortInt))
                {
                    bool anyTcpListener = _networkService.IsPortInUse(listenPortInt, udp: false);
                    bool anyUdpListener = _networkService.IsPortInUse(listenPortInt, udp: true);

                    if (anyUdpListener)
                    {
                        // Give the user strong warning about UDP listener
                        bool canceled = false;
                        UnhandledErrorWindow portWarningDialog = new UnhandledErrorWindow();
                        portWarningDialog.DataContext = new UnhandledErrorWindowModel
                        {
                            Title = Resources.PotentialPortConflict,
                            Text = string.Format(Resources.UDPPortConflictMessage, listenPort),
                            SecondaryButtonText = Resources.Cancel,
                            SecondaryButtonAction = () =>
                            {
                                canceled = true;
                                portWarningDialog.Close();
                            }
                        };
                        portWarningDialog.ShowDialog();

                        if (canceled)
                        {
                            return;
                        }
                    }
                    else if (anyTcpListener)
                    {
                        // Give the user less strong warning about TCP listener
                        bool canceled = false;
                        UnhandledErrorWindow portWarningDialog = new UnhandledErrorWindow();
                        portWarningDialog.DataContext = new UnhandledErrorWindowModel
                        {
                            Title = Resources.PotentialPortConflict,
                            Text = string.Format(Resources.TCPPortConflictMessage, listenPort),
                            SecondaryButtonText = Resources.Cancel,
                            SecondaryButtonAction = () =>
                            {
                                canceled = true;
                                portWarningDialog.Close();
                            }
                        };
                        portWarningDialog.ShowDialog();

                        if (canceled)
                        {
                            return;
                        }
                    }
                }
            }
            catch
            {
                // If we can't verify the listen port, it's ok.
            }

            WaitCursor.SetOverrideCursor(Cursors.Wait);

            using (TemporaryFile temporaryFile = new(ServerConfigurationPrerequisite.ServerWGPath, ServerConfigurationPrerequisite.ServerWGPathWithCustomTunnelName))
            {
                _networkService.InstallTunnelService(temporaryFile.NewFilePath);
            }
            
            await WaitForFulfilled();
            
            WaitCursor.SetOverrideCursor(null);
        }

        public override async void Configure()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            _networkService.UninstallTunnelService(GlobalAppSettings.Instance.TunnelServiceName);
            await WaitForFulfilled(false);

            WaitCursor.SetOverrideCursor(null);
        }
    }
}
