using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using SharpConfig;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public partial class MtuWizardViewModel : ObservableObject
    {
        private readonly INetworkService _networkService;

        [ObservableProperty]
        private string _targetHost = "8.8.8.8";

        [ObservableProperty]
        private int _progress;

        [ObservableProperty]
        private string _statusMessage = "Ready to start.";

        [ObservableProperty]
        private int _resultMtu;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isCompleted;

        public MtuWizardViewModel(INetworkService networkService)
        {
            _networkService = networkService;
        }

        [RelayCommand]
        private async Task StartEstimationAsync()
        {
            IsBusy = true;
            IsCompleted = false;
            Progress = 0;

            ResultMtu = await _networkService.EstimateMtuAsync(TargetHost, (p, m) =>
            {
                Progress = p;
                StatusMessage = m;
            });

            IsBusy = false;
            IsCompleted = true;
        }

        [RelayCommand]
        private void ApplyMtu()
        {
            try
            {
                string configPath = ServerConfigurationPrerequisite.ServerDataPath;
                if (!File.Exists(configPath)) return;

                // Load configuration
                var config = Configuration.LoadFromFile(configPath);
                var serverConfiguration = new ServerConfiguration().Load<ServerConfiguration>(config);

                // Update MTU. Note: WireGuard MTU is typically 80 bytes smaller than physical MTU for IPv6.
                // We'll subtract 80 to be safe and efficient.
                serverConfiguration.MtuProperty.Value = (ResultMtu - 80).ToString();

                // Save configuration
                serverConfiguration.Save(config);
                config.SaveToFile(configPath);

                _networkService.SyncConfiguration(GlobalAppSettings.Instance.TunnelServiceName, configPath);
            }
            catch
            {
                // Handle or ignore
            }
        }
    }
}