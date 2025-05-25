using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using VKR_Core.Enums;
using VKR_Node.Configuration;
using VKR_Node.Persistance;
using VKR_Node.Persistance.Entities;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class DatabaseManagementViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<NodeDatabaseInfo> _availableNodeDatabases = new();
        
        [ObservableProperty]
        private NodeDatabaseInfo? _selectedNodeDatabase;
        
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
        private string _statusMessage = "Готов";
        
        [ObservableProperty]
        private bool _isEditing;
        
        [ObservableProperty]
        private string _searchText = string.Empty;
        
        private NodeDbContext? _dbContext;
        
        public ICollectionView TableDataView { get; }
        
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
            AvailableTables.Add(new TableViewModel { TableName = "Files", DisplayName = "Данные файлов", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Chunks", DisplayName = "Данные фрагментов", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "ChunkLocations", DisplayName = "Местоположение фрагментов", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Nodes", DisplayName = "Состояние узлов", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Users", DisplayName = "Пользователи", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Logs", DisplayName = "Журнал событий", CanEdit = false });
            
            //SelectedTable = AvailableTables.FirstOrDefault();
        }
        
        private void RefreshNodeList()
        {
            IsLoading = true;
            StatusMessage = "Поиск баз данных узла...";
            
            try
            {
                AvailableNodeDatabases.Clear();
                
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
                        foreach (var file in Directory.GetFiles(basePath, "node_*.db"))
                        {
                            var fileName = Path.GetFileName(file);
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
                    StatusMessage = $"Найдено {AvailableNodeDatabases.Count} баз данных узла";
                }
                else
                {
                    StatusMessage = "Не найдено баз данных. Используйте операцию открытия файла базы данных.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка поиск базы данных: {ex.Message}";
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
                var match = System.Text.RegularExpressions.Regex.Match(fileName, @"node[_-](\w+)\.db$");
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }
            catch
            {
            }
            
            return Path.GetFileNameWithoutExtension(fileName).Replace("node_", "").Replace("node-", "");
        }
        
        private void OpenDatabase()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = ".db",
                Title = "Открытие базы данных"
            };
            
            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Открытие базы данных...";
                
                try
                {
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
                    
                    StatusMessage = $"Открыта база данных узла {nodeId}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка открытия базы данных: {ex.Message}";
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
                _dbContext?.Dispose();
                _dbContext = null;
                
                TableData.Clear();
                StatusMessage = "База данных не выбрана";
            }
            
            RefreshDataCommand.NotifyCanExecuteChanged();
            SaveChangesCommand.NotifyCanExecuteChanged();
            DeleteRowCommand.NotifyCanExecuteChanged();
            AddRowCommand.NotifyCanExecuteChanged();
            BackupDatabaseCommand.NotifyCanExecuteChanged();
        }
        
        private void ConnectToDatabase(string dbPath)
        {
            IsLoading = true;
            StatusMessage = $"Подключение к базе данных...";
            
            try
            {
                _dbContext?.Dispose();
                
                var optionsBuilder = new DbContextOptionsBuilder<NodeDbContext>();
                optionsBuilder.UseSqlite($"Data Source={dbPath}");
                optionsBuilder.EnableSensitiveDataLogging();
                
                var dbOptions = Options.Create(new DatabaseOptions 
                { 
                    DatabasePath = dbPath,
                    ConnectionString = $"Data Source={dbPath}",
                    HasExplicitConnectionString = false
                });
                
                _dbContext = new NodeDbContext(optionsBuilder.Options, dbOptions);
                
                
                TableData.Clear();
                
                if (SelectedTable != null)
                {
                    _ = RefreshTableDataAsync();
                }
                else
                {
                    StatusMessage = "База данных подключена. Выберите таблицу для просмотра.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка подключени к базе данных: {ex.Message}";
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
            StatusMessage = $"Загрузка данных {SelectedTable.DisplayName}...";
            
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
                        StatusMessage = $"Неизвестная таблица: {SelectedTable.TableName}";
                        break;
                }
                
                StatusMessage = $"Загружено {TableData.Count} строк из {SelectedTable.DisplayName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки таблицы: {ex.Message}";
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
            StatusMessage = "Загрузка данных таблицы Файлов"; 

            try
            {
                if (_dbContext == null || !_dbContext.Database.CanConnect())
                {
                    StatusMessage = "Контекст базы данных равен null или невозможно подключиться"; 
                    return;
                }

                var files = await _dbContext.FilesMetadata
                    .AsNoTracking() 
                    .OrderBy(f => f.FileName)
                    .Take(1000) 
                    .ToListAsync();

                StatusMessage = $"Получено {files.Count} записей из таблицы FilesMetadata"; 

                if (files.Count > 0)
                {
                    var first = files[0];
                    StatusMessage = $"Пример записи - FileId: {first.FileId}, FileName: {first.FileName}, Size: {first.FileSize}"; // Translated
                }

                // foreach (var file in files)
                // {
                //     var viewModel = new FileRowViewModel
                //     {
                //         FileId = file.FileId,
                //         FileName = file.FileName,
                //         FileSize = file.FileSize,
                //         CreationTime = file.CreationTime,
                //         ModificationTime = file.ModificationTime,
                //         ContentType = file.ContentType,
                //         ChunkSize = file.ChunkSize,
                //         TotalChunks = file.TotalChunks,
                //         State = file.State
                //     };
                //
                //     TableData.Add(viewModel);
                // }
                var viewModels = files.Select(file => new FileRowViewModel
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
                }).ToList();
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    TableData.Clear();
                    foreach (var vm in viewModels)
                    {
                        TableData.Add(vm);
                    }
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error in LoadFilesDataAsync: {ex}";
                throw;
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
            StatusMessage = "Просмотр логов пока не реализован"; // Translated
        }

        private async Task SaveChangesAsync()
        {
            if (_dbContext == null || SelectedTable == null || !SelectedTable.CanEdit)
                return;

            IsLoading = true;
            StatusMessage = $"Сохранение изменений в {SelectedTable.DisplayName}..."; // Translated

            try
            {
                var modifiedRows = TableData.OfType<IModifiableRow>().Where(r => r.IsModified).ToList();

                if (!modifiedRows.Any())
                {
                    StatusMessage = "Нет изменений для сохранения."; // Translated
                    IsLoading = false;
                    return;
                }

                using var transaction = await _dbContext.Database.BeginTransactionAsync();

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
                        throw new InvalidOperationException($"Невозможно сохранить изменения в {SelectedTable.TableName}"); // Translated
                }

                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                foreach (var row in modifiedRows)
                {
                    row.IsModified = false;
                }

                StatusMessage = $"Сохранено {modifiedRows.Count} изменений в {SelectedTable.DisplayName}."; // Translated
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка сохранения изменений: {ex.Message}"; // Translated
                MessageBox.Show($"Ошибка сохранения изменений: {ex.Message}", "Ошибка сохранения изменений", MessageBoxButton.OK, MessageBoxImage.Error); // Translated
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

            var result = MessageBox.Show($"Вы уверены, что хотите удалить эту запись из таблицы {SelectedTable.TableName}?", // Translated
                "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Warning); // Translated

            if (result != MessageBoxResult.Yes)
                return;

            IsLoading = true;
            StatusMessage = $"Удаление записи из {SelectedTable.DisplayName}..."; // Translated

            try
            {
                bool deleted = false;

                using var transaction = await _dbContext.Database.BeginTransactionAsync();

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
                        throw new InvalidOperationException($"Невозможно удалить из {SelectedTable.TableName}"); // Translated
                }

                if (deleted)
                {
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    TableData.Remove(SelectedRow);

                    StatusMessage = $"Запись удалена из {SelectedTable.DisplayName}."; // Translated
                }
                else
                {
                    await transaction.RollbackAsync();
                    StatusMessage = "Операция удаления не удалась"; // Translated
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка удаления записи: {ex.Message}"; // Translated
                MessageBox.Show($"Ошибка удаления записи: {ex.Message}", "Ошибка удаления записи", MessageBoxButton.OK, MessageBoxImage.Error); // Translated
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
                        MessageBox.Show("Невозможно создать местоположение фрагмента без существующих фрагментов.", // Translated
                            "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning); // Translated
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
                    MessageBox.Show($"Добавление новых строк в {SelectedTable.TableName} в настоящее время не поддерживается.", // Translated
                        "Не поддерживается", MessageBoxButton.OK, MessageBoxImage.Information); // Translated
                    return;
            }

            TableData.Add(newRow);
            SelectedRow = newRow;

            StatusMessage = $"Добавлена новая строка в {SelectedTable.DisplayName}. Не забудьте сохранить изменения."; // Translated
            SaveChangesCommand.NotifyCanExecuteChanged();
        }

        private async Task BackupDatabaseAsync()
        {
            if (_dbContext == null || SelectedNodeDatabase == null)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "База данных SQLite (*.db)|*.db|Все файлы (*.*)|*.*", // Translated
                DefaultExt = ".db",
                FileName = $"backup_{Path.GetFileName(SelectedNodeDatabase.DbPath)}"
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Создание резервной копии базы данных..."; // Translated

                try
                {
                    await _dbContext.Database.CloseConnectionAsync();

                    using var sourceConnection = new SqliteConnection($"Data Source={SelectedNodeDatabase.DbPath}");
                    await sourceConnection.OpenAsync();

                    using var destinationConnection = new SqliteConnection($"Data Source={dialog.FileName}");
                    await destinationConnection.OpenAsync();

                    sourceConnection.BackupDatabase(destinationConnection);

                    StatusMessage = $"Резервная копия базы данных успешно создана в {dialog.FileName}"; // Translated

                    MessageBox.Show($"Резервное копирование базы данных успешно завершено.\nРасположение: {dialog.FileName}", // Translated
                        "Резервное копирование успешно", MessageBoxButton.OK, MessageBoxImage.Information); // Translated

                    ConnectToDatabase(SelectedNodeDatabase.DbPath);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка создания резервной копии базы данных: {ex.Message}"; // Translated
                    MessageBox.Show($"Ошибка создания резервной копии базы данных: {ex.Message}",
                        "Ошибка резервного копирования", MessageBoxButton.OK, MessageBoxImage.Error); // Translated
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

    public class NodeDatabaseInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string DbPath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }

        public override string ToString()
        {
            return $"Узел {NodeId} (Последнее изменение: {LastModified:g})"; // Translated
        }
    }
}