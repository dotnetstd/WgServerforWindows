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

    public interface IOperationLogService : IDisposable
    {
        void Log(string message, LogLevel level = LogLevel.Info);
        void LogException(string message, Exception ex, LogLevel level = LogLevel.Error);
        void LogMethodEntry(string methodName, params object[] parameters);
        void LogMethodExit(string methodName, object result = null);
        string GetLogs();
        string GetAllLogs();
        string GetCrashLogs();
        void ClearLogs();
        void ClearCrashLogs();
        void Flush();
    }
}
