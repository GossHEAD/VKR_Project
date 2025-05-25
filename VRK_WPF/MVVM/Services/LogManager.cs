using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;

namespace VRK_WPF.MVVM.Services
{
    public class LogManager : IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly ObservableCollection<Model.LogEntry> _logs = new();
        private string? _currentLogPath;
        private long _lastPosition = 0;
        private FileSystemWatcher? _fileWatcher;
        private string _currentNodeId = string.Empty;
        private bool _disposed = false;
        
        public ObservableCollection<Model.LogEntry> Logs => _logs;
        
        public LogManager(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Debug.WriteLine("LogManager: Initialized");
        }
        
        public void SetCurrentNodeId(string nodeId)
        {
            _currentNodeId = nodeId;
        }
        
        public async Task StartMonitoringAsync()
        {
            Debug.WriteLine("LogManager: StartMonitoringAsync called");
            
            if (string.IsNullOrEmpty(_currentLogPath))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Debug.WriteLine($"LogManager: Base directory: {baseDir}");
                
                string logsDir = FindLogsDirectory(baseDir);
                Debug.WriteLine($"LogManager: Logs directory: {logsDir}");
                
                if (!string.IsNullOrEmpty(logsDir) && Directory.Exists(logsDir))
                {
                    var logFiles = Directory.GetFiles(logsDir, "*.txt")
                        .Where(f => Path.GetFileName(f).Contains("log") || Path.GetFileName(f).Contains("node"))
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .ToList();
                    
                    Debug.WriteLine($"LogManager: Found {logFiles.Count} potential log files:");
                    foreach (var file in logFiles)
                    {
                        Debug.WriteLine($"  - {Path.GetFileName(file)}");
                    }
                        
                    if (logFiles.Any())
                    {
                        _currentLogPath = logFiles.First();
                        Debug.WriteLine($"LogManager: Selected log file: {_currentLogPath}");
                    }
                    else
                    {
                        Debug.WriteLine("LogManager: No log files found");
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(_currentLogPath) && File.Exists(_currentLogPath))
            {
                Debug.WriteLine($"LogManager: Loading logs from: {_currentLogPath}");
                await LoadInitialLogsAsync();
                
                string? directory = Path.GetDirectoryName(_currentLogPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    SetupFileWatcher(directory);
                }
            }
            else
            {
                Debug.WriteLine("LogManager: No valid log file to monitor");
            }
        }
        
        public async Task LoadLogsManuallyAsync(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            {
                Debug.WriteLine($"LogManager: Cannot load logs manually - file not found: {logFilePath}");
                return;
            }
            
            Debug.WriteLine($"LogManager: Manually loading logs from: {logFilePath}");
            
            try
            {
                string nodeId = Path.GetFileNameWithoutExtension(logFilePath).Split('-')[0];
                
                string[] allLines = await File.ReadAllLinesAsync(logFilePath);
                Debug.WriteLine($"LogManager: Read {allLines.Length} lines from file");
                
                if (allLines.Length > 0)
                {
                    int count = 0;
                    foreach (var line in allLines)
                    {
                        var logEntry = ParseLogLine(line, nodeId);
                        if (logEntry != null)
                        {
                            _dispatcher.Invoke(() => _logs.Add(logEntry));
                            count++;
                        }
                    }
                    
                    Debug.WriteLine($"LogManager: Manually loaded {count} log entries");
                }
                else
                {
                    Debug.WriteLine("LogManager: File is empty");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogManager: Error manually loading logs: {ex.Message}");
            }
        }
        
        private string FindLogsDirectory(string startDir)
        {
            string logsDir = Path.Combine(startDir, "Logs");
            
            if (Directory.Exists(logsDir))
            {
                return logsDir;
            }
                
            DirectoryInfo? dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                logsDir = Path.Combine(dir.FullName, "Logs");
                if (Directory.Exists(logsDir))
                {
                    return logsDir;
                }
                    
                dir = dir.Parent;
            }
            
            return string.Empty;
        }
        
        private async Task LoadInitialLogsAsync()
        {
            if (string.IsNullOrEmpty(_currentLogPath) || !File.Exists(_currentLogPath))
            {
                Debug.WriteLine($"LogManager: Cannot load logs - current path invalid: {_currentLogPath}");
                return;
            }
                
            try
            {
                string nodeId = ExtractNodeIdFromFileName(_currentLogPath);
                _currentNodeId = nodeId;
                
                Debug.WriteLine($"LogManager: Extracted Node ID: '{nodeId}' from file: {_currentLogPath}");
                
                var lines = await ReadLastLinesAsync(_currentLogPath, 500);
                Debug.WriteLine($"LogManager: Read {lines.Count} lines from file");
                
                int parsedCount = 0;
                foreach (var line in lines)
                {
                    var logEntry = ParseLogLine(line, nodeId);
                    if (logEntry != null)
                    {
                        _dispatcher.Invoke(() => _logs.Add(logEntry));
                        parsedCount++;
                    }
                }
                
                Debug.WriteLine($"LogManager: Successfully parsed {parsedCount} log entries");
                
                _lastPosition = new FileInfo(_currentLogPath).Length;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogManager: Error loading logs: {ex.Message}");
            }
        }
        
        private string ExtractNodeIdFromFileName(string filePath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                Debug.WriteLine($"LogManager: Extracting Node ID from filename: {fileName}");
                
                if (fileName.Contains("-log"))
                {
                    string[] parts = fileName.Split('-');
                    
                    if (parts.Length >= 2)
                    {
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "log" || parts[i].StartsWith("log"))
                            {
                                if (i > 0)
                                {
                                    string result = string.Join("-", parts.Take(i));
                                    Debug.WriteLine($"LogManager: Extracted Node ID: {result}");
                                    return result;
                                }
                                break;
                            }
                        }
                    }
                }
                
