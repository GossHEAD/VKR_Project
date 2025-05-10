
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;

namespace VRK_WPF.MVVM.Services
{
    public class LogManager
{
    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<Model.LogEntry> _logs = new();
    private string? _currentLogPath;
    private long _lastPosition = 0;
    private FileSystemWatcher? _fileWatcher;
    private string _currentNodeId = string.Empty;
    
    public ObservableCollection<Model.LogEntry> Logs => _logs;
    
    public LogManager(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }
    
    public void SetCurrentNodeId(string nodeId)
    {
        _currentNodeId = nodeId;
    }
    
    public async Task StartMonitoringAsync()
    {
        if (string.IsNullOrEmpty(_currentLogPath))
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logsDir = FindLogsDirectory(baseDir);
            
            if (!string.IsNullOrEmpty(logsDir) && Directory.Exists(logsDir))
            {
                var logFiles = Directory.GetFiles(logsDir, "*-log-*.txt")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();
                    
                if (logFiles.Any())
                {
                    _currentLogPath = logFiles.First();
                }
            }
        }
        
        if (!string.IsNullOrEmpty(_currentLogPath) && File.Exists(_currentLogPath))
        {
            await LoadInitialLogsAsync();
            
            string? directory = Path.GetDirectoryName(_currentLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                SetupFileWatcher(directory);
            }
        }
    }
    
    private string FindLogsDirectory(string startDir)
    {
        string logsDir = Path.Combine(startDir, "Logs");
        
        if (Directory.Exists(logsDir))
            return logsDir;
            
        DirectoryInfo? dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            logsDir = Path.Combine(dir.FullName, "Logs");
            if (Directory.Exists(logsDir))
                return logsDir;
                
            dir = dir.Parent;
        }
        
        return string.Empty;
    }
    
    private async Task LoadInitialLogsAsync()
    {
        if (string.IsNullOrEmpty(_currentLogPath) || !File.Exists(_currentLogPath))
            return;
            
        try
        {
            string nodeId = ExtractNodeIdFromFileName(_currentLogPath);
            _currentNodeId = nodeId;
            
            var lines = await ReadLastLinesAsync(_currentLogPath, 500);
            
            foreach (var line in lines)
            {
                var logEntry = ParseLogLine(line, nodeId);
                if (logEntry != null)
                {
                    _dispatcher.Invoke(() => _logs.Add(logEntry));
                }
            }
            
            _lastPosition = new FileInfo(_currentLogPath).Length;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading logs: {ex.Message}");
        }
    }
    
    
    private string ExtractNodeIdFromFileName(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string[] parts = fileName.Split('-');
        
        if (parts.Length >= 3 && parts[1] == "log")
        {
            return parts[0];
        }
        
        return string.Empty;
    }
    
    private void SetupFileWatcher(string logsDirectory)
    {
        if (string.IsNullOrEmpty(logsDirectory) || !Directory.Exists(logsDirectory))
            return;
            
        
        _fileWatcher?.Dispose();
        
        
        _fileWatcher = new FileSystemWatcher(logsDirectory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*.txt",
            EnableRaisingEvents = true
        };
        
        _fileWatcher.Changed += async (sender, e) => 
        {
            if (e.FullPath == _currentLogPath)
            {
                await ReadNewLogEntriesAsync();
            }
        };
    }
    
    private async Task ReadNewLogEntriesAsync()
    {
        if (string.IsNullOrEmpty(_currentLogPath))
            return;
            
        try
        {
            await Task.Delay(100);
            
            long currentSize = new FileInfo(_currentLogPath).Length;
            if (currentSize <= _lastPosition)
                return;
                
            using (var fs = new FileStream(_currentLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(_lastPosition, SeekOrigin.Begin);
                using (var reader = new StreamReader(fs))
                {
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var logEntry = ParseLogLine(line, _currentNodeId);
                        if (logEntry != null)
                        {
                            _dispatcher.Invoke(() => _logs.Add(logEntry));
                        }
                    }
                    
                    _lastPosition = fs.Position;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading new log entries: {ex.Message}");
        }
    }
    
    private Model.LogEntry? ParseLogLine(string line, string nodeId)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, 
                @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[([^\]]+)\] (.+)$");
                
            if (match.Success)
            {
                var timestamp = DateTime.Parse(match.Groups[1].Value);
                var level = match.Groups[2].Value;
                var message = match.Groups[3].Value;
                
                var nodeIdMatch = System.Text.RegularExpressions.Regex.Match(message, @"NodeId: (\w+)");
                if (nodeIdMatch.Success)
                {
                    nodeId = nodeIdMatch.Groups[1].Value;
                }
                else if (string.IsNullOrEmpty(nodeId))
                {
                    nodeId = Path.GetFileNameWithoutExtension(_currentLogPath)?.Split('-')[0] ?? "Unknown";
                }
                
                return new Model.LogEntry
                {
                    Timestamp = timestamp,
                    Level = level,
                    Message = message,
                    NodeId = nodeId
                };
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    private async Task<List<string>> ReadLastLinesAsync(string filePath, int lineCount)
    {
        var result = new List<string>();
        
        await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs))
        {
            var allLines = new List<string>();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                allLines.Add(line);
            }
            
            return allLines.Skip(Math.Max(0, allLines.Count - lineCount)).ToList();
        }
    }
    
    public void StopMonitoring()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
    }
    
    public void ClearLogs()
    {
        _dispatcher.Invoke(() => _logs.Clear());
    }
    
    public async Task SwitchLogFileAsync(string logFilePath)
    {
        if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            return;
            
        _currentLogPath = logFilePath;
        _lastPosition = 0;
        _dispatcher.Invoke(() => _logs.Clear());
        await LoadInitialLogsAsync();
        
        
        string? directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            SetupFileWatcher(directory);
        }
    }
}
}