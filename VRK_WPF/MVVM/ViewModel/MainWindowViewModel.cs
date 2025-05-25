using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using VKR.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using VRK_WPF.MVVM.View;

namespace VRK_WPF.MVVM.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private StorageService.StorageServiceClient? _storageClient;
        private GrpcChannel? _currentChannel;
        private CancellationTokenSource? _uploadCts;
        private CancellationTokenSource? _downloadCts;

        [ObservableProperty]
        private ObservableCollection<FileViewModel> _files = new();

        [ObservableProperty]
        private ObservableCollection<NodeViewModel> _nodes = new();

        [ObservableProperty]
        private bool _isSimulationTabEnabled = true;

        [ObservableProperty]
        private string _simulationDuration = "30 секунд";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private string _targetNodeAddress = "http://localhost:5005";

        [ObservableProperty]
        private string _connectionStatus = "Отключено";

        [ObservableProperty]
        private Brush _connectionStatusColor = Brushes.OrangeRed;

        [ObservableProperty]
        private ObservableCollection<NodeViewModel> _simulationNodes = new();

        [ObservableProperty]
        private ObservableCollection<SimulationFileStatusViewModel> _simulationFileStatuses = new();

        [ObservableProperty]
        private ObservableCollection<SimulationChunkDistributionViewModel> _simulationChunkDistribution = new();

        [ObservableProperty]
        private string _simulationLog = "";

        [ObservableProperty]
        private string _simulationStatus = "Готово к симуляции";

        [ObservableProperty]
        private Brush _simulationStatusColor = Brushes.Black;

        [ObservableProperty]
        private double _simulationProgress;

        [ObservableProperty]
        private bool _isSimulationInProgress;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DisableSelectedNodesCommand))]
        private bool _canSimulateNodeFailure;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RestoreAllNodesCommand))]
        private bool _canRestoreNodes;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UploadFileCommand))]
        private string? _selectedFilePath;

        [ObservableProperty]
        private string? _selectedFileName;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UploadFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(DownloadFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteFileCommand))]
        private bool _isUploading;

        [ObservableProperty]
        private double _uploadProgress;

        [ObservableProperty]
        private string _uploadStatus = "Готово";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UploadFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(DownloadFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteFileCommand))]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _downloadStatus = "Готово";

        [ObservableProperty]
        private string _statusBarText = "Готов";

        private FileViewModel? _selectedFile;
        public FileViewModel? SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetProperty(ref _selectedFile, value))
                {
                    DownloadFileCommand.NotifyCanExecuteChanged();
                    DeleteFileCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(Debug_IsUploadPossible));
                }
            }
        }

        [ObservableProperty]
        private string _currentUserName = string.Empty;

        [ObservableProperty]
        private string _currentUserRole = string.Empty;

        [ObservableProperty]
        private double _settingCpuUsage;

        [ObservableProperty]
        private string _settingMemoryUsage = "Н/Д";

        [ObservableProperty]
        private string _settingDiskSpace = "Н/Д";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RefreshNodeStatusCommand))]
        private bool _isNodeStatusRefreshing;
        public ICollectionView FilesView { get; }
        public ICollectionView NodesView { get; }

        #region Node Settings Properties

        [ObservableProperty]
        private string _settingNodeId = "Н/Д";

        [ObservableProperty]
        private string _settingListenAddress = "Н/Д";

        [ObservableProperty]
        private string _settingStorageBasePath = "Н/Д";

        [ObservableProperty]
        private int _settingReplicationFactor;

        [ObservableProperty]
        private int _settingDefaultChunkSize;

        [ObservableProperty]
        private string _settingsErrorMessage = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
        [NotifyCanExecuteChangedFor(nameof(RefreshSettingsCommand))]
        private bool _isSettingsLoading;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
        private bool _hasSettingsError;

        public bool IsSettingsInteractionEnabled => _storageClient != null && !IsSettingsLoading && !HasSettingsError;

        #endregion

        public MainWindowViewModel(ILogger<MainWindowViewModel>? logger = null)
        {
            _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;

            FilesView = CollectionViewSource.GetDefaultView(Files);
            FilesView.SortDescriptions.Add(new SortDescription(nameof(FileViewModel.FileName), ListSortDirection.Ascending));

            NodesView = CollectionViewSource.GetDefaultView(Nodes);
            NodesView.SortDescriptions.Add(new SortDescription(nameof(NodeViewModel.NodeId), ListSortDirection.Ascending));

            UpdateConnectionStatus("Отключено", Brushes.OrangeRed);
            UpdateStatusBar("Готов. Введите адрес узла и подключайтесь.");

            if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                TargetNodeAddress = "http://design-time:5000";
                ConnectionStatus = "Режим дизайна";
                ConnectionStatusColor = Brushes.Gray;
                Files.Add(new FileViewModel { FileId = "design-1", FileName = "DesignFile1.txt", FileSize = 1024, CreationTime = DateTime.Now, State = "Доступен" });
                Nodes.Add(new NodeViewModel { NodeId = "Узел1-Дизайн", Address = "localhost:5001", Status = "Онлайн", StatusDetails = "Режим дизайна" });
            }
        }

        private void UpdateStatusBarWithUserInfo()
        {
            if (!string.IsNullOrEmpty(CurrentUserName))
            {
                StatusBarText = $"Пользователь: {CurrentUserName} | Готов к работе";
            }
            else
            {
                StatusBarText = "Готов";
            }
        }

        partial void OnCurrentUserNameChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                UpdateStatusBarWithUserInfo();
            }
        }

        public MainWindowViewModel()
        {
            FilesView = CollectionViewSource.GetDefaultView(Files);
            FilesView.SortDescriptions.Add(new SortDescription(nameof(FileViewModel.FileName), ListSortDirection.Ascending));

            NodesView = CollectionViewSource.GetDefaultView(Nodes);
            NodesView.SortDescriptions.Add(new SortDescription(nameof(NodeViewModel.NodeId), ListSortDirection.Ascending));

            UpdateConnectionStatus("Отключено", Brushes.OrangeRed);
            UpdateStatusBar("Готов. Введите адрес узла и подключайтесь.");
        }

        private void UpdateConnectionStatus(string status, Brush color)
        {
            ConnectionStatus = status;
            ConnectionStatusColor = color;
            StatusBarText = status;
        }

        private void UpdateSelectionStatus()
        {
            bool hasSelections = SimulationNodes.Any(n => n.IsSelected);
            CanSimulateNodeFailure = hasSelections && _storageClient != null;
        }

        public void OnNodeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionStatus();
        }

        private bool ConnectToNode()
        {
            _currentChannel?.Dispose();
            _currentChannel = null;
            _storageClient = null;
            UpdateConnectionStatus("Подключение...", Brushes.Orange);

            try
            {
                 if (string.IsNullOrWhiteSpace(TargetNodeAddress) || !Uri.TryCreate(TargetNodeAddress, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                 {
                     _logger.LogError("Неправильный формат адреса gRPC сервера: {Address}", TargetNodeAddress);
                     UpdateConnectionStatus($"Ошибка: Неправильный формат адреса gRPC сервера", Brushes.Red);
                     MessageBox.Show($"Неправильный формат адреса сервера: {TargetNodeAddress}\nПожалуйста, используйте http://host:port или https://host:port", "Ошибка подключения", MessageBoxButton.OK, MessageBoxImage.Error);
                     return false;
                 }

                 var channelOptions = new GrpcChannelOptions();

                _currentChannel = GrpcChannel.ForAddress(TargetNodeAddress, channelOptions);
                _storageClient = new StorageService.StorageServiceClient(_currentChannel);
                OnPropertyChanged(nameof(Debug_IsUploadPossible));
                RefreshSettingsCommand.NotifyCanExecuteChanged();
                _logger.LogInformation("gRPC клиент инициализирован по адресу: {Address}", TargetNodeAddress);
                UpdateConnectionStatus($"Подключено к {TargetNodeAddress}", Brushes.Green);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка инициализации клиента gRPC сервера по адресу: {Address}", TargetNodeAddress);
                UpdateConnectionStatus($"Ошибка подключения к {TargetNodeAddress}", Brushes.Red);
                MessageBox.Show($"Ошибка подключения по адресу {TargetNodeAddress}.\nУбедитесь, что сервер запущен\n\nError: {ex.Message}", "Ошибка подключения gRPC", MessageBoxButton.OK, MessageBoxImage.Error);
                _storageClient = null;
                _currentChannel = null;
                return false;
            }
        }

        private void UpdateStatusBar(string message) { StatusBarText = message; _logger.LogInformation("Статус: {Message}", message); }
        private bool CanExecuteUpload() => !string.IsNullOrEmpty(SelectedFilePath) && File.Exists(SelectedFilePath) && !IsUploading && !IsDownloading && _storageClient != null;

        public bool Debug_IsUploadPossible => !string.IsNullOrEmpty(SelectedFilePath) && File.Exists(SelectedFilePath) && !IsUploading && !IsDownloading && _storageClient != null;
        private bool CanExecuteDownload() => SelectedFile != null && !IsDownloading && !IsUploading &&_storageClient != null;
        private bool CanExecuteDelete() => SelectedFile != null && !IsUploading && !IsDownloading && _storageClient != null;
        private bool CanExecuteRefreshNodeStatus() => !IsNodeStatusRefreshing && _storageClient != null;
        private bool CanExecuteRefreshFilesList() => _storageClient != null;
        private bool CanExecuteConnect() => !string.IsNullOrWhiteSpace(TargetNodeAddress);

        [RelayCommand(CanExecute = nameof(CanExecuteConnect))]
        private async Task ConnectAsync()
        {
            _logger.LogInformation("Попытка подключения: {Address}", TargetNodeAddress);
            Files.Clear();
            Nodes.Clear();

            if (ConnectToNode())
            {
                await RefreshFilesListAsync();
                await RefreshNodeStatusAsync();
                await LoadSettingsAsync();
                await UpdateSimulationNodesAsync();
                await UpdateFileAndChunkStatusAsync();

                CanRestoreNodes = true;
                UpdateStatusBar($"Подключено к {TargetNodeAddress}. Конфигурация загружена.");
            }
            else
            {
                Nodes.Clear();
                SimulationNodes.Clear();
                SimulationFileStatuses.Clear();
                SimulationChunkDistribution.Clear();
                CanSimulateNodeFailure = false;
                CanRestoreNodes = false;
            }
        }



        private async Task UpdateFileAndChunkStatusAsync()
        {
            SimulationFileStatuses.Clear();
            SimulationChunkDistribution.Clear();

            try
            {
                var fileStatusRequest = new GetFileStatusesRequest();
                var fileStatusReply = await _storageClient.GetFileStatusesAsync(fileStatusRequest);

                foreach (var fileStatus in fileStatusReply.FileStatuses)
                {
                    SimulationFileStatuses.Add(new SimulationFileStatusViewModel
                    {
                        FileName = fileStatus.FileName,
                        Availability = fileStatus.IsAvailable ? "Доступен" : "Недоступен",
                        ReplicationStatus = $"{fileStatus.CurrentReplicationFactor}/{fileStatus.DesiredReplicationFactor}",
                        StatusColor = fileStatus.IsAvailable ? Brushes.Green : Brushes.Red
                    });
                }

                var chunkRequest = new GetChunkDistributionRequest();
                var chunkReply = await _storageClient.GetChunkDistributionAsync(chunkRequest);

                foreach (var chunk in chunkReply.ChunkDistributions)
                {
                    SimulationChunkDistribution.Add(new SimulationChunkDistributionViewModel
                    {
                        ChunkId = chunk.ChunkId,
                        FileName = chunk.FileName,
                        NodeLocations = string.Join(", ", chunk.NodeIds),
                        ReplicaCount = chunk.NodeIds.Count
                    });
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        [RelayCommand]
        private void SelectFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Выберите файл для загрузки",
                Filter = "Все файлы (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedFilePath = openFileDialog.FileName;
                SelectedFileName = Path.GetFileName(SelectedFilePath);
                 _logger.LogInformation("Файл выбран для загрузки: {FilePath}", SelectedFilePath);
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteUpload))]
        private async Task UploadFileAsync()
        {
            if (_storageClient == null || string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
            {
                MessageBox.Show("Клиент не инициализирован, либо файл не выбран или не существует", "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var configRequest = new GetNodeConfigurationRequest();
            var configReply = await _storageClient.GetNodeConfigurationAsync(configRequest);
            int preferredChunkSize = configReply.DefaultChunkSize;



            IsUploading = true;
            UploadProgress = 0;
            UploadStatus = "Начало загрузки...";
            _uploadCts = new CancellationTokenSource();
            UpdateStatusBar($"Загрузка {SelectedFileName}...");
            _logger.LogInformation("Загрузка началась для файла: {FilePath}", SelectedFilePath);

            AsyncClientStreamingCall<UploadFileRequest, UploadFileReply>? call = null;

            try
            {
                var fileInfo = new FileInfo(SelectedFilePath);
                long fileSize = fileInfo.Length;

                var metadata = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    ExpectedFileSize = fileSize,
                    ContentType = MimeMapping.MimeUtility.GetMimeMapping(fileInfo.Name),
                    CreationTime = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                _logger.LogInformation("Подготовлены метаданные. Ожидаемый размер: {FileSize}", fileSize);

                call = _storageClient.UploadFile(cancellationToken: _uploadCts.Token);

                await call.RequestStream.WriteAsync(new UploadFileRequest { Metadata = metadata });
                 _logger.LogDebug("Отправлены метаданные.");

                long totalBytesSent = 0;
                int chunkIndex = 0;
                int totalChunks = 0;

                if (preferredChunkSize <= 0)
                {
                    preferredChunkSize = 1 * 1024 * 1024; // 1 MB
                }

                if (fileSize > 100 * 1024 * 1024) // > 100 MB
                {
                    preferredChunkSize = 4 * 1024 * 1024; // 4 MB
                }
                if (fileSize > 1 * 1024 * 1024 * 1024) // > 1 GB
                {
                    preferredChunkSize = 32 * 1024 * 1024; // 16 MB
                }

                int bufferSize = preferredChunkSize;
                
                if (fileSize > 0 && bufferSize > 0)
                {
                    totalChunks = (int)Math.Ceiling((double)fileSize / bufferSize);
                }

                await using (var fileStream = File.OpenRead(SelectedFilePath))
                {
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;
                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, _uploadCts.Token)) > 0)
                    {
                         _uploadCts.Token.ThrowIfCancellationRequested();

                        var chunkData = ByteString.CopyFrom(buffer, 0, bytesRead);

                        var chunkId = $"chunk_{Guid.NewGuid()}_{chunkIndex}";
                        var chunk = new FileChunk
                        {
                            ChunkId = chunkId,
                            ChunkIndex = chunkIndex,
                            Data = chunkData,
                            Size = bytesRead
                        };

                        _logger.LogTrace("Отправка фрагмента: {Index}, размер: {Size}", chunkIndex, bytesRead);
                        
                        await call.RequestStream.WriteAsync(new UploadFileRequest { Chunk = chunk });

                        totalBytesSent += bytesRead;
                        chunkIndex++;

                        if (fileSize > 0)
                        {
                             UploadProgress = (double)totalBytesSent / fileSize * 100;
                             if (totalChunks > 0)
                             {
                                 UploadStatus = $"Загрузка фрагмента {chunkIndex}/{totalChunks}... ({UploadProgress:F1}%)";
                             }
                             else
                             {
                                 UploadStatus = $"Загрузка фрагмента {chunkIndex}... ({UploadProgress:F1}%)";
                             }
                        } else {
                             UploadProgress = 100;
                             UploadStatus = $"Загрузка пустого файла...";
                        }
                    }
                }
                _logger.LogInformation("Завершение отправки {ChunkCount} фрагментов. Отправлено байт: {TotalBytes}", chunkIndex, totalBytesSent);

                await call.RequestStream.CompleteAsync();

                UploadStatus = "Ожидание ответа сервера...";

                var response = await call.ResponseAsync;

                if (response.Success)
                {
                    UploadStatus = $"Загрузка завершена! ID файла: {response.FileId}";
                    UpdateStatusBar($"Успешная загрузка {SelectedFileName}");
                    await RefreshFilesListAsync();
                }
                else
                {
                    UploadStatus = $"Загрузка прервана: {response.Message}";
                    UpdateStatusBar($"Загрузка файла прервана {SelectedFileName}");
                    MessageBox.Show($"Сервер сообщил об ошибке при распределении:\n{response.Message}", "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "Ошибка gRPC во время загрузки: Статус={StatusCode}, Детали={Detail}", ex.StatusCode, ex.Status.Detail);
                UploadStatus = $"Ошибка gRPC: {ex.StatusCode}";
                UpdateStatusBar($"Не удалось загрузить {SelectedFileName} (Ошибка gRPC)");
                MessageBox.Show($"Произошла ошибка во время загрузки:\nСтатус: {ex.StatusCode}\nДетали: {ex.Status.Detail}", "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Загрузка отменена пользователем.");
                UploadStatus = "Загрузка отменена.";
                UpdateStatusBar($"Загрузка {SelectedFileName} отменена");
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Неожиданная ошибка во время загрузки.");
                UploadStatus = $"Ошибка: {ex.Message}";
                UpdateStatusBar($"Не удалось загрузить {SelectedFileName} (Ошибка)");
                MessageBox.Show($"Произошла неожиданная ошибка во время загрузки:\n{ex.Message}", "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsUploading = false;
                UploadProgress = 0;
                _uploadCts?.Dispose();
                _uploadCts = null;
                _logger.LogInformation("Операция загрузки завершена.");
            }
        }

        [RelayCommand]
        private void CancelUpload()
        {
            if (IsUploading && _uploadCts != null && !_uploadCts.IsCancellationRequested)
            {
                _logger.LogInformation("Попытка отменить загрузку.");
                _uploadCts.Cancel();
                UploadStatus = "Отмена загрузки...";
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDownload))]
        private async Task DownloadFileAsync()
        {
            if (_storageClient == null || SelectedFile == null)
            {
                 MessageBox.Show("Клиент gRPC не инициализирован или файл не выбран.", "Ошибка скачивания", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fileToDownload = SelectedFile;
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Сохранить скачанный файл как",
                FileName = fileToDownload.FileName ?? "downloaded_file"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            string savePath = saveFileDialog.FileName;
            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatus = "Начало скачивания...";
            _downloadCts = new CancellationTokenSource();
            UpdateStatusBar($"Скачивание {fileToDownload.FileName}...");

            string tempDownloadPath = savePath + ".tmp";

            try
            {
                var request = new DownloadFileRequest { FileId = fileToDownload.FileId };
                using var call = _storageClient.DownloadFile(request, cancellationToken: _downloadCts.Token);

                FileMetadata? fileMetadata = null;
                long totalBytesReceived = 0;
                long expectedFileSize = 0;
                int chunksReceived = 0;

                await using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write,
                                 FileShare.None, 4096, useAsync: true))
                {
                    await foreach (var reply in call.ResponseStream.ReadAllAsync(_downloadCts.Token))
                    {
                        if (reply.PayloadCase == DownloadFileReply.PayloadOneofCase.Metadata)
                        {
                            fileMetadata = reply.Metadata;
                            expectedFileSize = fileMetadata.FileSize;
                            DownloadStatus =
                                $"Получены метаданные, скачивание фрагментов... (Ожидаемый размер: {expectedFileSize} байт)";
                        }
                        else if (reply.PayloadCase == DownloadFileReply.PayloadOneofCase.Chunk)
                        {
                            if (fileMetadata == null)
                                throw new InvalidOperationException("Данные фрагмента получены до метаданных файла.");

                            var chunk = reply.Chunk;
                            await fileStream.WriteAsync(chunk.Data.Memory, _downloadCts.Token);
                            totalBytesReceived += chunk.Size;
                            chunksReceived++;

                            if (expectedFileSize > 0)
                            {
                                DownloadProgress = (double)totalBytesReceived / expectedFileSize * 100;
                                DownloadStatus =
                                    $"Скачивание фрагмента {chunksReceived}/{fileMetadata.TotalChunks}... ({DownloadProgress:F1}%)";
                            }
                            else
                            {
                                DownloadStatus =
                                    $"Скачивание фрагмента {chunksReceived}... (получено {totalBytesReceived} байт)";
                            }
                        }
                    }
                }

                if (expectedFileSize > 0 && totalBytesReceived != expectedFileSize)
                {
                    DownloadStatus = $"Скачивание завершено, но размер не совпадает! Ожидалось {expectedFileSize}, получено {totalBytesReceived}";
                    UpdateStatusBar($"Скачивание {fileToDownload.FileName} завершено с несовпадением размера");
                     MessageBox.Show($"Скачивание завершено, но полученный размер файла ({totalBytesReceived} байт) не соответствует ожидаемому ({expectedFileSize} байт).\nФайл может быть неполным или поврежденным.", "Предупреждение при скачивании", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                     if (File.Exists(savePath)) File.Delete(savePath);
                     File.Move(tempDownloadPath, savePath);

                    DownloadStatus = "Скачивание завершено!";
                    UpdateStatusBar($"Успешно скачан {fileToDownload.FileName}");
                }
            }
            catch (RpcException ex)
            {
                DownloadStatus = $"Ошибка gRPC: {ex.StatusCode}";
                UpdateStatusBar($"Не удалось скачать {fileToDownload.FileName} (Ошибка gRPC)");
                MessageBox.Show($"Произошла ошибка во время скачивания:\nСтатус: {ex.StatusCode}\nДетали: {ex.Status.Detail}", "Ошибка скачивания", MessageBoxButton.OK, MessageBoxImage.Error);
                if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch {} }
            }
            catch (OperationCanceledException)
            {
                DownloadStatus = "Скачивание отменено.";
                UpdateStatusBar($"Скачивание {fileToDownload.FileName} отменено");
                if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch {} }
            }
            catch (Exception ex)
            {
                DownloadStatus = $"Ошибка: {ex.Message}";
                UpdateStatusBar($"Не удалось скачать {fileToDownload.FileName} (Ошибка)");
                MessageBox.Show($"Произошла неожиданная ошибка во время скачивания:\n{ex.Message}", "Ошибка скачивания", MessageBoxButton.OK, MessageBoxImage.Error);
                if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch {} }
            }
            finally
            {
                IsDownloading = false;
                DownloadProgress = 0;
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }


        [RelayCommand]
        private void CancelDownload()
        {
            if (IsDownloading && _downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                _logger.LogInformation("Попытка отменить скачивание.");
                _downloadCts.Cancel();
                DownloadStatus = "Отмена скачивания...";
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDelete))]
        private async Task DeleteFileAsync()
        {
            if (_storageClient == null || SelectedFile == null)
            {
                 MessageBox.Show("Клиент gRPC не инициализирован или файл для удаления не выбран.", "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fileToDelete = SelectedFile;
            var result = MessageBox.Show($"Вы уверены, что хотите удалить '{fileToDelete.FileName}' (ID: {fileToDelete.FileId})?\nЭто действие попытается удалить файл со всех узлов и не может быть легко отменено.",
                                         "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

             UpdateStatusBar($"Удаление {fileToDelete.FileName}...");
             _logger.LogInformation("Инициирование запроса на удаление файла с ID: {FileId}", fileToDelete.FileId);

            try
            {
                var request = new DeleteFileRequest { FileId = fileToDelete.FileId };
                var reply = await _storageClient.DeleteFileAsync(request, deadline: DateTime.UtcNow.AddSeconds(30));


                if (reply.Success)
                {
                    UpdateStatusBar($"Успешно удален {fileToDelete.FileName}. {reply.Message}");
                     MessageBox.Show($"Процесс удаления файла '{fileToDelete.FileName}' инициирован.\nСообщение сервера: {reply.Message}", "Успешное удаление", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshFilesListAsync();
                }
                else
                {
                    UpdateStatusBar($"Не удалось удалить {fileToDelete.FileName}");
                     MessageBox.Show($"Сервер сообщил об ошибке во время удаления '{fileToDelete.FileName}':\n{reply.Message}", "Сбой удаления", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (RpcException ex)
            {
                UpdateStatusBar($"Не удалось удалить {fileToDelete.FileName} (Ошибка gRPC)");
                MessageBox.Show($"Произошла ошибка во время удаления:\nСтатус: {ex.StatusCode}\nДетали: {ex.Status.Detail}", "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"Не удалось удалить {fileToDelete.FileName} (Ошибка)");
                MessageBox.Show($"Произошла неожиданная ошибка во время удаления:\n{ex.Message}", "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                 // IsDeleting = false;
                 // Clear selection after attempt? Or keep it?
                 // SelectedFileForDelete = null;
            }
        }


        [RelayCommand(CanExecute = nameof(CanExecuteRefreshFilesList))]
        private async Task RefreshFilesListAsync()
        {
            if (_storageClient == null)
            {
                UpdateStatusBar("Невозможно обновить список файлов: клиент gRPC не готов.");
                 MessageBox.Show("Невозможно обновить список файлов, потому что соединение с сервером не установлено.", "Ошибка соединения",
                     MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatusBar("Обновление списка файлов...");
             _logger.LogInformation("Обновление списка файлов...");

            try
            {
                var request = new ListFilesRequest();
                var reply = await _storageClient.ListFilesAsync(request, deadline: DateTime.UtcNow.AddSeconds(15));

                Files.Clear();
                if (reply.Files != null)
                {
                    foreach (var fileProto in reply.Files.OrderBy(f => f.FileName))
                    {
                        Files.Add(new FileViewModel
                        {
                            FileId = fileProto.FileId,
                            FileName = fileProto.FileName,
                            FileSize = fileProto.FileSize,
                            CreationTime = fileProto.CreationTime?.ToDateTime().ToLocalTime() ?? DateTime.MinValue,
                            ContentType = fileProto.ContentType,
                            State = fileProto.State.ToString().Replace("FILE_STATE_", "")
                        });
                    }
                }
                UpdateStatusBar($"Список файлов обновлен. Найдено {Files.Count} файлов.");
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "Ошибка gRPC при обновлении списка файлов");
                UpdateStatusBar("Ошибка обновления списка файлов (Ошибка gRPC)");
                MessageBox.Show($"Не удалось обновить список файлов:\nСтатус: {ex.StatusCode}\nДетали: {ex.Status.Detail}", "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
                Files.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Неожиданная ошибка при обновлении списка файлов");
                UpdateStatusBar("Ошибка обновления списка файлов");
                MessageBox.Show($"Произошла неожиданная ошибка при обновлении списка файлов:\n{ex.Message}", "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
                Files.Clear();
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteRefreshNodeStatus))]
        private async Task RefreshNodeStatusAsync()
        {
            if (_storageClient == null)
            {
                UpdateStatusBar("Невозможно обновить статус узлов: клиент gRPC не готов.");
                MessageBox.Show("Невозможно обновить статусы узлов, потому что соединение с сервером не установлено.", "Ошибка соединения", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsNodeStatusRefreshing = true;

            UpdateStatusBar("Обновление статусов узлов...");
            _logger.LogInformation("Обновление статусов узлов...");

            Nodes.Clear();
            Nodes.Add(new NodeViewModel { NodeId = "Обновление...", Status = "Занят"});

            try
            {
                var request = new GetNodeStatusesRequest();
                var reply = await _storageClient.GetNodeStatusesAsync(request, deadline: DateTime.UtcNow.AddSeconds(15));

                Nodes.Clear();
                if (reply.Nodes != null)
                {
                    foreach (var nodeProto in reply.Nodes.OrderBy(n => n.NodeId))
                    {
                        Nodes.Add(new NodeViewModel
                        {
                            NodeId = nodeProto.NodeId,
                            Address = nodeProto.Address,
                            Status = nodeProto.Status.ToString().Replace("NODE_STATE_", ""),
                            StatusDetails = nodeProto.Details
                        });
                    }
                }
                UpdateStatusBar($"Статус узлов обновлен. Найдено {Nodes.Count} узлов.");
            }
            catch (RpcException ex)
            {
                 _logger.LogError(ex, "Ошибка gRPC при обновлении статусов узлов");
                 Nodes.Clear();
                 Nodes.Add(new NodeViewModel { NodeId = "Ошибка", Status = "Сбой", StatusDetails = $"gRPC: {ex.StatusCode}"});
                UpdateStatusBar("Ошибка обновления статусов узлов (Ошибка gRPC)");
                MessageBox.Show($"Не удалось обновить статусы узлов:\nСтатус: {ex.StatusCode}\nДетали: {ex.Status.Detail}", "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Неожиданная ошибка при обновлении статусов узлов");
                 Nodes.Clear();
                 Nodes.Add(new NodeViewModel { NodeId = "Ошибка", Status = "Сбой", StatusDetails = $"Ошибка: {ex.Message}"});
                UpdateStatusBar("Ошибка обновления статусов узлов");
                MessageBox.Show($"Произошла неожиданная ошибка при обновлении статусов узлов:\n{ex.Message}", "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsNodeStatusRefreshing = false;
            }
        }

        private async Task UpdateSimulationNodesAsync()
        {
            if (_storageClient == null) return;

            try
            {
                var request = new GetNodeStatusesRequest();
                var reply = await _storageClient.GetNodeStatusesAsync(request);

                SimulationNodes.Clear();
                if (reply.Nodes != null)
                {
                    foreach (var nodeProto in reply.Nodes)
                    {
                        SimulationNodes.Add(new NodeViewModel
                        {
                            NodeId = nodeProto.NodeId,
                            Address = nodeProto.Address,
                            Status = nodeProto.Status.ToString().Replace("NODE_STATE_", ""),
                            IsSelected = false
                        });
                    }
                }

                CanSimulateNodeFailure = SimulationNodes.Count > 0;
                CanRestoreNodes = _storageClient != null;

                DisableSelectedNodesCommand.NotifyCanExecuteChanged();
                RestoreAllNodesCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка обновления узлов симуляции");
                SimulationNodes.Clear();
                CanSimulateNodeFailure = false;
                CanRestoreNodes = false;

                DisableSelectedNodesCommand.NotifyCanExecuteChanged();
                RestoreAllNodesCommand.NotifyCanExecuteChanged();
            }
        }

        #region Node Settings Loading Logic
        private async Task LoadSettingsAsync()
        {
            if (_storageClient == null)
            {
                _logger.LogWarning("Невозможно загрузить настройки: StorageServiceClient равен null (нет соединения).");
                SettingNodeId = "Н/Д";
                SettingListenAddress = "Н/Д";
                SettingStorageBasePath = "Н/Д";
                SettingReplicationFactor = 0;
                SettingDefaultChunkSize = 0;
                SettingsErrorMessage = "Нет соединения с узлом.";
                HasSettingsError = true;
                IsSettingsLoading = false;
                OnPropertyChanged(nameof(IsSettingsInteractionEnabled));
                return;
            }

            IsSettingsLoading = true;
            HasSettingsError = false;
            SettingsErrorMessage = string.Empty;
            UpdateStatusBar("Загрузка настроек узла...");
            _logger.LogInformation("Попытка загрузить настройки узла...");
            RefreshSettingsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSettingsInteractionEnabled));

            try
            {
                var request = new GetNodeConfigurationRequest();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                var reply = await _storageClient.GetNodeConfigurationAsync(request, deadline: deadline);

                if (reply.Success)
                {
                    _logger.LogInformation("Настройки узла успешно загружены.");
                    SettingNodeId = reply.NodeId;
                    SettingListenAddress = reply.ListenAddress;
                    SettingStorageBasePath = reply.StorageBasePath;
                    SettingReplicationFactor = reply.ReplicationFactor;
                    SettingDefaultChunkSize = reply.DefaultChunkSize;

                    SettingCpuUsage = reply.CpuUsagePercent;

                    if (reply.MemoryUsedBytes > 0 && reply.MemoryTotalBytes > 0)
                    {
                        SettingMemoryUsage = $"{FormatBytes(reply.MemoryUsedBytes)} / {FormatBytes(reply.MemoryTotalBytes)}";
                    }
                    else
                    {
                        SettingMemoryUsage = "Неизвестно";
                    }

                    if (reply.DiskSpaceAvailableBytes > 0 && reply.DiskSpaceTotalBytes > 0)
                    {
                        SettingDiskSpace = $"{FormatBytes(reply.DiskSpaceAvailableBytes)} свободно из {FormatBytes(reply.DiskSpaceTotalBytes)}";
                    }
                    else
                    {
                        SettingDiskSpace = "Неизвестно";
                    }

                    UpdateStatusBar("Настройки узла загружены.");
                }
            }
            catch (RpcException rpcex)
            {
                SettingsErrorMessage = $"Ошибка gRPC при загрузке настроек: {rpcex.StatusCode}";
                HasSettingsError = true;
                _logger.LogError(rpcex, "Ошибка gRPC при загрузке настроек узла: {StatusCode}", rpcex.StatusCode);
                UpdateStatusBar($"Ошибка загрузки настроек (gRPC: {rpcex.StatusCode})");
            }
            catch (Exception ex)
            {
                SettingsErrorMessage = $"Ошибка загрузки настроек: {ex.Message}";
                HasSettingsError = true;
                _logger.LogError(ex, "Неожиданная ошибка при загрузке настроек узла.");
                UpdateStatusBar("Ошибка загрузки настроек.");
            }
            finally
            {
                IsSettingsLoading = false;
                RefreshSettingsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsSettingsInteractionEnabled));
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return $"{size:F2} {suffixes[suffixIndex]}";
        }

        [RelayCommand(CanExecute = nameof(CanRefreshSettings))]
        private async Task RefreshSettingsAsync()
        {
            await LoadSettingsAsync();
        }

        private bool CanRefreshSettings()
        {
            return _storageClient != null && !IsSettingsLoading;
        }

        #endregion

        [RelayCommand]
        private void StartSimulation()
        {
            MessageBox.Show("Функциональность симуляции сети пока не реализована.", "Симуляция", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void ViewLogs()
        {
             MessageBox.Show("Функциональность просмотра логов пока не реализована.", "Логи", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void OpenDocumentation()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowDocumentation();
            }
        }

        [RelayCommand]
        private void OpenAbout()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowAbout();
            }
        }
        [RelayCommand]
        private async Task DisableSelectedNodesAsync()
        {
            if (_storageClient == null || !SimulationNodes.Any() || !CanSimulateNodeFailure) return;

            var selectedNodes = SimulationNodes.Where(n => n.IsSelected).ToList();

            if (!selectedNodes.Any())
            {
                MessageBox.Show("Пожалуйста, выберите хотя бы один узел для отключения.",
                    "Выбор узла", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsSimulationInProgress = true;
            SimulationStatus = "Отключение выбранных узлов...";
            SimulationStatusColor = Brushes.Orange;
            SimulationProgress = 0;
            AppendToSimulationLog($"Начало симуляции: отключение {selectedNodes.Count} узлов...");

            try
            {
                var progressIncrement = 100.0 / selectedNodes.Count;
                foreach (var node in selectedNodes)
                {
                    var request = new SimulateNodeFailureRequest { NodeId = node.NodeId };
                    var reply = await _storageClient.SimulateNodeFailureAsync(request);

                    if (reply.Success)
                    {
                        AppendToSimulationLog($"Узел {request.NodeId} успешно отключен: {reply.Message}");
                    }
                    else
                    {
                        AppendToSimulationLog($"Не удалось отключить узел {request.NodeId}: {reply.Message}");
                    }

                    SimulationProgress += progressIncrement;
                }

                await RefreshNodeStatusAsync();
                await UpdateSimulationNodesAsync();
                await UpdateFileAndChunkStatusAsync();

                SimulationStatus = "Симуляция отказа узла завершена";
                SimulationStatusColor = Brushes.Green;
                AppendToSimulationLog("Симуляция завершена. Проверьте доступность файлов и распределение фрагментов.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка во время симуляции отказа узла");
                SimulationStatus = $"Ошибка: {ex.Message}";
                SimulationStatusColor = Brushes.Red;
                AppendToSimulationLog($"Ошибка симуляции: {ex.Message}");
            }
            finally
            {
                IsSimulationInProgress = false;
                SimulationProgress = 100;
            }
        }

        [RelayCommand]
        private async Task RestoreAllNodesAsync()
        {
            if (_storageClient == null || !CanRestoreNodes) return;

            IsSimulationInProgress = true;
            SimulationStatus = "Восстановление всех узлов...";
            SimulationStatusColor = Brushes.Orange;
            SimulationProgress = 50;
            AppendToSimulationLog("Восстановление всех узлов до онлайн-состояния...");

            try
            {
                var request = new RestoreAllNodesRequest();
                var reply = await _storageClient.RestoreAllNodesAsync(request);

                if (reply.Success)
                {
                    AppendToSimulationLog($"Все узлы восстановлены: {reply.Message}");

                    await RefreshNodeStatusAsync();
                    await UpdateSimulationNodesAsync();
                    await UpdateFileAndChunkStatusAsync();

                    SimulationStatus = "Все узлы успешно восстановлены";
                    SimulationStatusColor = Brushes.Green;
                }
                else
                {
                    AppendToSimulationLog($"Не удалось восстановить узлы: {reply.Message}");
                    SimulationStatus = $"Восстановление не удалось: {reply.Message}";
                    SimulationStatusColor = Brushes.Red;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка во время восстановления узлов");
                SimulationStatus = $"Ошибка: {ex.Message}";
                SimulationStatusColor = Brushes.Red;
                AppendToSimulationLog($"Ошибка восстановления: {ex.Message}");
            }
            finally
            {
                IsSimulationInProgress = false;
                SimulationProgress = 100;
            }
        }

        private void AppendToSimulationLog(string message)
        {
            SimulationLog += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        }
        
        public void Dispose()
        {
            _uploadCts?.Cancel();
            _uploadCts?.Dispose();
            _uploadCts = null;

            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;

            _currentChannel?.Dispose();
            _currentChannel = null;
            _storageClient = null;

            _logger.LogInformation("MainWindowViewModel disposed.");
        }

    }
}