using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using WgServerforWindows.Views;
using Microsoft.Extensions.DependencyInjection;
using WgServerforWindows.Services.Interfaces;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace WgServerforWindows.Models
{
    public partial class MainShellViewModel : ObservableObject
    {
        [ObservableProperty]
        private object _currentView;

        [ObservableProperty]
        private bool _isBusy;

        public ObservableCollection<ToastItem> Toasts { get; } = new();

        private readonly IToastService _toastService;

        public MainShellViewModel(IToastService toastService)
        {
            _toastService = toastService;
            _toastService.OnToastRequested += ToastService_OnToastRequested;

            // Default view
            Navigate("Dashboard");
        }

        private void ToastService_OnToastRequested(string message, ToastType type, string title, int durationSeconds)
        {
            var toast = new ToastItem
            {
                Message = message,
                Type = type,
                Title = title,
                DurationSeconds = durationSeconds
            };

            // Ensure UI thread
            App.Current.Dispatcher.Invoke(() =>
            {
                Toasts.Add(toast);
            });

            // Auto remove
            Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (Toasts.Contains(toast))
                    {
                        Toasts.Remove(toast);
                    }
                });
            });
        }

        [RelayCommand]
        private async Task Navigate(string viewName)
        {
            IsBusy = true;
            // Allow UI to update
            await Task.Delay(50);

            try
            {
                switch (viewName)
                {
                    case "Dashboard":
                        CurrentView = App.Current.Services.GetService<DashboardView>();
                        break;
                    case "Tunnels":
                        CurrentView = App.Current.Services.GetService<TunnelsView>();
                        break;
                    case "Logs":
                        CurrentView = App.Current.Services.GetService<LogsView>();
                        break;
                    case "Settings":
                        CurrentView = App.Current.Services.GetService<SettingsView>();
                        break;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
