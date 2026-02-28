using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Services
{
    public class BackgroundTaskManager : IDisposable
    {
        private readonly IOperationLogService _operationLogService;
        private readonly Dictionary<string, TaskInfo> _tasks = new Dictionary<string, TaskInfo>();
        private CancellationTokenSource _cts;
        private Task _backgroundTask;
        private bool _disposed = false;
        private readonly object _lockObject = new object();

        public BackgroundTaskManager(IOperationLogService operationLogService)
        {
            _operationLogService = operationLogService ?? throw new ArgumentNullException(nameof(operationLogService));
        }

        public void Start()
        {
            _operationLogService.LogMethodEntry(nameof(Start));
            
            try
            {
                lock (_lockObject)
                {
                    if (_cts != null && !_cts.IsCancellationRequested)
                    {
                        _operationLogService.Log("Background task manager is already running", LogLevel.Warning);
                        return;
                    }
                    
                    _cts = new CancellationTokenSource();
                    _backgroundTask = Task.Run(() => RunBackgroundTasksAsync(_cts.Token), _cts.Token);
                    _operationLogService.Log("Background task manager started successfully");
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Failed to start background task manager", ex);
                throw;
            }
            
            _operationLogService.LogMethodExit(nameof(Start));
        }

        public void Stop()
        {
            _operationLogService.LogMethodEntry(nameof(Stop));
            
            try
            {
                lock (_lockObject)
                {
                    if (_cts == null)
                    {
                        _operationLogService.Log("Background task manager is not running", LogLevel.Warning);
                        return;
                    }
                    
                    _cts.Cancel();
                    
                    // Wait for the background task to complete with timeout
                    if (_backgroundTask != null)
                    {
                        Task.WaitAny(_backgroundTask, Task.Delay(5000));
                    }
                    
                    _cts.Dispose();
                    _cts = null;
                    _backgroundTask = null;
                }
                
                _operationLogService.Log("Background task manager stopped successfully");
            }
            catch (Exception ex)
            {
                _operationLogService.LogException("Error stopping background task manager", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(Stop));
        }

        public void RegisterTask(string taskName, Func<Task<bool>> taskFunc, TimeSpan interval, int maxRetries)
        {
            _operationLogService.LogMethodEntry(nameof(RegisterTask), taskName, interval, maxRetries);
            
            if (string.IsNullOrEmpty(taskName))
                throw new ArgumentNullException(nameof(taskName));
            if (taskFunc == null)
                throw new ArgumentNullException(nameof(taskFunc));
            if (interval.TotalSeconds < 1)
                throw new ArgumentException("Interval must be at least 1 second", nameof(interval));
            if (maxRetries < 0)
                throw new ArgumentException("Max retries cannot be negative", nameof(maxRetries));
            
            try
            {
                lock (_lockObject)
                {
                    _tasks[taskName] = new TaskInfo
                    {
                        TaskFunc = taskFunc,
                        Interval = interval,
                        MaxRetries = maxRetries,
                        LastRunTime = DateTime.MinValue,
                        RetryCount = 0,
                        ConsecutiveFailures = 0
                    };
                }
                
                _operationLogService.Log($"Registered background task: {taskName}, Interval: {interval.TotalMinutes:F1} minutes, MaxRetries: {maxRetries}");
            }
            catch (Exception ex)
            {
                _operationLogService.LogException($"Failed to register background task: {taskName}", ex);
                throw;
            }
            
            _operationLogService.LogMethodExit(nameof(RegisterTask));
        }

        public void UnregisterTask(string taskName)
        {
            _operationLogService.LogMethodEntry(nameof(UnregisterTask), taskName);
            
            try
            {
                lock (_lockObject)
                {
                    if (_tasks.Remove(taskName))
                    {
                        _operationLogService.Log($"Unregistered background task: {taskName}");
                    }
                    else
                    {
                        _operationLogService.Log($"Task not found for unregistration: {taskName}", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                _operationLogService.LogException($"Failed to unregister background task: {taskName}", ex);
            }
            
            _operationLogService.LogMethodExit(nameof(UnregisterTask));
        }

        private async Task RunBackgroundTasksAsync(CancellationToken cancellationToken)
        {
            _operationLogService.Log("Background task loop started");
            
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    List<KeyValuePair<string, TaskInfo>> tasksToRun;
                    
                    lock (_lockObject)
                    {
                        tasksToRun = new List<KeyValuePair<string, TaskInfo>>(_tasks);
                    }
                    
                    foreach (var taskEntry in tasksToRun)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;
                        
                        var taskName = taskEntry.Key;
                        var taskInfo = taskEntry.Value;
                        
                        if (DateTime.Now - taskInfo.LastRunTime >= taskInfo.Interval)
                        {
                            await RunTaskAsync(taskName, taskInfo, cancellationToken);
                        }
                    }
                    
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _operationLogService.Log("Background task loop cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _operationLogService.LogException("Unexpected error in background task loop", ex);
                    await Task.Delay(5000, cancellationToken);
                }
            }
            
            _operationLogService.Log("Background task loop ended");
        }

        private async Task RunTaskAsync(string taskName, TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            
            _operationLogService.Log($"Starting background task: {taskName}");
            
            try
            {
                bool success = await taskInfo.TaskFunc();
                
                if (success)
                {
                    _operationLogService.Log($"Background task completed successfully: {taskName}");
                    taskInfo.RetryCount = 0;
                    taskInfo.ConsecutiveFailures = 0;
                }
                else
                {
                    taskInfo.ConsecutiveFailures++;
                    taskInfo.RetryCount++;
                    
                    if (taskInfo.RetryCount <= taskInfo.MaxRetries)
                    {
                        _operationLogService.Log($"Background task failed, will retry ({taskInfo.RetryCount}/{taskInfo.MaxRetries}): {taskName}", LogLevel.Warning);
                    }
                    else
                    {
                        _operationLogService.Log($"Background task failed after maximum retries ({taskInfo.MaxRetries}): {taskName}", LogLevel.Error);
                        taskInfo.RetryCount = 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _operationLogService.Log($"Background task cancelled: {taskName}", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                taskInfo.ConsecutiveFailures++;
                taskInfo.RetryCount++;
                
                if (taskInfo.RetryCount <= taskInfo.MaxRetries)
                {
                    _operationLogService.LogException($"Background task exception, will retry ({taskInfo.RetryCount}/{taskInfo.MaxRetries}): {taskName}", ex, LogLevel.Warning);
                }
                else
                {
                    _operationLogService.LogException($"Background task exception after maximum retries ({taskInfo.MaxRetries}): {taskName}", ex, LogLevel.Error);
                    taskInfo.RetryCount = 0;
                }
            }
            finally
            {
                taskInfo.LastRunTime = DateTime.Now;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
            }
        }

        private class TaskInfo
        {
            public Func<Task<bool>> TaskFunc { get; set; }
            public TimeSpan Interval { get; set; }
            public int MaxRetries { get; set; }
            public DateTime LastRunTime { get; set; }
            public int RetryCount { get; set; }
            public int ConsecutiveFailures { get; set; }
        }
    }
}
