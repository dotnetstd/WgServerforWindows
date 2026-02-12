using System;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class ToastService : IToastService
    {
        public event Action<string, ToastType, string, int> OnToastRequested;

        public void Show(string message, ToastType type = ToastType.Info, string title = null, int durationSeconds = 3)
        {
            OnToastRequested?.Invoke(message, type, title, durationSeconds);
        }
    }
}
