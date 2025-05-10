using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using VKR.Protos;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class DatabaseManagementViewModel : ObservableObject
{
    /*
    private readonly ILogger<DatabaseManagementViewModel>? _logger;
    private StorageService.StorageServiceClient? _storageClient;
    
    [ObservableProperty]
    private ObservableCollection<TableViewModel> _availableTables = new();
    
    [ObservableProperty]
    private TableViewModel? _selectedTable;
    
    [ObservableProperty]
    private ObservableCollection<object> _tableData = new();
    
    [ObservableProperty]
    private object? _selectedRow;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = "Ready";
    
    [ObservableProperty]
    private bool _isEditing;
    
    [ObservableProperty]
    private string _searchText = string.Empty;
    
    public ICollectionView TableDataView { get; }
    
    public DatabaseManagementViewModel(ILogger<DatabaseManagementViewModel>? logger = null, StorageService.StorageServiceClient? client = null)
    {
        _logger = logger;
        _storageClient = client;
        
        // Initialize available tables
        AvailableTables.Add(new TableViewModel { TableName = "Files", DisplayName = "Files Metadata", CanEdit = true });
        AvailableTables.Add(new TableViewModel { TableName = "Chunks", DisplayName = "Chunks Metadata", CanEdit = true });
        AvailableTables.Add(new TableViewModel { TableName = "ChunkLocations", DisplayName = "Chunk Locations", CanEdit = true });
        AvailableTables.Add(new TableViewModel { TableName = "Nodes", DisplayName = "Node States", CanEdit = true });
        AvailableTables.Add(new TableViewModel { TableName = "Users", DisplayName = "Users", CanEdit = true });
        
        // Set default selected table
        SelectedTable = AvailableTables.FirstOrDefault();
        
        // Initialize view
        TableDataView = CollectionViewSource.GetDefaultView(TableData);
        TableDataView.Filter = FilterTableData;
        
        // Initialize commands
        RefreshDataCommand = new RelayCommand(async () => await RefreshTableDataAsync(), CanRefreshData);
        SaveChangesCommand = new RelayCommand(async () => await SaveChangesAsync(), CanSaveChanges);
        DeleteRowCommand = new RelayCommand(async () => await DeleteRowAsync(), CanDeleteRow);
        AddRowCommand = new RelayCommand(AddNewRow, CanAddRow);
        BackupDatabaseCommand = new RelayCommand(async () => await BackupDatabaseAsync(), CanBackupDatabase);
    }
    
    partial void OnSelectedTableChanged(TableViewModel? oldValue, TableViewModel? newValue)
    {
        if (newValue != null)
        {
            RefreshTableDataAsync().ConfigureAwait(false);
        }
        
        // Update command availability
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
    
    public void SetClient(StorageService.StorageServiceClient client)
    {
        _storageClient = client;
        RefreshDataCommand.NotifyCanExecuteChanged();
        BackupDatabaseCommand.NotifyCanExecuteChanged();
    }
    
    // Commands
    public RelayCommand RefreshDataCommand { get; }
    public RelayCommand SaveChangesCommand { get; }
    public RelayCommand DeleteRowCommand { get; }
    public RelayCommand AddRowCommand { get; }
    public RelayCommand BackupDatabaseCommand { get; }
    
    private bool CanRefreshData() => !IsLoading && _storageClient != null;
    private bool CanSaveChanges() => IsEditing && SelectedTable?.CanEdit == true && !IsLoading && _storageClient != null;
    private bool CanDeleteRow() => SelectedRow != null && SelectedTable?.CanEdit == true && !IsLoading && _storageClient != null;
    private bool CanAddRow() => SelectedTable?.CanEdit == true && !IsLoading && _storageClient != null;
    private bool CanBackupDatabase() => !IsLoading && _storageClient != null;
    
    private async Task RefreshTableDataAsync()
    {
        if (SelectedTable == null || _storageClient == null)
            return;
            
        IsLoading = true;
        StatusMessage = $"Loading {SelectedTable.DisplayName} data...";
        
        try
        {
            TableData.Clear();
            
            // Request table data from server
            var request = new AdminQueryTableRequest
            {
                TableName = SelectedTable.TableName
            };
            
            var response = await _storageClient.QueryTableAsync(request);
            
            if (response.Success)
            {
                // Process rows based on table type
                switch (SelectedTable.TableName)
                {
                    case "Files":
                        foreach (var fileRow in response.FileRows)
                        {
                            TableData.Add(new FileRowViewModel
                            {
                                FileId = fileRow.FileId,
                                FileName = fileRow.FileName,
                                FileSize = fileRow.FileSize,
                                CreationTime = fileRow.CreationTime?.ToDateTime() ?? DateTime.MinValue,
                                ModificationTime = fileRow.ModificationTime?.ToDateTime() ?? DateTime.MinValue,
                                ContentType = fileRow.ContentType,
                                ChunkSize = fileRow.ChunkSize,
                                TotalChunks = fileRow.TotalChunks,
                                State = fileRow.State
                            });
                        }
                        break;
                        
                    case "Chunks":
                        foreach (var chunkRow in response.ChunkRows)
                        {
                            TableData.Add(new ChunkRowViewModel
                            {
                                FileId = chunkRow.FileId,
                                ChunkId = chunkRow.ChunkId,
                                ChunkIndex = chunkRow.ChunkIndex,
                                Size = chunkRow.Size,
                                ChunkHash = chunkRow.ChunkHash
                            });
                        }
                        break;
                        
                    case "ChunkLocations":
                        foreach (var locationRow in response.LocationRows)
                        {
                            TableData.Add(new ChunkLocationRowViewModel
                            {
                                FileId = locationRow.FileId,
                                ChunkId = locationRow.ChunkId,
                                StoredNodeId = locationRow.StoredNodeId,
                                ReplicationTime = locationRow.ReplicationTime?.ToDateTime() ?? DateTime.MinValue
                            });
                        }
                        break;
                        
                    case "Nodes":
                        foreach (var nodeRow in response.NodeRows)
                        {
                            TableData.Add(new NodeRowViewModel
                            {
                                NodeId = nodeRow.NodeId,
                                Address = nodeRow.Address,
                                State = nodeRow.State,
                                LastSeen = nodeRow.LastSeen?.ToDateTime() ?? DateTime.MinValue,
                                DiskSpaceAvailableBytes = nodeRow.DiskSpaceAvailableBytes,
                                DiskSpaceTotalBytes = nodeRow.DiskSpaceTotalBytes,
                                StoredChunkCount = nodeRow.StoredChunkCount
                            });
                        }
                        break;
                        
                    case "Users":
                        foreach (var userRow in response.UserRows)
                        {
                            TableData.Add(new UserRowViewModel
                            {
                                UserId = userRow.UserId,
                                Username = userRow.Username,
                                Role = userRow.Role,
                                IsActive = userRow.IsActive,
                                CreationTime = userRow.CreationTime?.ToDateTime() ?? DateTime.MinValue
                            });
                        }
                        break;
                }
                
                StatusMessage = $"Loaded {TableData.Count} rows from {SelectedTable.DisplayName}.";
            }
            else
            {
                StatusMessage = $"Error loading data: {response.ErrorMessage}";
                MessageBox.Show($"Failed to load data: {response.ErrorMessage}", "Data Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (RpcException ex)
        {
            StatusMessage = $"gRPC Error: {ex.Status.Detail}";
            MessageBox.Show($"A communication error occurred: {ex.Status.Detail}", "gRPC Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            IsEditing = false;
            RefreshDataCommand.NotifyCanExecuteChanged();
            SaveChangesCommand.NotifyCanExecuteChanged();
            DeleteRowCommand.NotifyCanExecuteChanged();
            AddRowCommand.NotifyCanExecuteChanged();
        }
    }
    
    private async Task SaveChangesAsync()
    {
        if (SelectedTable == null || _storageClient == null)
            return;
            
        IsLoading = true;
        StatusMessage = $"Saving changes to {SelectedTable.DisplayName}...";
        
        try
        {
            // Create update request based on table type
            var request = new AdminUpdateTableRequest
            {
                TableName = SelectedTable.TableName
            };
            
            // Add updated rows to request
            switch (SelectedTable.TableName)
            {
                case "Files":
                    foreach (var item in TableData)
                    {
                        if (item is FileRowViewModel fileRow && fileRow.IsModified)
                        {
                            request.UpdatedFileRows.Add(new FileRowData
                            {
                                FileId = fileRow.FileId,
                                FileName = fileRow.FileName,
                                FileSize = fileRow.FileSize,
                                State = fileRow.State,
                                ChunkSize = fileRow.ChunkSize,
                                TotalChunks = fileRow.TotalChunks,
                                ContentType = fileRow.ContentType ?? ""
                            });
                        }
                    }
                    break;
                    
                case "Nodes":
                    foreach (var item in TableData)
                    {
                        if (item is NodeRowViewModel nodeRow && nodeRow.IsModified)
                        {
                            request.UpdatedNodeRows.Add(new NodeRowData
                            {
                                NodeId = nodeRow.NodeId,
                                Address = nodeRow.Address,
                                State = nodeRow.State
                            });
                        }
                    }
                    break;
                    
                case "Users":
                    foreach (var item in TableData)
                    {
                        if (item is UserRowViewModel userRow && userRow.IsModified)
                        {
                            request.UpdatedUserRows.Add(new UserRowData
                            {
                                UserId = userRow.UserId,
                                Username = userRow.Username,
                                Role = userRow.Role,
                                IsActive = userRow.IsActive
                            });
                        }
                    }
                    break;
            }
            
            // Send update request
            var response = await _storageClient.UpdateTableAsync(request);
            
            if (response.Success)
            {
                StatusMessage = $"Successfully saved {response.RowsAffected} changes to {SelectedTable.DisplayName}.";
                
                // Reset modified flags
                foreach (IModifiableRow item in TableData)
                {
                    item.IsModified = false;
                }
                
                IsEditing = false;
            }
            else
            {
                StatusMessage = $"Error saving changes: {response.ErrorMessage}";
                MessageBox.Show($"Failed to save changes: {response.ErrorMessage}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (RpcException ex)
        {
            StatusMessage = $"gRPC Error: {ex.Status.Detail}";
            MessageBox.Show($"A communication error occurred: {ex.Status.Detail}", "gRPC Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            SaveChangesCommand.NotifyCanExecuteChanged();
        }
    }
    
    private async Task DeleteRowAsync()
    {
        if (SelectedRow == null || SelectedTable == null || _storageClient == null)
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
            // Create delete request based on table type
            var request = new AdminDeleteRowRequest
            {
                TableName = SelectedTable.TableName
            };
            
            // Set key fields based on table type
            switch (SelectedTable.TableName)
            {
                case "Files":
                    if (SelectedRow is FileRowViewModel fileRow)
                    {
                        request.FileId = fileRow.FileId;
                    }
                    break;
                    
                case "Chunks":
                    if (SelectedRow is ChunkRowViewModel chunkRow)
                    {
                        request.FileId = chunkRow.FileId;
                        request.ChunkId = chunkRow.ChunkId;
                    }
                    break;
                    
                case "ChunkLocations":
                    if (SelectedRow is ChunkLocationRowViewModel locationRow)
                    {
                        request.FileId = locationRow.FileId;
                        request.ChunkId = locationRow.ChunkId;
                        request.NodeId = locationRow.StoredNodeId;
                    }
                    break;
                    
                case "Nodes":
                    if (SelectedRow is NodeRowViewModel nodeRow)
                    {
                        request.NodeId = nodeRow.NodeId;
                    }
                    break;
                    
                case "Users":
                    if (SelectedRow is UserRowViewModel userRow)
                    {
                        request.UserId = userRow.UserId;
                    }
                    break;
            }
            
            // Send delete request
            var response = await _storageClient.DeleteRowAsync(request);
            
            if (response.Success)
            {
                StatusMessage = $"Successfully deleted record from {SelectedTable.DisplayName}.";
                
                // Remove from local collection
                TableData.Remove(SelectedRow);
                
                // Refresh data
                await RefreshTableDataAsync();
            }
            else
            {
                StatusMessage = $"Error deleting record: {response.ErrorMessage}";
                MessageBox.Show($"Failed to delete record: {response.ErrorMessage}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (RpcException ex)
        {
            StatusMessage = $"gRPC Error: {ex.Status.Detail}";
            MessageBox.Show($"A communication error occurred: {ex.Status.Detail}", "gRPC Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
            DeleteRowCommand.NotifyCanExecuteChanged();
        }
    }
    
    private void AddNewRow()
    {
        if (SelectedTable == null)
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
                    CreationTime = DateTime.Now,
                    ModificationTime = DateTime.Now,
                    State = 0,
                    IsModified = true
                };
                break;
                
            case "Users":
                newRow = new UserRowViewModel
                {
                    UserId = TableData.Count + 1,
                    Username = "NewUser",
                    Role = 0,
                    IsActive = true,
                    CreationTime = DateTime.Now,
                    IsModified = true
                };
                break;
                
            case "Nodes":
                newRow = new NodeRowViewModel
                {
                    NodeId = $"Node{TableData.Count + 1}",
                    Address = "localhost:5000",
                    State = 0,
                    LastSeen = DateTime.Now,
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
        IsEditing = true;
        
        StatusMessage = $"Added new row to {SelectedTable.DisplayName}. Don't forget to save changes.";
        SaveChangesCommand.NotifyCanExecuteChanged();
    }
    
    private async Task BackupDatabaseAsync()
    {
        if (_storageClient == null)
            return;
            
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
            DefaultExt = ".db",
            FileName = $"node_db_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db"
        };
        
        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = "Backing up database...";
            
            try
            {
                var request = new AdminBackupDatabaseRequest
                {
                    OutputPath = dialog.FileName
                };
                
                var response = await _storageClient.BackupDatabaseAsync(request);
                
                if (response.Success)
                {
                    StatusMessage = $"Database successfully backed up to {dialog.FileName}";
                    MessageBox.Show($"Database backup completed successfully.\nLocation: {dialog.FileName}", 
                        "Backup Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = $"Error backing up database: {response.ErrorMessage}";
                    MessageBox.Show($"Failed to backup database: {response.ErrorMessage}", 
                        "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (RpcException ex)
            {
                StatusMessage = $"gRPC Error: {ex.Status.Detail}";
                MessageBox.Show($"A communication error occurred: {ex.Status.Detail}", 
                    "gRPC Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"An unexpected error occurred: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
    */
}