using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VKR_Core.Enums;
using VKR_Node.Persistance;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class DatabaseManagementViewModel : ObservableObject, IDisposable
    {
        private NodeDbContext? _dbContext;
        private ICollectionView? _currentView;
        private readonly Dictionary<string, Type> _tableTypeMap;
        private AdminDatabaseService? _adminDbService;

        [ObservableProperty]
        private ObservableCollection<NodeDatabaseInfo> _availableNodeDatabases = new();
        
        [ObservableProperty]
        private NodeDatabaseInfo? _selectedNodeDatabase;
        
        [ObservableProperty]
        private ObservableCollection<TableViewModel> _availableTables = new();
        
        [ObservableProperty]
        private TableViewModel? _selectedTable;
        
        [ObservableProperty]
        private IList? _currentTableData;
        
        [ObservableProperty]
        private object? _selectedRow;
        
        [ObservableProperty]
        private bool _isLoading;
        
        [ObservableProperty]
        private string _statusMessage = "Готов";
        
        [ObservableProperty]
        private string _searchText = string.Empty;
        
        [ObservableProperty]
        private bool _hasUnsavedChanges;
        
        public DataGrid? DataGridControl { get; set; }
        
        public ICollectionView? CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }
        
        public RelayCommand RefreshNodeListCommand { get; set; }
        public RelayCommand RefreshDataCommand { get; set; }
        public RelayCommand SaveChangesCommand { get; set; }
        public RelayCommand DeleteRowCommand { get; set; }
        public RelayCommand AddRowCommand { get; set; }
        public RelayCommand BackupDatabaseCommand { get; set; }
        public RelayCommand OpenDatabaseCommand { get; set; }

        public DatabaseManagementViewModel()
        {
            _tableTypeMap = new Dictionary<string, Type>
            {
                ["Files"] = typeof(ObservableCollection<FileRowViewModel>),
                ["Chunks"] = typeof(ObservableCollection<ChunkRowViewModel>),
                ["ChunkLocations"] = typeof(ObservableCollection<ChunkLocationRowViewModel>),
                ["Nodes"] = typeof(ObservableCollection<NodeRowViewModel>),
                ["Users"] = typeof(ObservableCollection<UserRowViewModel>),
                ["Logs"] = typeof(ObservableCollection<LogRowViewModel>)
            };
            
            InitializeCommands();
            InitializeTableList();
            if (AvailableTables.Any())
            {
                SelectedTable = AvailableTables.FirstOrDefault(); 
            }
            RefreshNodeList();
        }
        
        private void InitializeCommands()
        {
            RefreshNodeListCommand = new RelayCommand(RefreshNodeList);
            RefreshDataCommand = new RelayCommand(async () => await RefreshTableDataAsync(), CanRefreshData);
            SaveChangesCommand = new RelayCommand(async () => await SaveChangesAsync(), CanSaveChanges);
            DeleteRowCommand = new RelayCommand(async () => await DeleteRowAsync(), CanDeleteRow);
            AddRowCommand = new RelayCommand(AddNewRow, CanAddRow);
            BackupDatabaseCommand = new RelayCommand(async () => await BackupDatabaseAsync(), CanBackupDatabase);
            OpenDatabaseCommand = new RelayCommand(OpenDatabase);
        }
        
        private void InitializeTableList()
        {
            AvailableTables.Clear();
            AvailableTables.Add(new TableViewModel { TableName = "Files", DisplayName = "Файлы", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Chunks", DisplayName = "Фрагменты", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "ChunkLocations", DisplayName = "Расположение фрагментов", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Nodes", DisplayName = "Узлы", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Users", DisplayName = "Пользователи", CanEdit = true });
            AvailableTables.Add(new TableViewModel { TableName = "Logs", DisplayName = "Журнал", CanEdit = false });
        }
        
        private void RefreshNodeList()
        {
            IsLoading = true;
            StatusMessage = "Поиск баз данных узлов...";
            
            try
            {
                AvailableNodeDatabases.Clear();
                
                // Expanded search paths to find database files
                var searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Data"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VKR_Network", "Data"),
                    "C:\\VKR_Network\\Storage\\Data",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\Data"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\Data"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VKR_Network", "Data"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VKR_Network", "Data"),
                    "C:\\Data", 
                    "D:\\Data"
                };
                
                foreach (var basePath in searchPaths.Where(p => Directory.Exists(p)))
                {
                    var dbFiles = Directory.GetFiles(basePath, "*.db", SearchOption.AllDirectories);
                    
                    foreach (var file in dbFiles)
                    {
                        var fileName = Path.GetFileName(file);
                        var nodeId = ExtractNodeIdFromFileName(fileName);
                        
                        if (!AvailableNodeDatabases.Any(db => db.DbPath == file))
                        {
                            AvailableNodeDatabases.Add(new NodeDatabaseInfo
                            {
                                NodeId = nodeId,
                                DbPath = file,
                                LastModified = File.GetLastWriteTime(file)
                            });
                        }
                    }
                }
                
                StatusMessage = AvailableNodeDatabases.Count > 0 
                    ? $"Найдено {AvailableNodeDatabases.Count} баз данных" 
                    : "Базы данных не найдены";
                
                // If no databases were found, suggest manual selection
                if (AvailableNodeDatabases.Count == 0)
                {
                    MessageBox.Show("Не удалось найти базы данных автоматически. Пожалуйста, выберите файл базы данных вручную.", 
                        "Базы данных не найдены", MessageBoxButton.OK, MessageBoxImage.Information);
                    OpenDatabase();
                }
                else if (AvailableNodeDatabases.Count > 0)
                {
                    SelectedNodeDatabase = AvailableNodeDatabases[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private string ExtractNodeIdFromFileName(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            return name.Replace("node_storage", "node").Replace("_storage", "");
        }
        
        private void OpenDatabase()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|All Files (*.*)|*.*",
                DefaultExt = ".db",
                Title = "Выберите базу данных"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var dbInfo = new NodeDatabaseInfo
                {
                    NodeId = ExtractNodeIdFromFileName(dialog.FileName),
                    DbPath = dialog.FileName,
                    LastModified = File.GetLastWriteTime(dialog.FileName)
                };
                
                if (!AvailableNodeDatabases.Any(db => db.DbPath == dbInfo.DbPath))
                {
                    AvailableNodeDatabases.Add(dbInfo);
                }
                
                SelectedNodeDatabase = dbInfo;
            }
        }
        
        partial void OnSelectedNodeDatabaseChanged(NodeDatabaseInfo? value)
        {
            if (value != null)
            {
                ConnectToDatabase(value.DbPath);
            }
            else
            {
                DisconnectDatabase();
            }
        }
        
        partial void OnSelectedTableChanged(TableViewModel? oldValue, TableViewModel? newValue)
        {
            if (newValue != null && _adminDbService != null)
            {
                // Reset the current view to null before refreshing to ensure we get fresh columns
                CurrentView = null;
                CurrentTableData = null;
                
                // Clear the auto-generated columns if we have a reference to the DataGrid
                if (DataGridControl != null)
                {
                    DataGridControl.Columns.Clear();
                    DataGridControl.AutoGenerateColumns = true;
                }
                
                // Now load the data for the new table
                RefreshTableDataAsync().ConfigureAwait(false);
            }
            
            RefreshDataCommand.NotifyCanExecuteChanged();
            SaveChangesCommand.NotifyCanExecuteChanged();
            DeleteRowCommand.NotifyCanExecuteChanged();
            AddRowCommand.NotifyCanExecuteChanged();
        }
        
        partial void OnSearchTextChanged(string value)
        {
            CurrentView?.Refresh();
        }
        
        private void ConnectToDatabase(string dbPath)
        {
            try
            {
                DisconnectDatabase();
                
                string connectionString = $"Data Source={dbPath}";
                _adminDbService = new AdminDatabaseService(connectionString);
                
                StatusMessage = $"База данных {Path.GetFileName(dbPath)} подключена";

                if (SelectedTable != null)
                {
                    RefreshTableDataAsync().ConfigureAwait(false);
                }
                else if (AvailableTables.Any())
                {
                    SelectedTable = AvailableTables.First();
                }
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                _adminDbService = null;
                StatusMessage = $"Ошибка подключения: {ex.Message}";
                MessageBox.Show($"Ошибка подключения к базе данных: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateCommandStates();
                
                CurrentTableData = null; 
                CurrentView = null;
                UpdateCommandStates();
            }
        }
        
        private void DisconnectDatabase()
        {
            CurrentView = null;
            CurrentTableData = null;
            _adminDbService = null;
            UpdateCommandStates();
        }
        
        private bool CanRefreshData() => !IsLoading && _adminDbService != null && SelectedTable != null;
        private bool CanSaveChanges() => !IsLoading && _adminDbService != null && SelectedTable?.CanEdit == true && HasUnsavedChanges;
        private bool CanDeleteRow() => !IsLoading && _adminDbService != null && SelectedTable?.CanEdit == true && SelectedRow != null;
        private bool CanAddRow() => !IsLoading && _adminDbService != null && SelectedTable?.CanEdit == true;
        private bool CanBackupDatabase() => !IsLoading && _adminDbService != null;
        
        private async Task RefreshTableDataAsync()
        {
            if (_adminDbService == null || SelectedTable == null)
            {
                StatusMessage = _adminDbService == null ? "Сервис базы данных не инициализирован." : "Таблица не выбрана.";
                CurrentTableData = null;
                CurrentView = null;
                UpdateCommandStates();
                return;
            }

            IsLoading = true;
            StatusMessage = $"Загрузка {SelectedTable.DisplayName}...";

            try
            {
                CurrentTableData = null; 
                CurrentView = null;
                SelectedRow = null;

                var data = await _adminDbService.GetTableDataAsync(SelectedTable);
                CurrentTableData = data;

                if (CurrentTableData != null)
                {
                    foreach (var item in CurrentTableData.OfType<INotifyPropertyChanged>())
                    {
                        item.PropertyChanged -= OnRowPropertyChanged; // Avoid multiple subscriptions
                        item.PropertyChanged += OnRowPropertyChanged;
                    }

                    CurrentView = CollectionViewSource.GetDefaultView(CurrentTableData);
                    if (CurrentView != null) CurrentView.Filter = FilterData;
                    StatusMessage = $"Загружено {CurrentTableData.Count} записей";
                    
                    // Force UI refresh if we have access to the DataGrid
                    if (DataGridControl != null)
                    {
                        DataGridControl.ItemsSource = null;
                        DataGridControl.Columns.Clear();
                        DataGridControl.AutoGenerateColumns = true;
                        DataGridControl.ItemsSource = CurrentTableData;
                    }
                }
                else
                {
                    StatusMessage = $"Данные для таблицы {SelectedTable.DisplayName} не загружены.";
                }
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки данных: {ex.Message}";
                MessageBox.Show($"Ошибка загрузки данных для таблицы {SelectedTable.DisplayName}: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                CurrentTableData = new ObservableCollection<object>(); // Ensure it's an empty collection on error
                CurrentView = CollectionViewSource.GetDefaultView(CurrentTableData);
            }
            finally
            {
                IsLoading = false;
                UpdateCommandStates();
            }
        }
        
        private bool FilterData(object item)
        {
            if (string.IsNullOrWhiteSpace(SearchText) || item == null)
                return true;
                
            var type = item.GetType();
            var properties = type.GetProperties()
                .Where(p => p.CanRead && (p.PropertyType == typeof(string) || 
                                         p.PropertyType == typeof(int) || 
                                         p.PropertyType == typeof(long)));
                
            foreach (var prop in properties)
            {
                var value = prop.GetValue(item)?.ToString();
                if (!string.IsNullOrEmpty(value) && 
                    value.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            
            return false;
        }
        
        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IModifiableRow.IsModified))
            {
                HasUnsavedChanges = CurrentTableData?.Cast<IModifiableRow>().Any(r => r.IsModified) ?? false;
                UpdateCommandStates();
            }
        }
        
        private void UpdateCommandStates()
        {
            RefreshDataCommand.NotifyCanExecuteChanged();
            SaveChangesCommand.NotifyCanExecuteChanged();
            DeleteRowCommand.NotifyCanExecuteChanged();
            AddRowCommand.NotifyCanExecuteChanged();
            BackupDatabaseCommand.NotifyCanExecuteChanged();
        }
        
        private async Task SaveChangesAsync()
        {
            if (_adminDbService == null || CurrentTableData == null || SelectedTable == null || !SelectedTable.CanEdit || !HasUnsavedChanges)
                return;

            IsLoading = true;
            StatusMessage = "Сохранение изменений...";

            try
            {
                var modifiedRows = CurrentTableData.OfType<IModifiableRow>()
                    .Where(r => r.IsModified)
                    .Cast<object>()
                    .ToList();

                if (modifiedRows.Any())
                {
                    bool success = await _adminDbService.UpdateTableRowsAsync(SelectedTable, modifiedRows);
                    if (success)
                    {
                        foreach (var row in modifiedRows.Cast<IModifiableRow>())
                        {
                            row.IsModified = false;
                        }
                        HasUnsavedChanges = false;
                        StatusMessage = $"Сохранено {modifiedRows.Count} изменений";
                    }
                    else
                    {
                        StatusMessage = "Не удалось сохранить изменения (сервис вернул false).";
                        MessageBox.Show(StatusMessage, "Ошибка сохранения", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else { StatusMessage = "Нет изменений для сохранения."; }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка сохранения: {ex.Message}";
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                UpdateCommandStates();
            }
        }
        
        private async Task DeleteRowAsync()
        {
            if (SelectedRow == null || _adminDbService == null || SelectedTable == null || !SelectedTable.CanEdit)
                return;

            var result = MessageBox.Show("Вы уверены, что хотите удалить эту запись?",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            IsLoading = true;
            StatusMessage = "Удаление записи...";

            try
            {
                bool success = await _adminDbService.DeleteRowAsync(SelectedTable, SelectedRow);
                if (success)
                {
                    CurrentTableData?.Remove(SelectedRow); // Assumes CurrentTableData is ObservableCollection<object>
                    SelectedRow = null;
                    StatusMessage = "Запись удалена";
                }
                else
                {
                    StatusMessage = "Не удалось удалить запись (сервис вернул false).";
                    MessageBox.Show(StatusMessage, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка удаления: {ex.Message}";
                MessageBox.Show($"Ошибка удаления: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                UpdateCommandStates();
            }
        }
        
        private void AddNewRow()
        {
            if (CurrentTableData == null || SelectedTable == null || !SelectedTable.CanEdit)
                return;
                
            BaseRowViewModel? newRow = null;
            
            switch (SelectedTable.TableName)
            {
                case "Files":
                    newRow = new FileRowViewModel
                    {
                        FileId = Guid.NewGuid().ToString(),
                        FileName = "Новый файл",
                        FileSize = 0,
                        CreationTime = DateTime.UtcNow,
                        ModificationTime = DateTime.UtcNow,
                        State = (int)FileStateCore.Unknown,
                        IsModified = true
                    };
                    break;
                    
                case "Users":
                    var dialog = new UserPasswordDialog();
                    if (dialog.ShowDialog() == true)
                    {
                        newRow = new UserRowViewModel
                        {
                            UserId = GetNextUserId(),
                            Username = dialog.Username,
                            NewPassword = dialog.Password,
                            Role = (int)UserRole.ITSpecialist,
                            IsActive = true,
                            CreationTime = DateTime.UtcNow,
                            IsModified = true
                        };
                    }
                    break;
                    
                case "Nodes":
                    newRow = new NodeRowViewModel
                    {
                        NodeId = $"Node{GetNextNodeNumber()}",
                        Address = "localhost:5000",
                        State = (int)NodeStateCore.Offline,
                        LastSeen = DateTime.UtcNow,
                        IsModified = true
                    };
                    break;
            }
            
            if (newRow != null)
            {
                newRow.PropertyChanged += OnRowPropertyChanged;
                CurrentTableData.Add(newRow);
                SelectedRow = newRow;
                HasUnsavedChanges = true;
                UpdateCommandStates();
            }
        }
        
        private int GetNextUserId()
        {
            if (CurrentTableData is ObservableCollection<UserRowViewModel> users && users.Any())
            {
                return users.Max(u => u.UserId) + 1;
            }
            return 1;
        }
        
        private int GetNextNodeNumber()
        {
            if (CurrentTableData is ObservableCollection<NodeRowViewModel> nodes && nodes.Any())
            {
                var numbers = nodes
                    .Select(n => System.Text.RegularExpressions.Regex.Match(n.NodeId, @"\d+"))
                    .Where(m => m.Success)
                    .Select(m => int.Parse(m.Value))
                    .DefaultIfEmpty(0);
                return numbers.Max() + 1;
            }
            return 1;
        }
        
        private async Task BackupDatabaseAsync()
        {
            if (_adminDbService == null || SelectedNodeDatabase == null) // Check _adminDbService
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db",
                DefaultExt = ".db",
                FileName = $"backup_{Path.GetFileNameWithoutExtension(SelectedNodeDatabase.DbPath)}_{DateTime.Now:yyyyMMdd_HHmmss}.db"
            };

            if (dialog.ShowDialog() == true)
            {
                IsLoading = true;
                StatusMessage = "Создание резервной копии...";
                try
                {
                    bool success = await _adminDbService.BackupDatabaseAsync(dialog.FileName);
                    if (success)
                    {
                        StatusMessage = "Резервная копия создана";
                        MessageBox.Show("Резервная копия успешно создана", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        StatusMessage = "Не удалось создать резервную копию (сервис вернул false).";
                        MessageBox.Show(StatusMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка создания резервной копии: {ex.Message}";
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }
        
        public void Dispose()
        {
            DisconnectDatabase();
        }
    }
    
    public class NodeDatabaseInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string DbPath { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        
        public override string ToString() => $"{NodeId} ({LastModified:g})";
    }
}