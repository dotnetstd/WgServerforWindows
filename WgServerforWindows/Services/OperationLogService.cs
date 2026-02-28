using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class OperationLogService : IOperationLogService, IDisposable
    {
        private readonly string _logDirectory;
        private readonly string _crashLogFile;
        private string _currentLogFile;
        private readonly ConcurrentQueue<string> _logQueue;
        private readonly Timer _flushTimer;
        private readonly object _lockObject = new object();
        private bool _disposed = false;
        private readonly int _maxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private readonly int _maxLogFiles = 30; // Keep 30 days of logs
        private readonly int _maxCrashLogSizeBytes = 5 * 1024 * 1024; // 5MB for crash logs

        public OperationLogService()
        {
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WS4W", "Logs");
            
            if (!Directory.Exists(_logDirectory))
            {
                try
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                catch
                {
                    _logDirectory = Path.Combine(Path.GetTempPath(), "WS4W", "Logs");
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            
            _currentLogFile = GetLogFilePath();
            _crashLogFile = Path.Combine(_logDirectory, "crash.log");
            _logQueue = new ConcurrentQueue<string>();
            
            _flushTimer = new Timer(FlushLogs, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            
            Task.Run(() => CleanupOldLogFiles());
            
            Log("OperationLogService initialized", LogLevel.Info);
        }

        private string GetLogFilePath()
        {
            return Path.Combine(_logDirectory, $"operation_{DateTime.Now:yyyyMMdd}.log");
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] [{Thread.CurrentThread.ManagedThreadId}] {message}";
                _logQueue.Enqueue(logEntry);
                
                System.Diagnostics.Debug.WriteLine(logEntry);
                
                if (level == LogLevel.Error)
                {
                    FlushImmediate(logEntry);
                }
            }
            catch
            {
            }
        }

        public void LogException(string message, Exception ex, LogLevel level = LogLevel.Error)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level.ToString().ToUpperInvariant()}] [{Thread.CurrentThread.ManagedThreadId}] {message}");
                sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
                sb.AppendLine($"Exception Message: {ex.Message}");
                sb.AppendLine($"Stack Trace: {ex.StackTrace}");
                
                Exception innerEx = ex.InnerException;
                int innerCount = 1;
                while (innerEx != null && innerCount <= 10)
                {
                    sb.AppendLine($"--- Inner Exception {innerCount} ---");
                    sb.AppendLine($"Inner Exception Type: {innerEx.GetType().FullName}");
                    sb.AppendLine($"Inner Exception Message: {innerEx.Message}");
                    sb.AppendLine($"Inner Stack Trace: {innerEx.StackTrace}");
                    innerEx = innerEx.InnerException;
                    innerCount++;
                }
                
                if (ex is AggregateException aggEx)
                {
                    sb.AppendLine("--- Aggregate Exceptions ---");
                    foreach (var inner in aggEx.InnerExceptions)
                    {
                        sb.AppendLine($"Exception Type: {inner.GetType().FullName}");
                        sb.AppendLine($"Exception Message: {inner.Message}");
                        sb.AppendLine($"Stack Trace: {inner.StackTrace}");
                    }
                }
                
                string logEntry = sb.ToString();
                _logQueue.Enqueue(logEntry);
                System.Diagnostics.Debug.WriteLine(logEntry);
                
                FlushImmediate(logEntry);
                WriteCrashLog(logEntry);
            }
            catch
            {
            }
        }

        public void LogMethodEntry(string methodName, params object[] parameters)
        {
            try
            {
                string paramStr = parameters != null && parameters.Length > 0 
                    ? string.Join(", ", parameters) 
                    : "none";
                Log($"[METHOD ENTRY] {methodName} - Parameters: {paramStr}", LogLevel.Debug);
            }
            catch
            {
            }
        }

        public void LogMethodExit(string methodName, object result = null)
        {
            try
            {
                string resultStr = result != null ? result.ToString() : "void/null";
                Log($"[METHOD EXIT] {methodName} - Result: {resultStr}", LogLevel.Debug);
            }
            catch
            {
            }
        }

        private void FlushImmediate(string logEntry)
        {
            try
            {
                string expectedLogFile = GetLogFilePath();
                if (_currentLogFile != expectedLogFile)
                {
                    _currentLogFile = expectedLogFile;
                }
                
                lock (_lockObject)
                {
                    File.AppendAllText(_currentLogFile, logEntry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        private void WriteCrashLog(string logEntry)
        {
            try
            {
                lock (_lockObject)
                {
                    if (File.Exists(_crashLogFile))
                    {
                        var fileInfo = new FileInfo(_crashLogFile);
                        if (fileInfo.Length > _maxCrashLogSizeBytes)
                        {
                            string backupFile = Path.Combine(_logDirectory, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                            File.Move(_crashLogFile, backupFile);
                            
                            Task.Run(() =>
                            {
                                try
                                {
                                    var crashFiles = Directory.GetFiles(_logDirectory, "crash_*.log");
                                    if (crashFiles.Length > 10)
                                    {
                                        Array.Sort(crashFiles);
                                        for (int i = 0; i < crashFiles.Length - 10; i++)
                                        {
                                            File.Delete(crashFiles[i]);
                                        }
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    
                    File.AppendAllText(_crashLogFile, logEntry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public void Flush()
        {
            FlushLogs(null);
        }

        private void FlushLogs(object state)
        {
            if (_logQueue.IsEmpty) return;
            
            try
            {
                string expectedLogFile = GetLogFilePath();
                if (_currentLogFile != expectedLogFile)
                {
                    _currentLogFile = expectedLogFile;
                    Task.Run(() => CleanupOldLogFiles());
                }
                
                if (File.Exists(_currentLogFile))
                {
                    var fileInfo = new FileInfo(_currentLogFile);
                    if (fileInfo.Length > _maxLogFileSizeBytes)
                    {
                        string rotatedFile = Path.Combine(_logDirectory, $"operation_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                        File.Move(_currentLogFile, rotatedFile);
                        _currentLogFile = expectedLogFile;
                    }
                }
                
                StringBuilder sb = new StringBuilder();
                while (_logQueue.TryDequeue(out string logEntry))
                {
                    sb.AppendLine(logEntry);
                }
                
                if (sb.Length > 0)
                {
                    lock (_lockObject)
                    {
                        File.AppendAllText(_currentLogFile, sb.ToString(), Encoding.UTF8);
                    }
                }
            }
            catch
            {
            }
        }

        private void CleanupOldLogFiles()
        {
            try
            {
                var logFiles = Directory.GetFiles(_logDirectory, "operation_*.log");
                if (logFiles.Length > _maxLogFiles)
                {
                    Array.Sort(logFiles);
                    int filesToDelete = logFiles.Length - _maxLogFiles;
                    for (int i = 0; i < filesToDelete; i++)
                    {
                        try
                        {
                            File.Delete(logFiles[i]);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public string GetLogs()
        {
            try
            {
                FlushLogs(null);
                
                if (File.Exists(_currentLogFile))
                {
                    return File.ReadAllText(_currentLogFile, Encoding.UTF8);
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
                FlushLogs(null);
                
                if (Directory.Exists(_logDirectory))
                {
                    StringBuilder logsBuilder = new StringBuilder();
                    string[] logFiles = Directory.GetFiles(_logDirectory, "operation_*.log");
                    Array.Sort(logFiles, (a, b) => b.CompareTo(a));

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

        public string GetCrashLogs()
        {
            try
            {
                FlushLogs(null);
                
                if (File.Exists(_crashLogFile))
                {
                    return File.ReadAllText(_crashLogFile, Encoding.UTF8);
                }
                return "No crash logs available.";
            }
            catch
            {
                return "Error reading crash logs.";
            }
        }

        public void ClearLogs()
        {
            try
            {
                while (_logQueue.TryDequeue(out _)) { }
                
                if (Directory.Exists(_logDirectory))
                {
                    string[] logFiles = Directory.GetFiles(_logDirectory, "operation_*.log");
                    foreach (string logFile in logFiles)
                    {
                        try
                        {
                            File.Delete(logFile);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void ClearCrashLogs()
        {
            try
            {
                if (File.Exists(_crashLogFile))
                {
                    File.Delete(_crashLogFile);
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _flushTimer?.Dispose();
                FlushLogs(null);
            }
        }
    }
}
