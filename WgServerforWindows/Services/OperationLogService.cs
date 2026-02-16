using System;
using System.IO;
using System.Text;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class OperationLogService : IOperationLogService
    {
        private readonly string _logFilePath;

        public OperationLogService()
        {
            // 创建日志文件路径
            string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WS4W", "Logs");
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            _logFilePath = Path.Combine(logDirectory, $"operation_{DateTime.Now:yyyyMMdd}.log");
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 忽略日志写入错误
            }
        }

        public string GetLogs()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    return File.ReadAllText(_logFilePath, Encoding.UTF8);
                }
                return "No operation logs available.";
            }
            catch
            {
                return "Error reading operation logs.";
            }
        }

        public string GetAllLogs()
        {
            try
            {
                string logDirectory = Path.GetDirectoryName(_logFilePath);
                if (Directory.Exists(logDirectory))
                {
                    StringBuilder logsBuilder = new StringBuilder();
                    string[] logFiles = Directory.GetFiles(logDirectory, "operation_*.log");
                    Array.Sort(logFiles, (a, b) => b.CompareTo(a)); // 按日期倒序排列

                    foreach (string logFile in logFiles)
                    {
                        logsBuilder.AppendLine($"=== {Path.GetFileName(logFile)} ===");
                        logsBuilder.AppendLine(File.ReadAllText(logFile, Encoding.UTF8));
                        logsBuilder.AppendLine();
                    }

                    return logsBuilder.ToString();
                }
                return "No operation logs available.";
            }
            catch
            {
                return "Error reading operation logs.";
            }
        }
    }
}
