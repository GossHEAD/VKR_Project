using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using VKR_Core.Enums;
using VKR_Node.Persistance;
using VKR_Node.Persistance.Entities;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class DatabaseManagementViewModel : ObservableObject
    {
        // Node selection properties
        [ObservableProperty]
        private ObservableCollection<NodeDatabaseInfo> _availableNodeDatabases = new();
        
        [ObservableProperty]
        private NodeDatabaseInfo? _selectedNodeDatabase;
        
        // Table properties
        [ObservableProperty]
        private ObservableCollection<TableViewModel> _availableTables = new();
        
        [ObservableProperty]
        private TableViewModel? _selectedTable;
        
        [ObservableProperty]
        private ObservableCollection<object> _tableData = new();
        
        [ObservableProperty]
        private object? _selectedRow;
        
        // UI state properties
        [ObservableProperty]
        private bool _isLoading;
        
        [ObservableProperty]
        private string _statusMessage = "Ready";
        
        [ObservableProperty]
        private bool _isEditing;
        
        [ObservableProperty]
        private string _searchText = string.Empty;
        
        private NodeDbContext? _dbContext;
        
        public ICollectionView TableDataView { get; }
        
        // Commands
        public RelayCommand RefreshNodeListCommand { get; }
        public RelayCommand RefreshDataCommand { get; }
        public RelayCommand SaveChangesCommand { get; }
        public RelayCommand DeleteRowCommand { get; }
        public RelayCommand AddRowCommand { get; }
        public RelayCommand BackupDatabaseCommand { get; }
        public RelayCommand OpenDatabaseCommand { get; }
        
        public DatabaseManagementViewModel()
        {
            InitializeTableList();
            
            TableDataView = CollectionViewSource.GetDefaultView(TableData);
            TableDataView.Filter = FilterTableData;
            
            RefreshNodeListCommand = new RelayCommand(RefreshNodeList);
            RefreshDataCommand = new RelayCommand(async () => await RefreshTableDataAsync(), CanRefreshData);
            SaveChangesCommand = new RelayCommand(async () => await SaveChangesAsync(), CanSaveChanges);
            DeleteRowCommand = new RelayCommand(async () => await DeleteRowAsync(), CanDeleteRow);
            AddRowCommand = new RelayCommand(AddNewRow, CanAddRow);
            BackupDatabaseCommand = new RelayCommand(async () => await BackupDatabaseAsync(), CanBackupDatabase);
            OpenDatabaseCommand = new RelayCommand(OpenDatabase);
            
            InitializeTableList();
            RefreshNodeList();
        }
        
        private void InitializeTableList()
        {
            AvailableTables.Clear();
            AvailableTables.Add(new TableViewModel { TableName = "Files", DisplayName = "Files Metadata", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Chunks", DisplayName = "Chunks Metadata", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "ChunkLocations", DisplayName = "Chunk Locations", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Nodes", DisplayName = "Node States", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Users", DisplayName = "Users", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Logs", DisplayName = "Logs", CanEdit = false });
            
            // Set default selected table
            //SelectedTable = AvailableTables.FirstOrDefault();
        }
        
        private void RefreshNodeList()
        {
            IsLoading = true;
            StatusMessage = "Searching for node databases...";
            
            try
            {
                AvailableNodeDatabases.Clear();
                
                // Search for node databases in common locations
                var searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage", "Data"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VKR_Network", "Data"),
                };
                
                foreach (var basePath in searchPaths)
                {
                    if (Directory.Exists(basePath))
                    {
                        // Look for database files
                        foreach (var file in Directory.GetFiles(basePath, "node_*.db"))
                        {
                            var fileName = Path.GetFileName(file);
                            // Try to extract node ID from the filename
                            var nodeId = ExtractNodeIdFromFileName(fileName);
                            
                            AvailableNodeDatabases.Add(new NodeDatabaseInfo
                            {
                                NodeId = nodeId,
                                DbPath = file,
                                LastModified = File.GetLastWriteTime(file)
                            });
                        }
                    }
                }
                
                if (AvailableNodeDatabases.Count > 0)
                {
                    SelectedNodeDatabase = AvailableNodeDatabases[0];
                    StatusMessage = $"Found {AvailableNodeDatabases.Count} node databases";
                }
                else
                {
                    StatusMessage = "No node databases found. Use the Open button to select a database file.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error finding node databases: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private string ExtractNodeIdFromFileName(string fileName)
        {
            try
            {
                // Expected format: node_{NodeID}.db
                var match = System.Text.RegularExpressions.Regex.Match(fileName, @"node[_-](\w+)\.db$");
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
                // If regex fails, fall back to simpler approach
            }
            
            // Simple fallback
            return Path.GetFileNameWithoutExtension(fileName).Replace("node_", "").Replace("node-", "");
        }
        
        private void OpenDatabase()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = ".db",
                Title = "Open Node Database"
            };
            
            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Opening database...";
                
                try
                {
                    // Add to available databases if not already present
                    var dbPath = dialog.FileName;
                    var fileName = Path.GetFileName(dbPath);
                    var nodeId = ExtractNodeIdFromFileName(fileName);
                    
                    var existingDatabase = AvailableNodeDatabases.FirstOrDefault(db => db.DbPath == dbPath);
                    if (existingDatabase == null)
                    {
                        var dbInfo = new NodeDatabaseInfo
                        {
                            NodeId = nodeId,
                            DbPath = dbPath,
                            LastModified = File.GetLastWriteTime(dbPath)
                        };
                        
                        AvailableNodeDatabases.Add(dbInfo);
                        SelectedNodeDatabase = dbInfo;
                    }
                    else
                    {
                        SelectedNodeDatabase = existingDatabase;
                    }
                    
                    StatusMessage = $"Opened database for Node {nodeId}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error opening database: {ex.Message}";
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }
        
        partial void OnSelectedNodeDatabaseChanged(NodeDatabaseInfo? oldValue, NodeDatabaseInfo? newValue)
        {
            if (newValue != null)
            {
                ConnectToDatabase(newValue.DbPath);
            }
            else
            {
                // Disconnect from database
                _dbContext?.Dispose();
                _dbContext = null;
                
                TableData.Clear();
                StatusMessage = "No database selected";
            }
            
            // Update command availability
            RefreshDataCommand.NotifyCanExecuteChanged();
            SaveChangesCommand.NotifyCanExecuteChanged();
            DeleteRowCommand.NotifyCanExecuteChanged();
            AddRowCommand.NotifyCanExecuteChanged();
            BackupDatabaseCommand.NotifyCanExecuteChanged();
        }
        
        private void ConnectToDatabase(string dbPath)
        {
            IsLoading = true;
            StatusMessage = $"Connecting to database...";
            
            try
            {
                // Dispose of existing context if any
                _dbContext?.Dispose();
                
                // Create new database context
                var optionsBuilder = new DbContextOptionsBuilder<NodeDbContext>();
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
                
                // Create a minimal options
                var dbOptions = Microsoft.Extensions.Options.Options.Create(new VKR_Node.Configuration.DatabaseOptions 
                { 
                    DatabasePath = dbPath,
                    HasExplicitConnectionString = false
                });
                
                _dbContext = new NodeDbContext(optionsBuilder.Options, dbOptions);
                
                // Clear existing data
                TableData.Clear();
                
                // Load data if a table is selected
                if (SelectedTable != null)
                {
                    _ = RefreshTableDataAsync();
                }
                else
                {
                    StatusMessage = "Connected to database. Select a table to view data.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error connecting to database: {ex.Message}";
                _dbContext = null;
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        partial void OnSelectedTableChanged(TableViewModel? oldValue, TableViewModel? newValue)
        {
            if (newValue != null && _dbContext != null)
            {
                _ = RefreshTableDataAsync();
            }
            else
            {
                TableData.Clear();
            }
    
            RefreshDataCommand.NotifyCanExecuteChanged();
            SaveChangesCommand.NotifyCanExecuteChanged();
            DeleteRowCommand.NotifyCanExecuteChanged();
            AddRowCommand.NotifyCanExecuteChanged();
        }
        
        partial void OnSearchTextChanged(string value)
        {
            TableDataView.Refresh();
        }
        
        private bool FilterTableData(object item)
        {
            if (string.IsNullOrWhiteSpace(SearchText))
                return true;
                
            // Simple contains search on all string properties
            var properties = item.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(int) || p.PropertyType == typeof(long));
                
            foreach (var property in properties)
            {
                var value = property.GetValue(item)?.ToString();
                if (!string.IsNullOrEmpty(value) && value.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }
        
        private bool CanRefreshData() => !IsLoading && _dbContext != null && SelectedTable != null;
        private bool CanSaveChanges() => !IsLoading && _dbContext != null && SelectedTable?.CanEdit == true && TableData.OfType<IModifiableRow>().Any(r => r.IsModified);
        private bool CanDeleteRow() => !IsLoading && _dbContext != null && SelectedTable?.CanEdit == true && SelectedRow != null;
        private bool CanAddRow() => !IsLoading && _dbContext != null && SelectedTable?.CanEdit == true;
        private bool CanBackupDatabase() => !IsLoading && _dbContext != null;
        
        private async Task RefreshTableDataAsync()
        {
            if (_dbContext == null || SelectedTable == null)
                return;
                
            IsLoading = true;
            StatusMessage = $"Loading {SelectedTable.DisplayName} data...";
            
            try
            {
                TableData.Clear();
                
                switch (SelectedTable.TableName)
                {
                    case "Files":
                        await LoadFilesDataAsync();
                        break;
                    case "Chunks":
                        await LoadChunksDataAsync();
                        break;
                    case "ChunkLocations":
                        await LoadChunkLocationsDataAsync();
                        break;
                    case "Nodes":
                        await LoadNodesDataAsync();
                        break;
                    case "Users":
                        await LoadUsersDataAsync();
                        break;
                    case "Logs":
                        await LoadLogsDataAsync();
                        break;
                    default:
                        StatusMessage = $"Unknown table: {SelectedTable.TableName}";
                        break;
                }
                
                StatusMessage = $"Loaded {TableData.Count} rows from {SelectedTable.DisplayName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading table data: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                RefreshDataCommand.NotifyCanExecuteChanged();
                SaveChangesCommand.NotifyCanExecuteChanged();
                DeleteRowCommand.NotifyCanExecuteChanged();
                AddRowCommand.NotifyCanExecuteChanged();
            }
        }
        
        private async Task LoadFilesDataAsync()
        {
            var files = await _dbContext!.FilesMetadata.ToListAsync();
            foreach (var file in files)
            {
                TableData.Add(new FileRowViewModel
                {
                    FileId = file.FileId,
                    FileName = file.FileName,
                    FileSize = file.FileSize,
                    CreationTime = file.CreationTime,
                    ModificationTime = file.ModificationTime,
                    ContentType = file.ContentType,
                    ChunkSize = file.ChunkSize,
                    TotalChunks = file.TotalChunks,
                    State = file.State
                });
            }
        }
        
        private async Task LoadChunksDataAsync()
        {
            var chunks = await _dbContext!.ChunksMetadata.ToListAsync();
            foreach (var chunk in chunks)
            {
                TableData.Add(new ChunkRowViewModel
                {
                    FileId = chunk.FileId,
                    ChunkId = chunk.ChunkId,
                    ChunkIndex = chunk.ChunkIndex,
                    Size = chunk.Size,
                    ChunkHash = chunk.ChunkHash
                });
            }
        }
        
        private async Task LoadChunkLocationsDataAsync()
        {
            var locations = await _dbContext!.ChunkLocations.ToListAsync();
            foreach (var location in locations)
            {
                TableData.Add(new ChunkLocationRowViewModel
                {
                    FileId = location.FileId,
                    ChunkId = location.ChunkId,
                    StoredNodeId = location.StoredNodeId,
                    ReplicationTime = location.ReplicationTime ?? DateTime.MinValue
                });
            }
        }
        
        private async Task LoadNodesDataAsync()
        {
            var nodes = await _dbContext!.NodeStates.ToListAsync();
            foreach (var node in nodes)
            {
                TableData.Add(new NodeRowViewModel
                {
                    NodeId = node.NodeId,
                    Address = node.Address,
                    State = node.State,
                    LastSeen = node.LastSeen,
                    DiskSpaceAvailableBytes = node.DiskSpaceAvailableBytes,
                    DiskSpaceTotalBytes = node.DiskSpaceTotalBytes,
                    StoredChunkCount = node.StoredChunkCount
                });
            }
        }
        
        private async Task LoadUsersDataAsync()
        {
            var users = await _dbContext!.Users.ToListAsync();
            foreach (var user in users)
            {
                TableData.Add(new UserRowViewModel
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Role = (int)user.Role,
                    IsActive = user.IsActive,
                    CreationTime = user.CreationTime
                });
            }
        }
        
        private async Task LoadLogsDataAsync()
        {
            // Add code to load logs if there's a logs table
            // This would depend on the structure of your logs table
            StatusMessage = "Log viewing not implemented yet";
        }
        
        private async Task SaveChangesAsync()
        {
            if (_dbContext == null || SelectedTable == null || !SelectedTable.CanEdit)
                return;
                
            IsLoading = true;
            StatusMessage = $"Saving changes to {SelectedTable.DisplayName}...";
            
            try
            {
                // Find modified rows
                var modifiedRows = TableData.OfType<IModifiableRow>().Where(r => r.IsModified).ToList();
                
                if (!modifiedRows.Any())
                {
                    StatusMessage = "No changes to save.";
                    IsLoading = false;
                    return;
                }
                
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                
                // Apply changes based on table type
                switch (SelectedTable.TableName)
                {
                    case "Files":
                        await SaveFilesChangesAsync(modifiedRows.Cast<FileRowViewModel>().ToList());
                        break;
                    case "Chunks":
                        await SaveChunksChangesAsync(modifiedRows.Cast<ChunkRowViewModel>().ToList());
                        break;
                    case "ChunkLocations":
                        await SaveChunkLocationsChangesAsync(modifiedRows.Cast<ChunkLocationRowViewModel>().ToList());
                        break;
                    case "Nodes":
                        await SaveNodesChangesAsync(modifiedRows.Cast<NodeRowViewModel>().ToList());
                        break;
                    case "Users":
                        await SaveUsersChangesAsync(modifiedRows.Cast<UserRowViewModel>().ToList());
                        break;
                    default:
                        throw new InvalidOperationException($"Cannot save changes to {SelectedTable.TableName}");
                }
                
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                
                // Reset modified flags
                foreach (var row in modifiedRows)
                {
                    row.IsModified = false;
                }
                
                StatusMessage = $"Saved {modifiedRows.Count} changes to {SelectedTable.DisplayName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving changes: {ex.Message}";
                MessageBox.Show($"Error saving changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                SaveChangesCommand.NotifyCanExecuteChanged();
            }
        }
        
        private async Task SaveFilesChangesAsync(List<FileRowViewModel> modifiedRows)
        {
            foreach (var row in modifiedRows)
            {
                var entity = await _dbContext!.FilesMetadata.FindAsync(row.FileId);
                if (entity != null)
                {
                    entity.FileName = row.FileName;
                    entity.FileSize = row.FileSize;
                    entity.ContentType = row.ContentType;
                    entity.ChunkSize = row.ChunkSize;
                    entity.TotalChunks = row.TotalChunks;
                    entity.State = row.State;
                    entity.ModificationTime = DateTime.UtcNow;
                }
                else
                {
                    // Add new entity
                    _dbContext!.FilesMetadata.Add(new FileEntity
                    {
                        FileId = row.FileId,
                        FileName = row.FileName,
                        FileSize = row.FileSize,
                        CreationTime = row.CreationTime,
                        ModificationTime = DateTime.UtcNow,
                        ContentType = row.ContentType,
                        ChunkSize = row.ChunkSize,
                        TotalChunks = row.TotalChunks,
                        State = row.State
                    });
                }
            }
        }
        
        private async Task SaveChunksChangesAsync(List<ChunkRowViewModel> modifiedRows)
        {
            foreach (var row in modifiedRows)
            {
                var entity = await _dbContext!.ChunksMetadata.FindAsync(row.FileId, row.ChunkId);
                if (entity != null)
                {
                    entity.ChunkIndex = row.ChunkIndex;
                    entity.Size = row.Size;
                    entity.ChunkHash = row.ChunkHash;
                }
                else
                {
                    // Add new entity
                    _dbContext!.ChunksMetadata.Add(new ChunkEntity
                    {
                        FileId = row.FileId,
                        ChunkId = row.ChunkId,
                        ChunkIndex = row.ChunkIndex,
                        Size = row.Size,
                        ChunkHash = row.ChunkHash
                    });
                }
            }
        }
        
        private async Task SaveChunkLocationsChangesAsync(List<ChunkLocationRowViewModel> modifiedRows)
        {
            foreach (var row in modifiedRows)
            {
                var entity = await _dbContext!.ChunkLocations.FindAsync(row.FileId, row.ChunkId, row.StoredNodeId);
                if (entity != null)
                {
                    entity.ReplicationTime = row.ReplicationTime;
                }
                else
                {
                    // Add new entity
                    _dbContext!.ChunkLocations.Add(new ChunkLocationEntity
                    {
                        FileId = row.FileId,
                        ChunkId = row.ChunkId,
                        StoredNodeId = row.StoredNodeId,
                        ReplicationTime = row.ReplicationTime
                    });
                }
            }
        }
        
        private async Task SaveNodesChangesAsync(List<NodeRowViewModel> modifiedRows)
        {
            foreach (var row in modifiedRows)
            {
                var entity = await _dbContext!.NodeStates.FindAsync(row.NodeId);
                if (entity != null)
                {
                    entity.Address = row.Address;
                    entity.State = row.State;
                    entity.LastSeen = row.LastSeen;
                    entity.DiskSpaceAvailableBytes = row.DiskSpaceAvailableBytes;
                    entity.DiskSpaceTotalBytes = row.DiskSpaceTotalBytes;
                    entity.StoredChunkCount = row.StoredChunkCount;
                }
                else
                {
                    // Add new entity
                    _dbContext!.NodeStates.Add(new NodeEntity
                    {
                        NodeId = row.NodeId,
                        Address = row.Address,
                        State = row.State,
                        LastSeen = row.LastSeen,
                        DiskSpaceAvailableBytes = row.DiskSpaceAvailableBytes,
                        DiskSpaceTotalBytes = row.DiskSpaceTotalBytes,
                        StoredChunkCount = row.StoredChunkCount
                    });
                }
            }
        }
        
        private async Task SaveUsersChangesAsync(List<UserRowViewModel> modifiedRows)
        {
            foreach (var row in modifiedRows)
            {
                var entity = await _dbContext!.Users.FindAsync(row.UserId);
                if (entity != null)
                {
                    entity.Username = row.Username;
                    entity.Role = (UserRole)row.Role;
                    entity.IsActive = row.IsActive;
                }
                else
                {
                    // Add new entity
                    _dbContext!.Users.Add(new UserEntity
                    {
                        UserId = row.UserId,
                        Username = row.Username,
                        Role = (UserRole)row.Role,
                        IsActive = row.IsActive,
                        CreationTime = row.CreationTime
                    });
                }
            }
        }
        
        private async Task DeleteRowAsync()
        {
            if (_dbContext == null || SelectedTable == null || !SelectedTable.CanEdit || SelectedRow == null)
                return;
                
            // Confirm deletion
            var result = MessageBox.Show($"Are you sure you want to delete this {SelectedTable.TableName} record?", 
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                
            if (result != MessageBoxResult.Yes)
                return;
                
            IsLoading = true;
            StatusMessage = $"Deleting record from {SelectedTable.DisplayName}...";
            
            try
            {
                bool deleted = false;
                
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                
                // Delete based on table type
                switch (SelectedTable.TableName)
                {
                    case "Files":
                        deleted = await DeleteFileRowAsync((FileRowViewModel)SelectedRow);
                        break;
                    case "Chunks":
                        deleted = await DeleteChunkRowAsync((ChunkRowViewModel)SelectedRow);
                        break;
                    case "ChunkLocations":
                        deleted = await DeleteChunkLocationRowAsync((ChunkLocationRowViewModel)SelectedRow);
                        break;
                    case "Nodes":
                        deleted = await DeleteNodeRowAsync((NodeRowViewModel)SelectedRow);
                        break;
                    case "Users":
                        deleted = await DeleteUserRowAsync((UserRowViewModel)SelectedRow);
                        break;
                    default:
                        throw new InvalidOperationException($"Cannot delete from {SelectedTable.TableName}");
                }
                
                if (deleted)
                {
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    
                    // Remove from collection
                    TableData.Remove(SelectedRow);
                    
                    StatusMessage = $"Deleted record from {SelectedTable.DisplayName}.";
                }
                else
                {
                    await transaction.RollbackAsync();
                    StatusMessage = "Delete operation failed";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error deleting record: {ex.Message}";
                MessageBox.Show($"Error deleting record: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                DeleteRowCommand.NotifyCanExecuteChanged();
            }
        }
        
        private async Task<bool> DeleteFileRowAsync(FileRowViewModel row)
        {
            var entity = await _dbContext!.FilesMetadata.FindAsync(row.FileId);
            if (entity != null)
            {
                _dbContext.FilesMetadata.Remove(entity);
                return true;
            }
            return false;
        }
        
        private async Task<bool> DeleteChunkRowAsync(ChunkRowViewModel row)
        {
            var entity = await _dbContext!.ChunksMetadata.FindAsync(row.FileId, row.ChunkId);
            if (entity != null)
            {
                _dbContext.ChunksMetadata.Remove(entity);
                return true;
            }
            return false;
        }
        
        private async Task<bool> DeleteChunkLocationRowAsync(ChunkLocationRowViewModel row)
        {
            var entity = await _dbContext!.ChunkLocations.FindAsync(row.FileId, row.ChunkId, row.StoredNodeId);
            if (entity != null)
            {
                _dbContext.ChunkLocations.Remove(entity);
                return true;
            }
            return false;
        }
        
        private async Task<bool> DeleteNodeRowAsync(NodeRowViewModel row)
        {
            var entity = await _dbContext!.NodeStates.FindAsync(row.NodeId);
            if (entity != null)
            {
                _dbContext.NodeStates.Remove(entity);
                return true;
            }
            return false;
        }
        
        private async Task<bool> DeleteUserRowAsync(UserRowViewModel row)
        {
            var entity = await _dbContext!.Users.FindAsync(row.UserId);
            if (entity != null)
            {
                _dbContext.Users.Remove(entity);
                return true;
            }
            return false;
        }
        
        private void AddNewRow()
        {
            if (_dbContext == null || SelectedTable == null || !SelectedTable.CanEdit)
                return;
                
            // Create new row based on table type
            object newRow;
            
            switch (SelectedTable.TableName)
            {
                case "Files":
                    newRow = new FileRowViewModel
                    {
                        FileId = Guid.NewGuid().ToString(),
                        FileName = "NewFile",
                        CreationTime = DateTime.UtcNow,
                        ModificationTime = DateTime.UtcNow,
                        State = 0,
                        IsModified = true
                    };
                    break;
                    
                case "Chunks":
                    newRow = new ChunkRowViewModel
                    {
                        FileId = TableData.OfType<FileRowViewModel>().FirstOrDefault()?.FileId ?? Guid.NewGuid().ToString(),
                        ChunkId = Guid.NewGuid().ToString(),
                        ChunkIndex = 0,
                        Size = 0,
                        IsModified = true
                    };
                    break;
                    
                case "ChunkLocations":
                    var chunks = TableData.OfType<ChunkRowViewModel>().ToList();
                    if (chunks.Count == 0)
                    {
                        MessageBox.Show("Cannot create chunk location without existing chunks.", 
                            "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    
                    var firstChunk = chunks.First();
                    newRow = new ChunkLocationRowViewModel
                    {
                        FileId = firstChunk.FileId,
                        ChunkId = firstChunk.ChunkId,
                        StoredNodeId = "NewNode",
                        ReplicationTime = DateTime.UtcNow,
                        IsModified = true
                    };
                    break;
                    
                case "Nodes":
                    newRow = new NodeRowViewModel
                    {
                        NodeId = $"Node{TableData.Count + 1}",
                        Address = "localhost:5000",
                        State = 0,
                        LastSeen = DateTime.UtcNow,
                        DiskSpaceAvailableBytes = 1000000000,
                        DiskSpaceTotalBytes = 10000000000,
                        StoredChunkCount = 0,
                        IsModified = true
                    };
                    break;
                    
                case "Users":
                    newRow = new UserRowViewModel
                    {
                        UserId = TableData.Count > 0 ? 
                            TableData.OfType<UserRowViewModel>().Max(u => u.UserId) + 1 : 1,
                        Username = "NewUser",
                        Role = (int)UserRole.ITSpecialist,
                        IsActive = true,
                        CreationTime = DateTime.UtcNow,
                        IsModified = true
                    };
                    break;
                    
                default:
                    MessageBox.Show($"Adding new rows to {SelectedTable.TableName} is not currently supported.", 
                        "Not Supported", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
            }
            
            // Add to collection
            TableData.Add(newRow);
            SelectedRow = newRow;
            
            StatusMessage = $"Added new row to {SelectedTable.DisplayName}. Don't forget to save changes.";
            SaveChangesCommand.NotifyCanExecuteChanged();
        }
        
        private async Task BackupDatabaseAsync()
        {
            if (_dbContext == null || SelectedNodeDatabase == null)
                return;
                
            var dialog = new SaveFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = ".db",
                FileName = $"backup_{Path.GetFileName(SelectedNodeDatabase.DbPath)}"
            };
            
            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Backing up database...";
                
                try
                {
                    // Close existing connections
                    await _dbContext.Database.CloseConnectionAsync();
                    
                    // Create a backup connection
                    using var sourceConnection = new SqliteConnection($"Data Source={SelectedNodeDatabase.DbPath}");
                    await sourceConnection.OpenAsync();
                    
                    using var destinationConnection = new SqliteConnection($"Data Source={dialog.FileName}");
                    await destinationConnection.OpenAsync();
                    
                    // Perform the backup
                    sourceConnection.BackupDatabase(destinationConnection);
                    
                    StatusMessage = $"Database successfully backed up to {dialog.FileName}";
                    
                    MessageBox.Show($"Database backup completed successfully.\nLocation: {dialog.FileName}", 
                        "Backup Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        
                    // Reconnect to the database
                    ConnectToDatabase(SelectedNodeDatabase.DbPath);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error backing up database: {ex.Message}";
                    MessageBox.Show($"Error backing up database: {ex.Message}", 
                        "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }
        
        public void Dispose()
        {
            _dbContext?.Dispose();
        }
    }
    
    // Class to store node database information
    public class NodeDatabaseInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string DbPath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        
        public override string ToString()
        {
            return $"Node {NodeId} (Last Modified: {LastModified:g})";
        }
    }
}