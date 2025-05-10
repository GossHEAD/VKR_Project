using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using VKR.Protos;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class NodeConfigViewModel : ObservableObject
{
    /*
    private readonly ILogger<NodeConfigViewModel>? _logger;
    private StorageService.StorageServiceClient? _storageClient;

    // Node Identity
    [ObservableProperty] private string _nodeId = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private DateTime _creationTime = DateTime.Now;

    // Network Settings
    [ObservableProperty] private string _listenAddress = "0.0.0.0";
    [ObservableProperty] private int _listenPort = 5000;
    [ObservableProperty] private int _maxConnections = 100;
    [ObservableProperty] private int _connectionTimeout = 30;

    // Storage Settings
    [ObservableProperty] private string _storageBasePath = "ChunkData";
    [ObservableProperty] private int _maxSizeValue = 10;
    [ObservableProperty] private string _maxSizeUnit = "GB";
    [ObservableProperty] private int _chunkSizeValue = 1;
    [ObservableProperty] private string _chunkSizeUnit = "MB";
    [ObservableProperty] private bool _useHashBasedDirectories = true;
    [ObservableProperty] private int _hashDirectoryDepth = 2;

    // DHT Settings
    [ObservableProperty] private string _bootstrapNodeAddress = string.Empty;
    [ObservableProperty] private int _replicationFactor = 3;
    [ObservableProperty] private int _stabilizationInterval = 30;
    [ObservableProperty] private int _replicationCheckInterval = 60;
    [ObservableProperty] private int _replicationMaxParallelism = 10;

    // Database Settings
    [ObservableProperty] private string _databasePath = "Data/node_storage.db";
    [ObservableProperty] private bool _autoMigrate = true;
    [ObservableProperty] private bool _backupBeforeMigration = true;
    [ObservableProperty] private bool _enableSqlLogging = false;

    // UI Properties
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadingMessage = "Loading configuration...";
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _hasChanges;

    // Dropdown options
    public ObservableCollection<string> SizeUnits { get; } =
        new ObservableCollection<string> { "B", "KB", "MB", "GB", "TB" };

    public ObservableCollection<int> HashDepthOptions { get; } = new ObservableCollection<int> { 1, 2, 3 };

    // Commands
    public RelayCommand RefreshConfigCommand { get; }
    public RelayCommand ApplyConfigCommand { get; }

    public NodeConfigViewModel(ILogger<NodeConfigViewModel>? logger = null,
        StorageService.StorageServiceClient? client = null)
    {
        _logger = logger;
        _storageClient = client;

        // Initialize commands
        RefreshConfigCommand = new RelayCommand(async () => await RefreshConfigAsync(), CanRefreshConfig);
        ApplyConfigCommand = new RelayCommand(async () => await ApplyConfigAsync(), CanApplyConfig);

        // Register property change handlers to detect modifications
        PropertyChanged += (s, e) =>
        {
            // Skip UI-only properties
            if (e.PropertyName != nameof(IsLoading) &&
                e.PropertyName != nameof(LoadingMessage) &&
                e.PropertyName != nameof(StatusMessage) &&
                e.PropertyName != nameof(HasChanges))
            {
                HasChanges = true;
                ApplyConfigCommand.NotifyCanExecuteChanged();
            }
        };
    }

    public void SetClient(StorageService.StorageServiceClient client)
    {
        _storageClient = client;
        RefreshConfigCommand.NotifyCanExecuteChanged();
        ApplyConfigCommand.NotifyCanExecuteChanged();

        // Load config when client is set
        RefreshConfigAsync().ConfigureAwait(false);
    }

    private bool CanRefreshConfig() => !IsLoading && _storageClient != null;
    private bool CanApplyConfig() => HasChanges && !IsLoading && _storageClient != null;

    private async Task RefreshConfigAsync()
    {
        if (_storageClient == null)
        {
            StatusMessage = "Client not initialized";
            return;
        }

        IsLoading = true;
        LoadingMessage = "Loading node configuration...";
        StatusMessage = "Retrieving configuration...";

        try
        {
            var request = new GetNodeConfigurationRequest();
            var reply = await _storageClient.GetNodeConfigurationAsync(request,
                deadline: DateTime.UtcNow.AddSeconds(15));

            if (reply.Success)
            {
                // Node Identity
                NodeId = reply.NodeId;

                // Network Settings
                ListenAddress = reply.ListenAddress;
                //ConnectionTimeout = reply.ConnectionTimeoutSeconds;

                // Storage Settings
                StorageBasePath = reply.StorageBasePath;
                ParseSizeValue(reply.MaxSizeBytes, out int sizeValue, out string sizeUnit);
                MaxSizeValue = sizeValue;
                MaxSizeUnit = sizeUnit;
                ParseSizeValue(reply.DefaultChunkSize, out int chunkValue, out string chunkUnit);
                ChunkSizeValue = chunkValue;
                ChunkSizeUnit = chunkUnit;
                UseHashBasedDirectories = reply.UseHashBasedDirectories;
                HashDirectoryDepth = reply.HashDirectoryDepth;
                
                // Database Settings
                DatabasePath = reply.DatabasePath;
                AutoMigrate = reply.AutoMigrate;
                BackupBeforeMigration = reply.BackupBeforeMigration;
                EnableSqlLogging = reply.EnableSqlLogging;

                HasChanges = false;
                StatusMessage = "Configuration loaded successfully";
            }
            else
            {
                StatusMessage = $"Failed to load configuration: {reply.ErrorMessage}";
                MessageBox.Show($"Error loading configuration: {reply.ErrorMessage}", "Configuration Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            RefreshConfigCommand.NotifyCanExecuteChanged();
            ApplyConfigCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ApplyConfigAsync()
    {
        if (_storageClient == null || !HasChanges)
            return;

        // Confirm changes
        var result = MessageBox.Show(
            "Are you sure you want to apply these configuration changes?\nThe node may need to be restarted for some changes to take effect.",
            "Confirm Configuration Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        IsLoading = true;
        LoadingMessage = "Applying configuration changes...";
        StatusMessage = "Saving configuration...";

        try
        {
            // Create request
            var request = new UpdateNodeConfigurationRequest
            {
                // Node Identity
                NodeId = NodeId,
                DisplayName = DisplayName,

                // Network Settings
                ListenAddress = ListenAddress,
                ListenPort = ListenPort,
                MaxConnections = MaxConnections,
                ConnectionTimeoutSeconds = ConnectionTimeout,

                // Storage Settings
                StorageBasePath = StorageBasePath,
                MaxSizeBytes = ConvertToBytes(MaxSizeValue, MaxSizeUnit),
                DefaultChunkSize = ConvertToBytes(ChunkSizeValue, ChunkSizeUnit),
                UseHashBasedDirectories = UseHashBasedDirectories,
                HashDirectoryDepth = HashDirectoryDepth,

                // Database Settings
                DatabasePath = DatabasePath,
                AutoMigrate = AutoMigrate,
                BackupBeforeMigration = BackupBeforeMigration,
                EnableSqlLogging = EnableSqlLogging
            };

            var reply = await _storageClient.UpdateNodeConfigurationAsync(request,
                deadline: DateTime.UtcNow.AddSeconds(30));

            if (reply.Success)
            {
                HasChanges = false;
                StatusMessage = "Configuration updated successfully";

                if (reply.RequiresRestart)
                {
                    MessageBox.Show(
                        "Configuration has been updated successfully.\nA node restart is required for some changes to take effect.",
                        "Configuration Updated",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Configuration has been updated successfully.",
                        "Configuration Updated",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                StatusMessage = $"Failed to update configuration: {reply.ErrorMessage}";
                MessageBox.Show($"Error updating configuration: {reply.ErrorMessage}", "Configuration Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
            RefreshConfigCommand.NotifyCanExecuteChanged();
            ApplyConfigCommand.NotifyCanExecuteChanged();
        }
    }

    private void ParseSizeValue(long bytes, out int value, out string unit)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;

        if (bytes >= TB && bytes % TB == 0)
        {
            value = (int)(bytes / TB);
            unit = "TB";
        }
        else if (bytes >= GB && bytes % GB == 0)
        {
            value = (int)(bytes / GB);
            unit = "GB";
        }
        else if (bytes >= MB && bytes % MB == 0)
        {
            value = (int)(bytes / MB);
            unit = "MB";
        }
        else if (bytes >= KB && bytes % KB == 0)
        {
            value = (int)(bytes / KB);
            unit = "KB";
        }
        else
        {
            value = (int)bytes;
            unit = "B";
        }
    }

    private long ConvertToBytes(int value, string unit)
    {
        return unit switch
        {
            "KB" => value * 1024L,
            "MB" => value * 1024L * 1024L,
            "GB" => value * 1024L * 1024L * 1024L,
            "TB" => value * 1024L * 1024L * 1024L * 1024L,
            _ => value // Bytes
        };
    }
    */
}