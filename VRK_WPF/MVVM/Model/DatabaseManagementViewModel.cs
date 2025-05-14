using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel.AdminViewModels;

namespace VRK_WPF.MVVM.Model
{
    public partial class DatabaseManagementViewModel : ObservableObject
    {
        private readonly AdminDatabaseService? _databaseService;
        
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
        
        public DatabaseManagementViewModel()
        {
            try
            {
                // Try to find the database file and create a connection to it
                string dbPath = FindDatabaseFile();
                if (!string.IsNullOrEmpty(dbPath))
                {
                    _databaseService = new AdminDatabaseService($"Data Source={dbPath}");
                    StatusMessage = $"Connected to database: {dbPath}";
                }
                else
                {
                    StatusMessage = "Database file not found. Please connect to a node first.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error initializing database service: {ex.Message}";
            }
            
            // Initialize available tables
            AvailableTables.Add(new TableViewModel { TableName = "Files", DisplayName = "Files Metadata", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Chunks", DisplayName = "Chunks Metadata", CanEdit = false });
            AvailableTables.Add(new TableViewModel { TableName = "ChunkLocations", DisplayName = "Chunk Locations", CanEdit = false });
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
        
        private string FindDatabaseFile()
        {
            var possiblePaths = new[] 
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "node_storage.db"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Data", "node_storage.db"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VKR_Network", "Data", "node_storage.db")
            };
            
            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            return string.Empty;
        }
        
        partial void OnSelectedTableChanged(TableViewModel? oldValue, TableViewModel? newValue)
        {
            if (newValue != null)
            {
                TableData.Clear();
                
                RefreshTableDataAsync().ConfigureAwait(false);
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
        
        public RelayCommand RefreshDataCommand { get; }
        public RelayCommand SaveChangesCommand { get; }
        public RelayCommand DeleteRowCommand { get; }
        public RelayCommand AddRowCommand { get; }
        public RelayCommand BackupDatabaseCommand { get; }
        
        private bool CanRefreshData() => !IsLoading && _databaseService != null;
        private bool CanSaveChanges() => IsEditing && SelectedTable?.CanEdit == true && !IsLoading && _databaseService != null;
        private bool CanDeleteRow() => SelectedRow != null && SelectedTable?.CanEdit == true && !IsLoading && _databaseService != null;
        private bool CanAddRow() => SelectedTable?.CanEdit == true && !IsLoading && _databaseService != null;
        private bool CanBackupDatabase() => !IsLoading && _databaseService != null;
        
        private async Task RefreshTableDataAsync()
        {
            if (SelectedTable == null || _databaseService == null)
                return;
                
            IsLoading = true;
            StatusMessage = $"Loading {SelectedTable.DisplayName} data...";
            
            try
            {
                TableData.Clear();
                
                var results = await _databaseService.GetTableDataAsync(SelectedTable);
                
                foreach (var item in results)
                {
                    TableData.Add(item);
                }
                
                StatusMessage = $"Loaded {TableData.Count} rows from {SelectedTable.DisplayName}.";
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
            if (SelectedTable == null || _databaseService == null)
                return;
                
            IsLoading = true;
            StatusMessage = $"Saving changes to {SelectedTable.DisplayName}...";
            
            try
            {
                // Find modified rows
                var modifiedRows = TableData.OfType<IModifiableRow>()
                    .Where(r => r.IsModified)
                    .Cast<object>()
                    .ToList();
                
                if (!modifiedRows.Any())
                {
                    StatusMessage = "No changes to save.";
                    IsLoading = false;
                    return;
                }
                
                bool success = await _databaseService.UpdateTableRowsAsync(SelectedTable, modifiedRows);
                
                if (success)
                {
                    StatusMessage = $"Successfully saved {modifiedRows.Count} changes to {SelectedTable.DisplayName}.";
                    
                    // Reset modified flags
                    foreach (var item in TableData.OfType<IModifiableRow>())
                    {
                        item.IsModified = false;
                    }
                    
                    IsEditing = false;
                }
                else
                {
                    StatusMessage = "Error saving changes.";
                    MessageBox.Show("Failed to save changes.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            if (SelectedRow == null || SelectedTable == null || _databaseService == null)
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
                bool success = await _databaseService.DeleteRowAsync(SelectedTable, SelectedRow);
                
                if (success)
                {
                    StatusMessage = $"Successfully deleted record from {SelectedTable.DisplayName}.";
                    
                    // Remove from local collection
                    TableData.Remove(SelectedRow);
                }
                else
                {
                    StatusMessage = "Error deleting record.";
                    MessageBox.Show("Failed to delete record.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
            if (_databaseService == null)
                return;
                
            var dialog = new SaveFileDialog
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
                    bool success = await _databaseService.BackupDatabaseAsync(dialog.FileName);
                    
                    if (success)
                    {
                        StatusMessage = $"Database successfully backed up to {dialog.FileName}";
                        MessageBox.Show($"Database backup completed successfully.\nLocation: {dialog.FileName}", 
                            "Backup Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        StatusMessage = "Error backing up database.";
                        MessageBox.Show("Failed to backup database.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                    MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }
    }
}