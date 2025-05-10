using System.ComponentModel;
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
            _logManager = new LogManager(Dispatcher);
            _viewSource = new CollectionViewSource { Source = _logManager.Logs };
    
            InitializeComponent();
            DataContext = this;
            lvLogs.ItemsSource = _viewSource.View;
    
            this.Loaded += async (s, e) => 
            {
                // Node selector is always visible in this version
                PopulateNodeLogsDropdown();
                ApplyFilters();
                await StartMonitoring();
            };
    
            ((ICollectionView)_viewSource.View).CollectionChanged += (s, e) =>
            {
                if (_autoScroll && e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    lvLogs.ScrollIntoView(lvLogs.Items[lvLogs.Items.Count - 1]);
                }
            };

            Unloaded += (s, e) => 
            {
                StopMonitoring();
            };
        }
        
        private async Task StartMonitoring()
        {
            await _logManager.StartMonitoringAsync();
        }
        
        private void StopMonitoring()
        {
            _logManager.StopMonitoring();
        }
        
        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange < 0)
            {
                _autoScroll = false;
            }
            
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1)
            {
                _autoScroll = true;
            }
        }
        
        private void FilterLogs(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                ApplyFilters();
            }
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
        
        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            StartMonitoring();
        }
        
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _logManager.ClearLogs();
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
        
        private string DetermineNodeIdFromLogFile(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string[] parts = fileName.Split('-');
        
            if (parts.Length >= 3 && parts[1] == "log")
            {
                return parts[0];
            }
        
            try
            {
                using var reader = new StreamReader(filePath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite
                });
            
                for (int i = 0; i < 20 && !reader.EndOfStream; i++)
                {
                    string? line = reader.ReadLine();
                    if (line == null) continue;
                
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"NodeId: (\w+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // Ignore file reading errors
            }
        
            return Path.GetFileNameWithoutExtension(filePath);
        }
        
        private void PopulateNodeLogsDropdown()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logsDir = FindLogsDirectory(baseDir);
        
            if (string.IsNullOrEmpty(logsDir) || !Directory.Exists(logsDir))
                return;
            
            var logFiles = Directory.GetFiles(logsDir, "*-log-*.txt")
                .Select(f => new NodeLogFile
                {
                    FilePath = f,
                    NodeId = DetermineNodeIdFromLogFile(f),
                    LastWriteTime = File.GetLastWriteTime(f)
                })
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();
            
            cbNodeLogs.ItemsSource = logFiles;
            if (logFiles.Any())
            {
                cbNodeLogs.SelectedIndex = 0;
            }
        }

        private async void CbNodeLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbNodeLogs.SelectedItem is NodeLogFile selectedLog)
            {
                await _logManager.SwitchLogFileAsync(selectedLog.FilePath);
            }
        }
        
        private void ApplyFilters()
        {
            if (_viewSource == null || _viewSource.View == null)
            {
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
                    return (string.IsNullOrWhiteSpace(searchText) || 
                            log.Message.Contains(searchText, StringComparison.OrdinalIgnoreCase) || 
                            log.FullText.Contains(searchText, StringComparison.OrdinalIgnoreCase)) &&
                           ((includeInfo && log.IsInfo) ||
                            (includeWarning && log.IsWarning) ||
                            (includeError && log.IsError) ||
                            (includeDebug && log.IsDebug));
                }
        
                return false;
            };
        }
    }
}