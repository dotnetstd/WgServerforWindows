using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public enum LogType
    {
        SystemLogs,
        OperationLogs
    }

    public partial class LogsViewModel : ObservableObject
    {
        private readonly ILogService _logService;
        private readonly IOperationLogService _operationLogService;
        private readonly IToastService _toastService;
        private System.Timers.Timer _autoRefreshTimer;

        [ObservableProperty]
        private string _logContent = "Loading logs...";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _isAutoRefreshEnabled;

        [ObservableProperty]
        private LogType _selectedLogType = LogType.OperationLogs;

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

        partial void OnSelectedLogTypeChanged(LogType value)
        {
            RefreshLogsCommand.Execute(null);
        }

        public LogsViewModel(ILogService logService, IOperationLogService operationLogService, IToastService toastService)
        {
            _logService = logService;
            _operationLogService = operationLogService;
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

        [RelayCommand]
        private void ClearLogs()
        {
            if (SelectedLogType == LogType.OperationLogs)
            {
                _operationLogService.ClearLogs();
                _toastService.Show("Operation logs cleared", ToastType.Success);
                RefreshLogsCommand.Execute(null);
            }
            else
            {
                _toastService.Show("System logs cannot be cleared from this interface", ToastType.Info);
            }
        }

        private async Task RefreshLogsAsync(bool showToast)
        {
            if (IsLoading) return;

            IsLoading = true;
            
            try
            {
                string logs;
                if (SelectedLogType == LogType.SystemLogs)
                {
                    logs = await _logService.GetLogsAsync();
                    if (string.IsNullOrWhiteSpace(logs))
                    {
                        LogContent = "No system logs available or failed to retrieve logs.";
                    }
                    else
                    {
                        LogContent = logs;
                    }
                }
                else
                {
                    logs = _operationLogService.GetAllLogs();
                    if (string.IsNullOrWhiteSpace(logs))
                    {
                        LogContent = "No operation logs available.";
                    }
                    else
                    {
                        LogContent = logs;
                    }
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
