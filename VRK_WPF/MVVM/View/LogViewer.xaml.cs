using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using VRK_WPF.MVVM.Model;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.View
{
    public partial class LogViewer : UserControl
    {
        private readonly LogManager _logManager;
        private readonly CollectionViewSource _viewSource;
        private bool _autoScroll = true;

        public LogViewer()
        {
            InitializeComponent();
            
            // Initialize after UI is ready
            _logManager = new LogManager(Dispatcher);
            _viewSource = new CollectionViewSource { Source = _logManager.Logs };
            
            lvLogs.ItemsSource = _viewSource.View;
            DataContext = this;

            this.Loaded += LogViewer_Loaded;
            this.Unloaded += LogViewer_Unloaded;

            // Set up the collection changed event to handle auto-scrolling
            ((ICollectionView)_viewSource.View).CollectionChanged += (s, e) =>
            {
                if (_autoScroll && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    if (lvLogs.Items.Count > 0)
                    {
                        lvLogs.ScrollIntoView(lvLogs.Items[lvLogs.Items.Count - 1]);
                    }
                }
            };
        }

        private async void LogViewer_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("LogViewer: Loaded event fired");
            InitLogsDirectory();
            PopulateNodeLogsDropdown();
            ApplyFilters();
            
            // Force an immediate refresh
            await RefreshLogsAsync();
        }

        private void LogViewer_Unloaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("LogViewer: Unloaded event fired");
            _logManager.StopMonitoring();
        }

        private void InitLogsDirectory()
        {
            try
            {
                // Create Logs directory in application folder if it doesn't exist
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logsDir = Path.Combine(baseDir, "Logs");
                
                Debug.WriteLine($"LogViewer: Base directory: {baseDir}");
                Debug.WriteLine($"LogViewer: Looking for logs in: {logsDir}");
                
                if (!Directory.Exists(logsDir))
                {
                    Debug.WriteLine($"LogViewer: Logs directory not found at {logsDir}, searching parent directories");
                    logsDir = FindLogsDirectory(baseDir);
                }
                
                if (Directory.Exists(logsDir))
                {
                    Debug.WriteLine($"LogViewer: Found logs directory: {logsDir}");
                    var logFiles = Directory.GetFiles(logsDir, "*.txt");
                    Debug.WriteLine($"LogViewer: Found {logFiles.Length} files with .txt extension");
                    foreach (var file in logFiles)
                    {
                        Debug.WriteLine($"LogViewer: Log file found: {Path.GetFileName(file)}");
                    }
                }
                else
                {
                    Debug.WriteLine("LogViewer: No logs directory found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogViewer: Error initializing logs directory: {ex.Message}");
            }
        }

        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Disable auto-scroll when user scrolls up
            if (e.VerticalChange < 0)
            {
                _autoScroll = false;
            }
            
            // Re-enable auto-scroll when user scrolls to bottom
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1)
            {
                _autoScroll = true;
            }
        }
        
        private void FilterLogs(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }
        
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }
        
        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                txtSearch.Clear();
            }
        }
        
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshLogsAsync();
        }

        private async Task RefreshLogsAsync()
        {
            btnRefresh.IsEnabled = false;
            btnRefresh.Content = "Загрузка...";
            
            try
            {
                Debug.WriteLine("LogViewer: Refreshing logs");
                
                // Re-scan logs directory
                InitLogsDirectory();
                
                // Re-populate dropdown
                PopulateNodeLogsDropdown();
                
                // Clear existing logs
                _logManager.ClearLogs();
                
                // Start monitoring the selected log file
                if (cbNodeLogs.SelectedItem is NodeLogFile selectedLog)
                {
                    Debug.WriteLine($"LogViewer: Selected log file: {selectedLog.FilePath}");
                    await _logManager.SwitchLogFileAsync(selectedLog.FilePath);
                    
                    // Try loading logs manually if automatic loading failed
                    if (_logManager.Logs.Count == 0)
                    {
                        Debug.WriteLine("LogViewer: Automatic loading failed, trying manual load");
                        await _logManager.LoadLogsManuallyAsync(selectedLog.FilePath);
                    }
                }
                else
                {
                    // If no log is selected, try to load all logs
                    Debug.WriteLine("LogViewer: No log file selected, trying to load any available log");
                    await _logManager.StartMonitoringAsync();
                }
                
                Debug.WriteLine($"LogViewer: Loaded {_logManager.Logs.Count} log entries");
                
                // Apply filters after loading
                ApplyFilters();
                
                // Display log count
                if (_logManager.Logs.Count > 0)
                {
                    btnRefresh.Content = $"Обновить ({_logManager.Logs.Count})";
                }
                else
                {
                    btnRefresh.Content = "Обновить (нет логов)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogViewer: Error refreshing logs: {ex.Message}");
                MessageBox.Show($"Ошибка при обновлении логов: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                btnRefresh.Content = "Ошибка";
            }
            finally
            {
                // Re-enable button after a delay
                await Task.Delay(500);
                btnRefresh.IsEnabled = true;
                
                // Reset button text if needed
                if (btnRefresh.Content.ToString() == "Загрузка...")
                {
                    btnRefresh.Content = "Обновить";
                }
            }
        }
        
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _logManager.ClearLogs();
        }
        
        private string FindLogsDirectory(string startDir)
        {
            // Look for Logs directory in current and parent directories
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
        
        private string GetNodeIdFromFilename(string filename)
        {
            try
            {
                // Try to extract node ID from various log filename formats
                string name = Path.GetFileNameWithoutExtension(filename);
                
                // Pattern: {nodeId}-log-.txt, {nodeId}-log.txt, etc.
                if (name.Contains("-log"))
                {
                    string[] parts = name.Split('-');
                    if (parts.Length >= 2)
                    {
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (parts[i].StartsWith("log"))
                            {
                                // Everything before "log" is the node ID
                                return string.Join("-", parts.Take(i));
                            }
                        }
                    }
                }
                
                // Default: return filename without extension
                return name;
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(filename);
            }
        }
        
        private void PopulateNodeLogsDropdown()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logsDir = FindLogsDirectory(baseDir);
                
                if (string.IsNullOrEmpty(logsDir) || !Directory.Exists(logsDir))
                {
                    Debug.WriteLine("LogViewer: Logs directory not found");
                    return;
                }
                
                // Print all files in the directory for debugging
                var allFiles = Directory.GetFiles(logsDir);
                Debug.WriteLine($"LogViewer: All files in logs directory ({allFiles.Length}):");
                foreach (var file in allFiles)
                {
                    Debug.WriteLine($"  - {Path.GetFileName(file)}");
                }
                
                // Look for any log files (more permissive pattern)
                var logFiles = Directory.GetFiles(logsDir, "*.txt")
                    .Where(f => Path.GetFileName(f).Contains("log") || Path.GetFileName(f).Contains("node"))
                    .Select(f => new NodeLogFile
                    {
                        FilePath = f,
                        NodeId = GetNodeIdFromFilename(f),
                        LastWriteTime = File.GetLastWriteTime(f)
                    })
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();
                
                Debug.WriteLine($"LogViewer: Found {logFiles.Count} potential log files:");
                foreach (var logFile in logFiles)
                {
                    Debug.WriteLine($"  - {Path.GetFileName(logFile.FilePath)}, Node ID: {logFile.NodeId}");
                }
                
                // Remember current selection
                NodeLogFile? currentSelection = cbNodeLogs.SelectedItem as NodeLogFile;
                
                // Set new items
                cbNodeLogs.ItemsSource = logFiles;
                
                // Restore selection or select first item
                if (currentSelection != null)
                {
                    var matchingFile = logFiles.FirstOrDefault(f => 
                        f.FilePath == currentSelection.FilePath);
                    
                    if (matchingFile != null)
                    {
                        cbNodeLogs.SelectedItem = matchingFile;
                    }
                    else if (logFiles.Any())
                    {
                        cbNodeLogs.SelectedIndex = 0;
                    }
                }
                else if (logFiles.Any())
                {
                    cbNodeLogs.SelectedIndex = 0;
                }
                
                // Ensure event handler is connected
                cbNodeLogs.SelectionChanged -= CbNodeLogs_SelectionChanged;
                cbNodeLogs.SelectionChanged += CbNodeLogs_SelectionChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogViewer: Error populating dropdown: {ex.Message}");
            }
        }

        private async void CbNodeLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbNodeLogs.SelectedItem is NodeLogFile selectedLog)
            {
                Debug.WriteLine($"LogViewer: Selected log file changed to: {selectedLog.FilePath}");
                
                // Clear logs and switch to the new file
                _logManager.ClearLogs();
                await _logManager.SwitchLogFileAsync(selectedLog.FilePath);
                
                // Try manual loading if automatic loading failed
                if (_logManager.Logs.Count == 0)
                {
                    Debug.WriteLine("LogViewer: Automatic loading failed, trying manual load on selection change");
                    await _logManager.LoadLogsManuallyAsync(selectedLog.FilePath);
                }
                
                Debug.WriteLine($"LogViewer: After selection change, loaded {_logManager.Logs.Count} log entries");
                ApplyFilters();
            }
        }
        
        private void ApplyFilters()
        {
            if (_viewSource?.View == null)
            {
                Debug.WriteLine("LogViewer: Cannot apply filters - view source is null");
                return;
            }

            string searchText = txtSearch?.Text ?? string.Empty;
    
            bool includeInfo = cbInfo?.IsChecked == true;
            bool includeWarning = cbWarning?.IsChecked == true;
            bool includeError = cbError?.IsChecked == true;
            bool includeDebug = cbDebug?.IsChecked == true;
    
            _viewSource.View.Filter = item =>
            {
                if (item is LogEntry log)
                {
                    bool matchesSearch = string.IsNullOrWhiteSpace(searchText) || 
                            log.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) || 
                            log.FullText.Contains(searchText, StringComparison.OrdinalIgnoreCase);
                            
                    bool matchesLevel = (includeInfo && log.IsInfo) ||
                            (includeWarning && log.IsWarning) ||
                            (includeError && log.IsError) ||
                            (includeDebug && log.IsDebug);
                            
                    return matchesSearch && matchesLevel;
                }
        
                return false;
            };
            
            Debug.WriteLine($"LogViewer: Filters applied - found {lvLogs.Items.Count} matching entries");
        }
    }
}