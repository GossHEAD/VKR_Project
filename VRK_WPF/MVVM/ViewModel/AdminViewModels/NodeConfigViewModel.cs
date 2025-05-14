using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class NodeConfigViewModel : ObservableObject
    {
        private readonly NodeConfigurationManager _configManager;

        
        [ObservableProperty] private string _nodeId = string.Empty;
        [ObservableProperty] private string _displayName = string.Empty;
        [ObservableProperty] private DateTime _creationTime = DateTime.Now;

        
        [ObservableProperty] private string _listenAddress = "0.0.0.0";
        [ObservableProperty] private int _listenPort = 5000;
        [ObservableProperty] private int _maxConnections = 100;
        [ObservableProperty] private int _connectionTimeout = 30;

        
        [ObservableProperty] private string _storageBasePath = "ChunkData";
        [ObservableProperty] private int _maxSizeValue = 10;
        [ObservableProperty] private string _maxSizeUnit = "GB";
        [ObservableProperty] private int _chunkSizeValue = 1;
        [ObservableProperty] private string _chunkSizeUnit = "MB";
        [ObservableProperty] private bool _useHashBasedDirectories = true;
        [ObservableProperty] private int _hashDirectoryDepth = 2;

        
        [ObservableProperty] private string _bootstrapNodeAddress = string.Empty;
        [ObservableProperty] private int _replicationFactor = 3;
        [ObservableProperty] private int _stabilizationInterval = 30;
        [ObservableProperty] private int _replicationCheckInterval = 60;
        [ObservableProperty] private int _replicationMaxParallelism = 10;

        
        [ObservableProperty] private string _databasePath = "Data/node_storage.db";
        [ObservableProperty] private bool _autoMigrate = true;
        [ObservableProperty] private bool _backupBeforeMigration = true;
        [ObservableProperty] private bool _enableSqlLogging = false;

        
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _loadingMessage = "Loading configuration...";
        [ObservableProperty] private string _statusMessage = "Ready";
        [ObservableProperty] private bool _hasChanges;

        
        public ObservableCollection<string> SizeUnits { get; } =
            ["B", "KB", "MB", "GB", "TB"];

        public ObservableCollection<int> HashDepthOptions { get; } = [1, 2, 3];

        
        public RelayCommand RefreshConfigCommand { get; }
        public RelayCommand ApplyConfigCommand { get; }
        public RelayCommand OpenConfigCommand { get; }
        public RelayCommand CreateConfigCommand { get; }

        public NodeConfigViewModel()
        {
            _configManager = new NodeConfigurationManager();
            
            
            RefreshConfigCommand = new RelayCommand(RefreshConfig, CanRefreshConfig);
            ApplyConfigCommand = new RelayCommand(ApplyConfig, CanApplyConfig);
            OpenConfigCommand = new RelayCommand(OpenConfig);
            CreateConfigCommand = new RelayCommand(CreateConfig);

            
            PropertyChanged += (s, e) =>
            {
                
                if (e.PropertyName != nameof(IsLoading) &&
                    e.PropertyName != nameof(LoadingMessage) &&
                    e.PropertyName != nameof(StatusMessage) &&
                    e.PropertyName != nameof(HasChanges))
                {
                    HasChanges = true;
                    ApplyConfigCommand.NotifyCanExecuteChanged();
                }
            };
            
            
            RefreshConfig();
        }

        private bool CanRefreshConfig() => !IsLoading;
        private bool CanApplyConfig() => HasChanges && !IsLoading;

        private void RefreshConfig()
        {
            IsLoading = true;
            LoadingMessage = "Loading node configuration...";
            StatusMessage = "Retrieving configuration...";

            try
            {
                var configs = _configManager.GetAvailableConfigs();
                var currentConfig = configs.FirstOrDefault(c => c.IsCurrentNode);
                
                if (currentConfig != null && File.Exists(currentConfig.ConfigPath))
                {
                    string json = File.ReadAllText(currentConfig.ConfigPath);
                    LoadConfigurationFromJson(json);
                    
                    HasChanges = false;
                    StatusMessage = $"Configuration loaded from {Path.GetFileName(currentConfig.ConfigPath)}";
                }
                else
                {
                    StatusMessage = "No current node configuration found.";
                }
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

        private void OpenConfig()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                Title = "Open Node Configuration File"
            };
            
            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                LoadingMessage = "Loading configuration file...";
                
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    LoadConfigurationFromJson(json);
                    
                    _configManager.SetCurrentConfig(dialog.FileName);
                    
                    HasChanges = false;
                    StatusMessage = $"Loaded configuration from {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error loading configuration: {ex.Message}";
                    MessageBox.Show($"Error loading configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private void CreateConfig()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = ".json",
                FileName = $"Node-{Environment.MachineName}-config.json",
                Title = "Create New Node Configuration File"
            };
            
            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                LoadingMessage = "Creating new configuration...";
                
                try
                {
                    
                    var config = CreateConfigurationObject();
                    
                    
                    string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    
                    
                    File.WriteAllText(dialog.FileName, json);
                    
                    
                    _configManager.SetCurrentConfig(dialog.FileName);
                    
                    HasChanges = false;
                    StatusMessage = $"Created new configuration at {Path.GetFileName(dialog.FileName)}";
                    
                    
                    RefreshConfig();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error creating configuration: {ex.Message}";
                    MessageBox.Show($"Error creating configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        private void ApplyConfig()
        {
            if (!HasChanges)
                return;

            
            var result = MessageBox.Show(
                "Are you sure you want to apply these configuration changes?\nThe node may need to be restarted for changes to take effect.",
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
                var configs = _configManager.GetAvailableConfigs();
                var currentConfig = configs.FirstOrDefault(c => c.IsCurrentNode);
                
                if (currentConfig != null && File.Exists(currentConfig.ConfigPath))
                {
                    
                    var config = CreateConfigurationObject();
                    
                    
                    string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    
                    
                    File.WriteAllText(currentConfig.ConfigPath, json);
                    
                    HasChanges = false;
                    StatusMessage = $"Configuration saved to {Path.GetFileName(currentConfig.ConfigPath)}";
                    
                    MessageBox.Show(
                        "Configuration has been updated successfully.\nA node restart is required for changes to take effect.",
                        "Configuration Updated",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "No current node configuration found.";
                    MessageBox.Show("Cannot save configuration: No current node configuration found.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

        private void LoadConfigurationFromJson(string json)
        {
            try
            {
                
                using JsonDocument doc = JsonDocument.Parse(json);
                
                
                JsonElement root = doc.RootElement;
                
                
                if (root.TryGetProperty("DistributedStorage", out JsonElement dsElement))
                {
                    
                    if (dsElement.TryGetProperty("Identity", out JsonElement identity))
                    {
                        NodeId = identity.GetProperty("NodeId").GetString() ?? string.Empty;
                        DisplayName = identity.TryGetProperty("DisplayName", out var displayName) 
                            ? displayName.GetString() ?? string.Empty 
                            : string.Empty;
                    }
                    
                    
                    if (dsElement.TryGetProperty("Network", out JsonElement network))
                    {
                        ListenAddress = network.GetProperty("ListenAddress").GetString() ?? "localhost";
                        ListenPort = network.GetProperty("ListenPort").GetInt32();
                        MaxConnections = network.TryGetProperty("MaxConnections", out var maxConns) 
                            ? maxConns.GetInt32() 
                            : 100;
                        ConnectionTimeout = network.TryGetProperty("ConnectionTimeoutSeconds", out var timeout) 
                            ? timeout.GetInt32() 
                            : 30;
                    }
                    
                    if (dsElement.TryGetProperty("Storage", out JsonElement storage))
                    {
                        StorageBasePath = storage.GetProperty("BasePath").GetString() ?? "ChunkData";
                        
                        long maxSizeBytes = storage.TryGetProperty("MaxSizeBytes", out var maxSize) 
                            ? maxSize.GetInt64() 
                            : 10L * 1024 * 1024 * 1024; 
                        var maxSizeValue = MaxSizeValue;
                        var maxSizeUnit = MaxSizeUnit;
                        ParseSizeValue(maxSizeBytes, out maxSizeValue, out maxSizeUnit);
                        
                        int chunkSize = storage.TryGetProperty("ChunkSize", out var cSize) 
                            ? cSize.GetInt32() 
                            : 1 * 1024 * 1024; 
                        var chunkSizeValue = ChunkSizeValue;
                        var chunkSizeUnit = ChunkSizeUnit;
                        ParseSizeValue(chunkSize, out chunkSizeValue, out chunkSizeUnit);
                        
                        UseHashBasedDirectories = !storage.TryGetProperty("UseHashBasedDirectories", out var useHash) || useHash.GetBoolean();
                        
                        HashDirectoryDepth = storage.TryGetProperty("HashDirectoryDepth", out var hashDepth) 
                            ? hashDepth.GetInt32() 
                            : 2;
                    }
                    
                    if (dsElement.TryGetProperty("Dht", out JsonElement dht))
                    {
                        BootstrapNodeAddress = dht.TryGetProperty("BootstrapNodeAddress", out var bootstrap) 
                            ? bootstrap.GetString() ?? string.Empty 
                            : string.Empty;
                        
                        ReplicationFactor = dht.TryGetProperty("ReplicationFactor", out var repFactor) 
                            ? repFactor.GetInt32() 
                            : 3;
                        
                        StabilizationInterval = dht.TryGetProperty("StabilizationIntervalSeconds", out var stabInterval) 
                            ? stabInterval.GetInt32() 
                            : 30;
                        
                        ReplicationCheckInterval = dht.TryGetProperty("ReplicationCheckIntervalSeconds", out var repInterval) 
                            ? repInterval.GetInt32() 
                            : 60;
                        
                        ReplicationMaxParallelism = dht.TryGetProperty("ReplicationMaxParallelism", out var repParallel) 
                            ? repParallel.GetInt32() 
                            : 10;
                    }
                    
                    
                    if (dsElement.TryGetProperty("Database", out JsonElement db))
                    {
                        DatabasePath = db.GetProperty("DatabasePath").GetString() ?? "Data/node_storage.db";
                        
                        AutoMigrate = !db.TryGetProperty("AutoMigrate", out var autoMigrate) || autoMigrate.GetBoolean();
                        
                        BackupBeforeMigration = !db.TryGetProperty("BackupBeforeMigration", out var backup) || backup.GetBoolean();
                        
                        EnableSqlLogging = db.TryGetProperty("EnableSqlLogging", out var sqlLogging) && sqlLogging.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                
                throw new InvalidOperationException($"Error parsing configuration: {ex.Message}", ex);
            }
        }

        private object CreateConfigurationObject()
        {
            return new
            {
                DistributedStorage = new
                {
                    Identity = new
                    {
                        NodeId = NodeId,
                        DisplayName = DisplayName
                    },
                    Network = new
                    {
                        ListenAddress = ListenAddress,
                        ListenPort = ListenPort,
                        MaxConnections = MaxConnections,
                        ConnectionTimeoutSeconds = ConnectionTimeout,
                        KnownNodes = new object[] { }
                    },
                    Storage = new
                    {
                        BasePath = StorageBasePath,
                        MaxSizeBytes = ConvertToBytes(MaxSizeValue, MaxSizeUnit),
                        ChunkSize = ConvertToBytes(ChunkSizeValue, ChunkSizeUnit),
                        DefaultReplicationFactor = ReplicationFactor,
                        UseHashBasedDirectories = UseHashBasedDirectories,
                        HashDirectoryDepth = HashDirectoryDepth,
                        PerformIntegrityCheckOnStartup = true
                    },
                    Database = new
                    {
                        DatabasePath = DatabasePath,
                        AutoMigrate = AutoMigrate,
                        BackupBeforeMigration = BackupBeforeMigration,
                        CommandTimeoutSeconds = 60,
                        EnableSqlLogging = EnableSqlLogging
                    },
                    Dht = new
                    {
                        StabilizationIntervalSeconds = StabilizationInterval,
                        FixFingersIntervalSeconds = 60,
                        CheckPredecessorIntervalSeconds = 45,
                        ReplicationCheckIntervalSeconds = ReplicationCheckInterval,
                        ReplicationMaxParallelism = ReplicationMaxParallelism,
                        ReplicationFactor = ReplicationFactor,
                        AutoJoinNetwork = true,
                        BootstrapNodeAddress = BootstrapNodeAddress
                    }
                }
            };
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
                _ => value 
            };
        }
    }
}