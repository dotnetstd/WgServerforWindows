using System;

namespace WgServerforWindows.Services.Interfaces
{
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public interface IToastService
    {
        event Action<string, ToastType, string, int> OnToastRequested;
        void Show(string message, ToastType type = ToastType.Info, string title = null, int durationSeconds = 3);
    }
}
