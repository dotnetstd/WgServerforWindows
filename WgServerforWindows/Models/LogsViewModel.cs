using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public partial class LogsViewModel : ObservableObject
    {
        private readonly ILogService _logService;
        private readonly IToastService _toastService;
        private System.Timers.Timer _autoRefreshTimer;

        [ObservableProperty]
        private string _logContent = "Loading logs...";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isAutoRefreshEnabled;

        partial void OnIsAutoRefreshEnabledChanged(bool value)
        {
            if (value)
            {
                _autoRefreshTimer?.Start();
            }
            else
            {
                _autoRefreshTimer?.Stop();
            }
        }

        public LogsViewModel(ILogService logService, IToastService toastService)
        {
            _logService = logService;
            _toastService = toastService;

            _autoRefreshTimer = new System.Timers.Timer(5000); // 5 seconds
            _autoRefreshTimer.Elapsed += async (s, e) =>
            {
                // Ensure we update on UI thread for LogContent
                await App.Current.Dispatcher.InvokeAsync(async () => await RefreshLogsAsync(false));
            };
            _autoRefreshTimer.AutoReset = true;
            
            // Initial load
            RefreshLogsCommand.Execute(null);
        }

        [RelayCommand]
        private async Task RefreshLogs()
        {
            await RefreshLogsAsync(true);
        }

        private async Task RefreshLogsAsync(bool showToast)
        {
            if (IsLoading) return;

            IsLoading = true;
            
            try
            {
                var logs = await _logService.GetLogsAsync();
                if (string.IsNullOrWhiteSpace(logs))
                {
                    LogContent = "No logs available or failed to retrieve logs.";
                }
                else
                {
                    LogContent = logs;
                }

                if (showToast)
                {
                    _toastService.Show("Logs refreshed", ToastType.Success);
                }
            }
            catch (Exception ex)
            {
                LogContent = $"Error retrieving logs: {ex.Message}";
                if (showToast)
                {
                    _toastService.Show($"Failed to retrieve logs: {ex.Message}", ToastType.Error);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
