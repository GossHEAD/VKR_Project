using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using VKR.Protos;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class LogViewerViewModel : ObservableObject
{
    /*
    private readonly ILogger<LogViewerViewModel>? _logger;
    private StorageService.StorageServiceClient? _storageClient;
    private readonly List<LogEntryViewModel> _allLogs = new();

    // Observable collections
    [ObservableProperty] private ObservableCollection<LogEntryViewModel> _logs = new();

    [ObservableProperty]
    private ObservableCollection<string> _logLevels = new()
        { "All", "Error", "Warning", "Information", "Debug", "Trace" };

    // Filter properties
    [ObservableProperty] private string _selectedLogLevel = "All";

    [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-7); // Default to last 7 days

    [ObservableProperty] private string _searchText = string.Empty;

    // UI state
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _statusMessage = "Ready";

    [ObservableProperty] private int _logCount;

    [ObservableProperty] private LogEntryViewModel? _selectedLog;

    // Commands
    public RelayCommand RefreshLogsCommand { get; }
    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand ExportLogsCommand { get; }
    public RelayCommand ApplyFiltersCommand { get; }

    public LogViewerViewModel(ILogger<LogViewerViewModel>? logger = null,
        StorageService.StorageServiceClient? client = null)
    {
        _logger = logger;
        _storageClient = client;

        // Initialize commands
        RefreshLogsCommand = new RelayCommand(async () => await RefreshLogsAsync(), CanRefreshLogs);
        ClearLogsCommand = new RelayCommand(async () => await ClearLogsAsync(), CanClearLogs);
        ExportLogsCommand = new RelayCommand(ExportLogs, CanExportLogs);
        ApplyFiltersCommand = new RelayCommand(ApplyFilters, CanApplyFilters);

        // Set up view
        LogCount = 0;
    }

    public void SetClient(StorageService.StorageServiceClient client)
    {
        _storageClient = client;
        RefreshLogsCommand.NotifyCanExecuteChanged();
        ClearLogsCommand.NotifyCanExecuteChanged();
        ExportLogsCommand.NotifyCanExecuteChanged();

        // Load logs immediately
        RefreshLogsAsync().ConfigureAwait(false);
    }

    private bool CanRefreshLogs() => !IsLoading && _storageClient != null;
    private bool CanClearLogs() => !IsLoading && _storageClient != null && Logs.Count > 0;
    private bool CanExportLogs() => !IsLoading && Logs.Count > 0;
    private bool CanApplyFilters() => !IsLoading;

    private async Task RefreshLogsAsync()
    {
        if (_storageClient == null)
            return;

        IsLoading = true;
        StatusMessage = "Loading logs...";

        try
        {
            var request = new GetNodeLogsRequest
            {
                // Default to last 7 days if no date filter applied
                FromTimestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(FromDate.ToUniversalTime()),
                MaxEntries = 1000 // Limit to avoid overwhelming the UI
            };

            var reply = await _storageClient.GetNodeLogsAsync(request, deadline: DateTime.UtcNow.AddSeconds(30));

            // Clear existing logs
            _allLogs.Clear();
            Logs.Clear();

            // Process log entries
            if (reply.Entries != null)
            {
                foreach (var entry in reply.Entries)
                {
                    var logEntry = new LogEntryViewModel
                    {
                        Id = entry.Id,
                        Timestamp = entry.Timestamp?.ToDateTime() ?? DateTime.MinValue,
                        Level = entry.Level,
                        Message = entry.Message,
                        ExceptionDetails = entry.ExceptionDetails,
                        HasException = !string.IsNullOrEmpty(entry.ExceptionDetails)
                    };

                    _allLogs.Add(logEntry);
                }

                // Apply filters
                ApplyFilters();

                LogCount = _allLogs.Count;
                StatusMessage = $"Loaded {LogCount} log entries";
            }
            else
            {
                LogCount = 0;
                StatusMessage = "No log entries found";
            }
        }
        catch (RpcException ex)
        {
            StatusMessage = $"gRPC Error: {ex.Status.Detail}";
            MessageBox.Show($"Communication error: {ex.Status.Detail}", "gRPC Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            RefreshLogsCommand.NotifyCanExecuteChanged();
            ClearLogsCommand.NotifyCanExecuteChanged();
            ExportLogsCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ClearLogsAsync()
    {
        if (_storageClient == null)
            return;

        // Confirm action
        var result = MessageBox.Show(
            "Are you sure you want to clear all logs? This action cannot be undone.",
            "Confirm Clear Logs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        IsLoading = true;
        StatusMessage = "Clearing logs...";

        try
        {
            var request = new ClearNodeLogsRequest();
            var reply = await _storageClient.ClearNodeLogsAsync(request, deadline: DateTime.UtcNow.AddSeconds(30));

            if (reply.Success)
            {
                _allLogs.Clear();
                Logs.Clear();
                LogCount = 0;
                StatusMessage = "Logs cleared successfully";

                MessageBox.Show(
                    $"Successfully cleared {reply.EntriesRemoved} log entries.",
                    "Logs Cleared",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                StatusMessage = $"Failed to clear logs: {reply.ErrorMessage}";
                MessageBox.Show($"Error clearing logs: {reply.ErrorMessage}", "Clear Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (RpcException ex)
        {
            StatusMessage = $"gRPC Error: {ex.Status.Detail}";
            MessageBox.Show($"Communication error: {ex.Status.Detail}", "gRPC Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"Unexpected error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            RefreshLogsCommand.NotifyCanExecuteChanged();
            ClearLogsCommand.NotifyCanExecuteChanged();
            ExportLogsCommand.NotifyCanExecuteChanged();
        }
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
            StatusMessage = "Exporting logs...";

            try
            {
                // Determine if CSV or TXT based on extension
                bool isCsv = Path.GetExtension(saveDialog.FileName).ToLowerInvariant() == ".csv";

                using (var writer = new StreamWriter(saveDialog.FileName, false, Encoding.UTF8))
                {
                    // Write header
                    if (isCsv)
                    {
                        writer.WriteLine("Id,Timestamp,Level,Message,HasException,ExceptionDetails");
                    }
                    else
                    {
                        writer.WriteLine("=== Log Export ===");
                        writer.WriteLine($"Date: {DateTime.Now}");
                        writer.WriteLine($"Total Entries: {Logs.Count}");
                        writer.WriteLine("==================");
                        writer.WriteLine();
                    }

                    // Write log entries
                    foreach (var log in Logs)
                    {
                        if (isCsv)
                        {
                            // CSV format: escape quotes in strings and wrap in quotes
                            string message = log.Message?.Replace("\"", "\"\"") ?? "";
                            string exception = log.ExceptionDetails?.Replace("\"", "\"\"") ?? "";

                            writer.WriteLine(
                                $"{log.Id},\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",{log.Level},\"{message}\",{log.HasException},\"{exception}\"");
                        }
                        else
                        {
                            // Text format: more readable
                            writer.WriteLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] {log.Level}: {log.Message}");
                            if (log.HasException)
                            {
                                writer.WriteLine("Exception Details:");
                                writer.WriteLine(log.ExceptionDetails);
                                writer.WriteLine();
                            }
                        }
                    }
                }

                StatusMessage = $"Exported {Logs.Count} logs to {saveDialog.FileName}";

                // Notify user of successful export
                MessageBox.Show(
                    $"Successfully exported {Logs.Count} log entries to {saveDialog.FileName}",
                    "Logs Exported",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error exporting logs: {ex.Message}";
                MessageBox.Show($"Error exporting logs: {ex.Message}", "Export Error", MessageBoxButton.OK,
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
        if (_allLogs == null)
            return;

        // Start with all logs
        var filteredLogs = _allLogs.AsEnumerable();

        // Apply log level filter
        if (SelectedLogLevel != "All")
        {
            filteredLogs = filteredLogs.Where(log => log.Level == SelectedLogLevel);
        }

        // Apply search text filter (if any)
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string searchLower = SearchText.ToLowerInvariant();
            filteredLogs = filteredLogs.Where(log =>
                (log.Message?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                (log.ExceptionDetails?.ToLowerInvariant().Contains(searchLower) ?? false));
        }

        // Apply from date filter
        if (FromDate != DateTime.MinValue)
        {
            filteredLogs = filteredLogs.Where(log => log.Timestamp >= FromDate);
        }

        // Update the observable collection
        Logs.Clear();
        foreach (var log in filteredLogs.OrderByDescending(l => l.Timestamp))
        {
            Logs.Add(log);
        }

        // Update status message
        StatusMessage = $"Showing {Logs.Count} of {_allLogs.Count} log entries";
    }

// Add property changed handlers to automatically apply filters when filter properties change
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
        // Optional: Add debounce logic here for better performance with typing
        ApplyFilters();
    }
    */
}