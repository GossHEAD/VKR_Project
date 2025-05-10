
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using VRK_WPF.MVVM.Model;

namespace VRK_WPF.MVVM.Services
{
    public class LogManager
    {
        private static readonly Regex LogRegex = new Regex(
            @"^(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}\.\d{3})\s\[(\w+)\]\s(.*?)(?:\r\n|\n|$)",
            RegexOptions.Compiled | RegexOptions.Multiline);
            
        private readonly ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();
        private readonly Dispatcher _dispatcher;
        private readonly string _logsDirectory;
        private FileSystemWatcher _fileWatcher;
        private readonly Dictionary<string, long> _filePositions = new Dictionary<string, long>();
        private CancellationTokenSource _cts;
        
        public ObservableCollection<LogEntry> Logs => _logs;
        
        public LogManager(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            
            // Determine log directory path - same as in Node application
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VKR_Node");
                
            _logsDirectory = Path.Combine(appDataPath, "Logs");
            
            // Default to a relative Logs directory if the AppData one doesn't exist
            if (!Directory.Exists(_logsDirectory))
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logsDirectory = Path.Combine(baseDir, "..", "..", "VKR_Node", "Logs");
            }
            
            // Try to find logs in the executable directory if all else fails
            if (!Directory.Exists(_logsDirectory))
            {
                _logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            }
            
            // Create if it doesn't exist
            Directory.CreateDirectory(_logsDirectory);
        }
        
        public async Task StartMonitoringAsync()
        {
            StopMonitoring();
            _cts = new CancellationTokenSource();
            await LoadInitialLogsAsync(_cts.Token);
            SetupFileWatcher();
        }
        
        public void StopMonitoring()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= OnLogFileChanged;
                _fileWatcher.Created -= OnLogFileCreated;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }
        
        private void SetupFileWatcher()
        {
            _fileWatcher = new FileSystemWatcher(_logsDirectory)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                Filter = "*.txt",
                EnableRaisingEvents = true
            };
            
            _fileWatcher.Changed += OnLogFileChanged;
            _fileWatcher.Created += OnLogFileCreated;
        }
        
        private async void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            await ReadLogFileChangesAsync(e.FullPath, _cts?.Token ?? CancellationToken.None);
        }
        
        private async void OnLogFileCreated(object sender, FileSystemEventArgs e)
        {
            _filePositions[e.FullPath] = 0;
            await ReadLogFileChangesAsync(e.FullPath, _cts?.Token ?? CancellationToken.None);
        }
        
        private async Task LoadInitialLogsAsync(CancellationToken cancellationToken)
        {
            try
            {
                _dispatcher.Invoke(() => _logs.Clear());
                _filePositions.Clear();
                
                var logFiles = Directory.GetFiles(_logsDirectory, "*.txt")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();
                
                if (!logFiles.Any())
                {
                    return;
                }
                
                string mostRecentFile = logFiles.First();
                await ReadLogFileChangesAsync(mostRecentFile, cancellationToken, initialLoad: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading initial logs: {ex.Message}");
            }
        }
        
        private async Task ReadLogFileChangesAsync(string filePath, CancellationToken cancellationToken, bool initialLoad = false)
        {
            try
            {
                await Task.Delay(100, cancellationToken);
                
                if (!_filePositions.TryGetValue(filePath, out long position))
                {
                    position = 0;
                }
                
                using (var fileStream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (initialLoad && fileStream.Length > 50000) // ~50KB
                    {
                        position = Math.Max(0, fileStream.Length - 50000);
                    }
                    
                    if (position > fileStream.Length)
                    {
                        position = 0;
                    }
                    
                    if (fileStream.Length > position)
                    {
                        fileStream.Seek(position, SeekOrigin.Begin);
                        
                        using (var reader = new StreamReader(fileStream))
                        {
                            string newContent = await reader.ReadToEndAsync();
                            ParseAndAddLogEntries(newContent, Path.GetFileNameWithoutExtension(filePath));
                            
                            _filePositions[filePath] = fileStream.Position;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading log file {filePath}: {ex.Message}");
            }
        }
        
        private void ParseAndAddLogEntries(string content, string sourceFile)
        {
            var matches = LogRegex.Matches(content);
            var newEntries = new List<LogEntry>();
            
            foreach (Match match in matches)
            {
                if (match.Groups.Count >= 4)
                {
                    var entry = new LogEntry
                    {
                        Timestamp = DateTime.Parse(match.Groups[1].Value),
                        Level = match.Groups[2].Value,
                        Message = match.Groups[3].Value,
                        NodeId = ExtractNodeId(sourceFile),
                        FullText = match.Value
                    };
                    
                    newEntries.Add(entry);
                }
            }
            
            if (newEntries.Any())
            {
                _dispatcher.Invoke(() =>
                {
                    foreach (var entry in newEntries)
                    {
                        _logs.Add(entry);
                    }
                    
                    while (_logs.Count > 5000)
                    {
                        _logs.RemoveAt(0);
                    }
                });
            }
        }
        
        private string ExtractNodeId(string fileName)
        {
            // Attempt to extract node ID from filename (e.g., "node1-log-20220101.txt")
            int dashIndex = fileName.IndexOf('-');
            if (dashIndex > 0)
            {
                return fileName.Substring(0, dashIndex);
            }
            
            return fileName;
        }
        
        public void ClearLogs()
        {
            _dispatcher.Invoke(() => _logs.Clear());
        }
        
        public IEnumerable<LogEntry> FilterLogs(
            string searchText = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            bool includeInfo = true,
            bool includeWarning = true,
            bool includeError = true,
            bool includeDebug = false)
        {
            return _logs.Where(log =>
                (string.IsNullOrWhiteSpace(searchText) || 
                 log.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                 log.FullText.Contains(searchText, StringComparison.OrdinalIgnoreCase)) &&
                (!startDate.HasValue || log.Timestamp >= startDate.Value) &&
                (!endDate.HasValue || log.Timestamp <= endDate.Value) &&
                ((includeInfo && log.IsInfo) ||
                 (includeWarning && log.IsWarning) ||
                 (includeError && log.IsError) ||
                 (includeDebug && log.IsDebug)));
        }
    }
}