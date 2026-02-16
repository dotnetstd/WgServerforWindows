using System;

namespace WgServerforWindows.Services.Interfaces
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public interface IOperationLogService
    {
        void Log(string message, LogLevel level = LogLevel.Info);
        string GetLogs();
        string GetAllLogs();
    }
}