                string defaultResult = fileName.Split('-')[0];
                Debug.WriteLine($"LogManager: Defaulting to Node ID: {defaultResult}");
                return defaultResult;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogManager: Error extracting Node ID: {ex.Message}");
                return Path.GetFileNameWithoutExtension(filePath);
            }
        }
        
        private void SetupFileWatcher(string logsDirectory)
        {
            if (string.IsNullOrEmpty(logsDirectory) || !Directory.Exists(logsDirectory))
            {
                Debug.WriteLine($"LogManager: Cannot set up file watcher - directory not found: {logsDirectory}");
                return;
            }
                
            Debug.WriteLine($"LogManager: Setting up file watcher for directory: {logsDirectory}");
            
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
                    Debug.WriteLine($"LogManager: Detected change in log file: {e.FullPath}");
                    await ReadNewLogEntriesAsync();
                }
            };
            
            Debug.WriteLine("LogManager: File watcher set up successfully");
        }
        
        private async Task ReadNewLogEntriesAsync()
        {
            if (string.IsNullOrEmpty(_currentLogPath))
            {
                Debug.WriteLine("LogManager: Cannot read new entries - no current log path");
                return;
            }
                
            try
            {
                await Task.Delay(100);
                
                long currentSize = new FileInfo(_currentLogPath).Length;
                if (currentSize <= _lastPosition)
                {
                    Debug.WriteLine("LogManager: File hasn't grown since last read");
                    return;
                }
                    
                Debug.WriteLine($"LogManager: Reading new content from position {_lastPosition} to {currentSize}");
                
                using (var fs = new FileStream(_currentLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_lastPosition, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs))
                    {
                        string? line;
                        int newLines = 0;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var logEntry = ParseLogLine(line, _currentNodeId);
                            if (logEntry != null)
                            {
                                _dispatcher.Invoke(() => _logs.Add(logEntry));
                                newLines++;
                            }
                        }
                        
                        _lastPosition = fs.Position;
                        Debug.WriteLine($"LogManager: Added {newLines} new log entries");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogManager: Error reading new log entries: {ex.Message}");
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
                    
                    var nodeIdMatch = System.Text.RegularExpressions.Regex.Match(message, @"\[([^\]]+)\]");
                    if (nodeIdMatch.Success)
                    {
                        var extractedNodeId = nodeIdMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(extractedNodeId) && 
                            !extractedNodeId.Equals("Information", StringComparison.OrdinalIgnoreCase) &&
                            !extractedNodeId.Equals("Warning", StringComparison.OrdinalIgnoreCase) &&
                            !extractedNodeId.Equals("Error", StringComparison.OrdinalIgnoreCase) &&
                            !extractedNodeId.Equals("Debug", StringComparison.OrdinalIgnoreCase))
                        {
                            nodeId = extractedNodeId;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(nodeId))
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
                
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Debug.WriteLine($"LogManager: Non-standard log line: {line}");
                    return new Model.LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = "INFO",
                        Message = line,
                        NodeId = nodeId
                    };
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogManager: Error parsing log line: {ex.Message}");
                return null;
            }
        }
        
        private async Task<List<string>> ReadLastLinesAsync(string filePath, int lineCount)
        {
            var result = new List<string>();
            
            try
            {
                var allLines = await File.ReadAllLinesAsync(filePath);
                
                return allLines.Skip(Math.Max(0, allLines.Length - lineCount)).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogManager: Error reading last lines: {ex.Message}");
                return result;
            }
        }
        
        public void StopMonitoring()
        {
            Debug.WriteLine("LogManager: Stopping monitoring");
            _fileWatcher?.Dispose();
            _fileWatcher = null;
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopMonitoring();
                    _fileWatcher?.Dispose();
                    _fileWatcher = null;
                    _logs.Clear();
                }
                _disposed = true;
            }
        }
        
        public void ClearLogs()
        {
            Debug.WriteLine("LogManager: Clearing logs");
            _dispatcher.Invoke(() => _logs.Clear());
        }
        
        public async Task SwitchLogFileAsync(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            {
                Debug.WriteLine($"LogManager: Cannot switch - invalid log file path: {logFilePath}");
                return;
            }
                
            Debug.WriteLine($"LogManager: Switching to log file: {logFilePath}");
            _currentLogPath = logFilePath;
            _lastPosition = 0;
            
            _dispatcher.Invoke(() => _logs.Clear());
            
            await LoadInitialLogsAsync();
            
            string? directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                SetupFileWatcher(directory);
            }
            
            Debug.WriteLine($"LogManager: Switched to file with {_logs.Count} log entries");
        }
    }
}