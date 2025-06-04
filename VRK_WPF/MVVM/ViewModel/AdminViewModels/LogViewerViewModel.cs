using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VRK_WPF.MVVM.Model;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class LogViewerViewModel : ObservableObject, IDisposable
    {
        private readonly LogManager _logManager;
        private readonly List<LogEntry> _allLogs = new();
        private bool _disposed = false;
        
        private ICollectionViewLiveShaping? _logView;

        [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();

        [ObservableProperty]
        private ObservableCollection<string> _logLevels = new()
            { "All", "Error", "Warning", "Information", "Debug", "Trace" };

        
        [ObservableProperty] private string _selectedLogLevel = "All";

        [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-7); 

        [ObservableProperty] private string _searchText = string.Empty;

        
        [ObservableProperty] private bool _isLoading;

        [ObservableProperty] private string _statusMessage = "Готов";

        [ObservableProperty] private int _logCount;

        [ObservableProperty] private LogEntry? _selectedLog;

        
        public RelayCommand RefreshLogsCommand { get; }
        public RelayCommand ClearLogsCommand { get; }
        public RelayCommand ExportLogsCommand { get; }
        public RelayCommand ApplyFiltersCommand { get; }

        public LogViewerViewModel()
        {
            _logManager = new LogManager(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            
            Logs = _logManager.Logs;
            
            var view = CollectionViewSource.GetDefaultView(Logs);
            _logView = view as ICollectionViewLiveShaping;
            
            RefreshLogsCommand = new RelayCommand(async () => await RefreshLogsAsync(), CanRefreshLogs);
            ClearLogsCommand = new RelayCommand(ClearLogs, CanClearLogs);
            ExportLogsCommand = new RelayCommand(ExportLogs, CanExportLogs);
            ApplyFiltersCommand = new RelayCommand(ApplyFilters, CanApplyFilters);
            
            LogCount = 0;
            
            StartMonitoringLogs();
        }

        private async void StartMonitoringLogs()
        {
            IsLoading = true;
            StatusMessage = "Запуск записи журнала...";
            
            try
            {
                await _logManager.StartMonitoringAsync();
                LogCount = Logs.Count;
                StatusMessage = $"Запись журнала. {LogCount} записей сделано.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка записи журнала событий: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                RefreshLogsCommand.NotifyCanExecuteChanged();
                ClearLogsCommand.NotifyCanExecuteChanged();
                ExportLogsCommand.NotifyCanExecuteChanged();
            }
        }

        private bool CanRefreshLogs() => !IsLoading;
        private bool CanClearLogs() => !IsLoading && Logs.Count > 0;
        private bool CanExportLogs() => !IsLoading && Logs.Count > 0;
        private bool CanApplyFilters() => !IsLoading;

        private async Task RefreshLogsAsync()
        {
            IsLoading = true;
            StatusMessage = "Обновление журнала...";
            
            try
            {
                _allLogs.Clear();
                _logManager.ClearLogs();
                await _logManager.StartMonitoringAsync();
                
                LogCount = Logs.Count;
                StatusMessage = $"Журнал обновлен. {LogCount} записей создано.";
                
                ApplyFilters();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка обновления журнала: {ex.Message}";
                MessageBox.Show($"Неожиданная ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                RefreshLogsCommand.NotifyCanExecuteChanged();
                ClearLogsCommand.NotifyCanExecuteChanged();
                ExportLogsCommand.NotifyCanExecuteChanged();
            }
        }

        private void ClearLogs()
        {
            
            var result = MessageBox.Show(
                "Вы уверены, что хотите очистить журнал? Это не затронет исходный файл.",
                "Принять очистку",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            _logManager.ClearLogs();
            LogCount = 0;
            StatusMessage = "Журнал очищен";
            
            ClearLogsCommand.NotifyCanExecuteChanged();
            ExportLogsCommand.NotifyCanExecuteChanged();
        }

        private void ExportLogs()
        {
            if (Logs.Count == 0)
                return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"node_logs_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (saveDialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Экспорт...";

                try
                {
                    
                    bool isCsv = Path.GetExtension(saveDialog.FileName).ToLowerInvariant() == ".csv";

                    using (var writer = new StreamWriter(saveDialog.FileName, false, Encoding.UTF8))
                    {
                        
                        if (isCsv)
                        {
                            writer.WriteLine("Timestamp,Level,NodeId,Message");
                        }
                        else
                        {
                            writer.WriteLine("=== Log Export ===");
                            writer.WriteLine($"Date: {DateTime.Now}");
                            writer.WriteLine($"Total Entries: {Logs.Count}");
                            writer.WriteLine("==================");
                            writer.WriteLine();
                        }

                        
                        foreach (var log in Logs)
                        {
                            if (isCsv)
                            {
                                
                                string message = log.Message?.Replace("\"", "\"\"") ?? "";
                                string nodeId = log.NodeId?.Replace("\"", "\"\"") ?? "";

                                writer.WriteLine(
                                    $"\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{log.Level}\",\"{nodeId}\",\"{message}\"");
                            }
                            else
                            {
                                
                                writer.WriteLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] [{log.NodeId}] {log.Message}");
                            }
                        }
                    }

                    StatusMessage = $"Экспортированы {Logs.Count} событий в файл {saveDialog.FileName}";

                    
                    MessageBox.Show(
                        $"Успешный экспорт {Logs.Count} событий в {saveDialog.FileName}",
                        "Журнал экспортирован",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка экспорта: {ex.Message}";
                    MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка экспорта", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private void ApplyFilters()
        {
            if (Logs.Count == 0)
                return;
            
            if (_allLogs.Count == 0)
                _allLogs.AddRange(Logs);

            CollectionViewSource.GetDefaultView(Logs).Filter = log =>
            {
                if (log is LogEntry entry)
                {
                    if (SelectedLogLevel != "All")
                    {
                        string levelFilter = GetLevelFilter(SelectedLogLevel);
                        if (!entry.Level.Contains(levelFilter, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }

                    if (!string.IsNullOrWhiteSpace(SearchText) && 
                        !entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
                        !entry.NodeId.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (FromDate != DateTime.MinValue && entry.Timestamp < FromDate)
                        return false;

                    return true;
                }
                return false;
            };

            StatusMessage = $"Фильтр применен. Показано {CollectionViewSource.GetDefaultView(Logs).Cast<LogEntry>().Count()} из {Logs.Count} записей.";
        }
        
        private string GetLevelFilter(string selectedLevel)
        {
            return selectedLevel switch
            {
                "Error" => "ERR",
                "Warning" => "WARN",
                "Information" => "INFO",
                "Debug" => "DEBUG",
                "Trace" => "TRACE",
                _ => ""
            };
        }
        
        partial void OnSelectedLogLevelChanged(string value)
        {
            ApplyFilters();
        }

        partial void OnFromDateChanged(DateTime value)
        {
            ApplyFilters();
        }

        partial void OnSearchTextChanged(string value)
        {
            
            Application.Current.Dispatcher.InvokeAsync(() => ApplyFilters(), 
                System.Windows.Threading.DispatcherPriority.Background);
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
                    _logManager?.StopMonitoring();
                    _logManager?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}