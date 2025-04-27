using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VKR.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using VRK_WPF.MVVM.View;
using VRK_WPF.MVVM.ViewModel;


namespace VRK_WPF.MVVM.ViewModel
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly ILogger<MainWindowViewModel> _logger; 
        private StorageService.StorageServiceClient? _storageClient; 
        private GrpcChannel? _currentChannel;
        private CancellationTokenSource? _uploadCts;
        private CancellationTokenSource? _downloadCts;
        private readonly ILoggerFactory? _loggerFactory;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private string _targetNodeAddress = "http://localhost:5005"; 

        [ObservableProperty]
        private string _connectionStatus = "Disconnected";

        [ObservableProperty]
        private Brush _connectionStatusColor = Brushes.OrangeRed;
        

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
        private string _uploadStatus = "Ready";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UploadFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(DownloadFileCommand))]
        [NotifyCanExecuteChangedFor(nameof(DeleteFileCommand))]
        private bool _isDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _downloadStatus = "Ready";

        [ObservableProperty]
        private string _statusBarText = "Ready";
        
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
        [NotifyCanExecuteChangedFor(nameof(RefreshNodeStatusCommand))]
        private bool _isNodeStatusRefreshing;
        public ObservableCollection<FileViewModel> StoredFiles { get; } = new ObservableCollection<FileViewModel>();
        public ICollectionView FilesView { get; } 
        public ObservableCollection<NodeViewModel> NetworkNodes { get; } = new ObservableCollection<NodeViewModel>();
        public ICollectionView NodesView { get; } 
        
        #region Node Settings Properties
        
        [ObservableProperty]
        private string _settingNodeId = "N/A"; // Default to N/A when not connected

        [ObservableProperty]
        private string _settingListenAddress = "N/A";

        [ObservableProperty]
        private string _settingStorageBasePath = "N/A";

        [ObservableProperty]
        private int _settingReplicationFactor;

        [ObservableProperty]
        private int _settingDefaultChunkSize;

        [ObservableProperty]
        private string _settingsErrorMessage = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))] // Notify dependent property
        private bool _isSettingsLoading = false;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))] // Notify dependent property
        private bool _hasSettingsError = false;
        
        public bool IsSettingsInteractionEnabled => _storageClient != null && !IsSettingsLoading && !HasSettingsError;

        #endregion
        
        public MainWindowViewModel(ILogger<MainWindowViewModel> logger, ILoggerFactory? loggerFactory) 
        {
            _logger = logger;
            _loggerFactory = loggerFactory; 
            
            FilesView = CollectionViewSource.GetDefaultView(StoredFiles);
            FilesView.SortDescriptions.Add(new SortDescription(nameof(FileViewModel.FileName), ListSortDirection.Ascending));
            
            NodesView = CollectionViewSource.GetDefaultView(NetworkNodes);
            NodesView.SortDescriptions.Add(new SortDescription(nameof(NodeViewModel.NodeId), ListSortDirection.Ascending));
            
            UpdateConnectionStatus("Disconnected", Brushes.OrangeRed);
            UpdateStatusBar("Ready. Enter node address and connect.");
        }
    
        public MainWindowViewModel(ILogger<MainWindowViewModel> logger) 
        {
            _logger = logger;
            
            FilesView = CollectionViewSource.GetDefaultView(StoredFiles);
            FilesView.SortDescriptions.Add(new SortDescription(nameof(FileViewModel.FileName), ListSortDirection.Ascending));
            
            NodesView = CollectionViewSource.GetDefaultView(NetworkNodes);
            NodesView.SortDescriptions.Add(new SortDescription(nameof(NodeViewModel.NodeId), ListSortDirection.Ascending));
            
            UpdateConnectionStatus("Disconnected", Brushes.OrangeRed);
            UpdateStatusBar("Ready. Enter node address and connect.");
        }    
        
        public MainWindowViewModel() : this(LoggerFactory.Create(builder => builder.AddDebug()).CreateLogger<MainWindowViewModel>())
        {
            if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                TargetNodeAddress = "http://design-time:5000";
                ConnectionStatus = "Design Mode";
                ConnectionStatusColor = Brushes.Gray;
                StoredFiles.Add(new FileViewModel { FileId = "design-1", FileName = "DesignFile1.txt", FileSize = 1024, CreationTime = DateTime.Now, State = "Available" });
                NetworkNodes.Add(new NodeViewModel { NodeId = "Node1-Design", Address = "localhost:5001", Status = "Online", StatusDetails="Design Mode"});
            }
        }

        private void UpdateConnectionStatus(string status, Brush color)
        {
            ConnectionStatus = status;
            ConnectionStatusColor = color;
            StatusBarText = status; 
        }
        
        private bool ConnectToNode()
        {
            _currentChannel?.Dispose();
            _currentChannel = null;
            _storageClient = null;
            UpdateConnectionStatus("Connecting...", Brushes.Orange);

            try
            {
                 if (string.IsNullOrWhiteSpace(TargetNodeAddress) || !Uri.TryCreate(TargetNodeAddress, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                 {
                     _logger?.LogError("Invalid gRPC server address format: {Address}", TargetNodeAddress);
                     UpdateConnectionStatus($"Error: Invalid address format.", Brushes.Red);
                     MessageBox.Show($"Invalid server address format: {TargetNodeAddress}\nPlease use http://host:port or https://host:port", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                     return false;
                 }
                 
                 var channelOptions = new GrpcChannelOptions();

                _currentChannel = GrpcChannel.ForAddress(TargetNodeAddress, channelOptions);
                _storageClient = new StorageService.StorageServiceClient(_currentChannel);
                OnPropertyChanged(nameof(Debug_IsUploadPossible));
                _logger?.LogInformation("gRPC client initialized for address: {Address}", TargetNodeAddress);
                UpdateConnectionStatus($"Connected to {TargetNodeAddress}", Brushes.Green);
                return true; 
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize gRPC client for address: {Address}", TargetNodeAddress);
                UpdateConnectionStatus($"Error connecting to {TargetNodeAddress}", Brushes.Red);
                MessageBox.Show($"Failed to connect to gRPC server at {TargetNodeAddress}.\nEnsure the server is running and the address is correct.\n\nError: {ex.Message}", "gRPC Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _storageClient = null;
                _currentChannel = null;
                return false; 
            }
        }
        
        private void UpdateStatusBar(string message) { StatusBarText = message; _logger?.LogInformation("Status Bar: {Message}", message); }
        private bool CanExecuteUpload() => !string.IsNullOrEmpty(SelectedFilePath) && File.Exists(SelectedFilePath) && !IsUploading && !IsDownloading && _storageClient != null;
        
        public bool Debug_IsUploadPossible => !string.IsNullOrEmpty(SelectedFilePath) && File.Exists(SelectedFilePath) && !IsUploading && !IsDownloading && _storageClient != null;
        private bool CanExecuteDownload() => SelectedFile != null && !IsDownloading && !IsUploading &&_storageClient != null;
        private bool CanExecuteDelete() => SelectedFile != null && !IsUploading && !IsDownloading && _storageClient != null;
        private bool CanExecuteRefreshNodeStatus() => !IsNodeStatusRefreshing && _storageClient != null;
        private bool CanExecuteRefreshFilesList() => _storageClient != null; 
        private bool CanExecuteConnect() => !string.IsNullOrWhiteSpace(TargetNodeAddress);
        // --- Methods ---
        
        
        [RelayCommand(CanExecute = nameof(CanExecuteConnect))]
        private async Task ConnectAsync()
        {
            _logger?.LogInformation("Attempting to connect to node: {Address}", TargetNodeAddress);
            StoredFiles.Clear(); 
            NetworkNodes.Clear();

            if (ConnectToNode()) 
            {
                await RefreshFilesListAsync();
                await RefreshNodeStatusAsync();
            }
            else
            {
                NetworkNodes.Clear();
            }
        }
        
        
        
        [RelayCommand]
        private void SelectFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select File to Upload",
                Filter = "All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedFilePath = openFileDialog.FileName;
                SelectedFileName = Path.GetFileName(SelectedFilePath);
                 _logger?.LogInformation("File selected for upload: {FilePath}", SelectedFilePath);
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteUpload))]
        private async Task UploadFileAsync()
        {
            if (_storageClient == null || string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
            {
                MessageBox.Show("gRPC client not initialized or file not selected/found.", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsUploading = true;
            UploadProgress = 0;
            UploadStatus = "Starting upload...";
            _uploadCts = new CancellationTokenSource();
            UpdateStatusBar($"Uploading {SelectedFileName}...");
            _logger?.LogInformation("Upload started for file: {FilePath}", SelectedFilePath); // Added log

            AsyncClientStreamingCall<UploadFileRequest, UploadFileReply>? call = null; // Declare call object outside try

            try
            {
                var fileInfo = new FileInfo(SelectedFilePath);
                long fileSize = fileInfo.Length;

                // Prepare metadata (ensure your proto has ExpectedFileSize field)
                var metadata = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    ExpectedFileSize = fileSize, // Send actual file size
                    ContentType = MimeMapping.MimeUtility.GetMimeMapping(fileInfo.Name), // Requires MimeMapping nuget or alternative
                    CreationTime = Timestamp.FromDateTime(DateTime.UtcNow)
                    // ChunkSize determined by server
                };
                _logger?.LogInformation("Prepared metadata. Expected Size: {FileSize}", fileSize);


                // Start the call
                call = _storageClient.UploadFile(cancellationToken: _uploadCts.Token);
                _logger?.LogInformation("gRPC UploadFile call initiated.");

                // 1. Send Metadata FIRST
                _logger?.LogDebug("Sending metadata...");
                await call.RequestStream.WriteAsync(new UploadFileRequest { Metadata = metadata });
                UploadStatus = "Sent metadata, sending chunks...";
                 _logger?.LogDebug("Metadata sent.");

                // 2. Send Chunks IN A LOOP
                 _logger?.LogDebug("Starting chunk sending loop...");
                long totalBytesSent = 0;
                int chunkIndex = 0;
                // Chunk size for buffer reading - can be different from server's internal chunking if needed, but using a reasonable size like 1MB is fine.
                int bufferSize = 1 * 1024 * 1024; // 1MB read buffer

                await using (var fileStream = File.OpenRead(SelectedFilePath))
                {
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;
                    while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, _uploadCts.Token)) > 0)
                    {
                        // Check for cancellation within the loop
                         _uploadCts.Token.ThrowIfCancellationRequested();

                        var chunkData = ByteString.CopyFrom(buffer, 0, bytesRead);
                        // Let server generate ChunkId? Or generate client-side? Assuming client-side for now.
                        var chunkId = $"chunk_{Guid.NewGuid()}_{chunkIndex}";
                        var chunk = new FileChunk
                        {
                            ChunkId = chunkId,
                            ChunkIndex = chunkIndex,
                            Data = chunkData,
                            Size = bytesRead // Important: Send the actual size read
                        };

                        _logger?.LogTrace("Sending Chunk Index: {Index}, Size: {Size}", chunkIndex, bytesRead);
                        await call.RequestStream.WriteAsync(new UploadFileRequest { Chunk = chunk });
                         _logger?.LogTrace("Sent Chunk Index: {Index}", chunkIndex);


                        totalBytesSent += bytesRead;
                        chunkIndex++; // Increment chunk index

                        if (fileSize > 0)
                        {
                             UploadProgress = (double)totalBytesSent / fileSize * 100;
                             UploadStatus = $"Uploading chunk {chunkIndex}... ({UploadProgress:F1}%)";
                        } else {
                             // Handle empty file case immediately
                             UploadProgress = 100;
                             UploadStatus = $"Uploading empty file...";
                             // Break loop after sending 0-byte chunk if necessary, or let CompleteAsync handle it
                        }
                    }
                } // FileStream disposed here
                _logger?.LogInformation("Finished sending {ChunkCount} chunks. Total bytes sent: {TotalBytes}", chunkIndex, totalBytesSent);

                // 3. Complete Request Stream **AFTER** the loop finishes
                 _logger?.LogDebug("Completing request stream...");
                await call.RequestStream.CompleteAsync(); // CRITICAL: This must be called only ONCE and AFTER all chunks are written
                 _logger?.LogInformation("Request stream completed.");

                UploadStatus = "Waiting for server confirmation...";

                // 4. Get Response
                var response = await call.ResponseAsync;
                _logger?.LogInformation("Received final response from server. Success: {Success}, Message: {Message}", response.Success, response.Message);


                if (response.Success)
                {
                    UploadStatus = $"Upload complete! File ID: {response.FileId}";
                    UpdateStatusBar($"Successfully uploaded {SelectedFileName}");
                    await RefreshFilesListAsync(); // Refresh UI
                }
                else
                {
                    UploadStatus = $"Upload failed: {response.Message}";
                    UpdateStatusBar($"Upload failed for {SelectedFileName}");
                    MessageBox.Show($"Server reported an error during upload:\n{response.Message}", "Upload Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (RpcException ex)
            {
                _logger?.LogError(ex, "gRPC Error during upload: Status={StatusCode}, Detail={Detail}", ex.StatusCode, ex.Status.Detail);
                UploadStatus = $"gRPC Error: {ex.StatusCode}";
                UpdateStatusBar($"Upload failed for {SelectedFileName} (gRPC Error)");
                MessageBox.Show($"An error occurred during upload:\nStatus: {ex.StatusCode}\nDetail: {ex.Status.Detail}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("Upload cancelled by user.");
                UploadStatus = "Upload cancelled.";
                UpdateStatusBar($"Upload cancelled for {SelectedFileName}");
            }
            catch (Exception ex)
            {
                 _logger?.LogError(ex, "Unexpected error during upload.");
                UploadStatus = $"Error: {ex.Message}";
                UpdateStatusBar($"Upload failed for {SelectedFileName} (Error)");
                MessageBox.Show($"An unexpected error occurred during upload:\n{ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsUploading = false;
                UploadProgress = 0;
                // Clear selection only on success? Or always? User preference.
                // SelectedFilePath = null;
                // SelectedFileName = null;
                _uploadCts?.Dispose();
                _uploadCts = null;
                // Note: The 'call' object (AsyncClientStreamingCall) doesn't need explicit disposal typically,
                // as completing/awaiting the response handles resource cleanup.
                _logger?.LogInformation("Upload operation finished.");
            }
        }

        
        
        [RelayCommand]
        private void CancelUpload()
        {
            if (IsUploading && _uploadCts != null && !_uploadCts.IsCancellationRequested)
            {
                _logger?.LogInformation("Attempting to cancel upload.");
                _uploadCts.Cancel();
                UploadStatus = "Cancelling upload...";
            }
        }


        [RelayCommand(CanExecute = nameof(CanExecuteDownload))]
        private async Task DownloadFileAsync()
        {
            if (_storageClient == null || SelectedFile == null)
            {
                 MessageBox.Show("gRPC client not initialized or no file selected.", "Download Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fileToDownload = SelectedFile; // Capture selection
            var saveFileDialog = new SaveFileDialog
            {
                Title = "Save Downloaded File As",
                FileName = fileToDownload.FileName ?? "downloaded_file"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            string savePath = saveFileDialog.FileName;
            IsDownloading = true; // Triggers CanExecute changes
            DownloadProgress = 0;
            DownloadStatus = "Starting download...";
            _downloadCts = new CancellationTokenSource();
            UpdateStatusBar($"Downloading {fileToDownload.FileName}...");

            string tempDownloadPath = savePath + ".tmp"; // Download to temp file first

            try
            {
                var request = new DownloadFileRequest { FileId = fileToDownload.FileId };
                using var call = _storageClient.DownloadFile(request, cancellationToken: _downloadCts.Token);

                FileMetadata? fileMetadata = null;
                long totalBytesReceived = 0;
                long expectedFileSize = 0;
                int chunksReceived = 0;

                await using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    await foreach (var reply in call.ResponseStream.ReadAllAsync(_downloadCts.Token))
                    {
                        if (reply.PayloadCase == DownloadFileReply.PayloadOneofCase.Metadata)
                        {
                            fileMetadata = reply.Metadata;
                            expectedFileSize = fileMetadata.FileSize;
                            DownloadStatus = $"Received metadata, downloading chunks... (Expected size: {expectedFileSize} bytes)";
                        }
                        else if (reply.PayloadCase == DownloadFileReply.PayloadOneofCase.Chunk)
                        {
                            if (fileMetadata == null) throw new InvalidOperationException("Chunk data received before file metadata.");

                            var chunk = reply.Chunk;
                            await fileStream.WriteAsync(chunk.Data.Memory, _downloadCts.Token);
                            totalBytesReceived += chunk.Size;
                            chunksReceived++;

                            if (expectedFileSize > 0) {
                                DownloadProgress = (double)totalBytesReceived / expectedFileSize * 100;
                                DownloadStatus = $"Downloading chunk {chunksReceived}/{fileMetadata.TotalChunks}... ({DownloadProgress:F1}%)";
                            } else {
                                DownloadStatus = $"Downloading chunk {chunksReceived}... ({totalBytesReceived} bytes received)";
                            }
                        }
                    }
                } // FileStream disposed here, flushing writes

                // Verify size and rename temp file
                if (expectedFileSize > 0 && totalBytesReceived != expectedFileSize)
                {
                    DownloadStatus = $"Download complete, but size mismatch! Expected {expectedFileSize}, Got {totalBytesReceived}";
                    UpdateStatusBar($"Download completed with size mismatch for {fileToDownload.FileName}");
                     MessageBox.Show($"Download completed, but the received file size ({totalBytesReceived} bytes) does not match the expected size ({expectedFileSize} bytes).\nThe file might be incomplete or corrupted.", "Download Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                     // Keep the .tmp file for inspection? Or delete? Decide policy.
                     // if (File.Exists(tempDownloadPath)) File.Delete(tempDownloadPath);
                }
                else
                {
                     // Rename temp file to final name
                     if (File.Exists(savePath)) File.Delete(savePath); // Delete if exists
                     File.Move(tempDownloadPath, savePath);

                    DownloadStatus = "Download complete!";
                    UpdateStatusBar($"Successfully downloaded {fileToDownload.FileName}");
                }
            }
            catch (RpcException ex)
            {
                DownloadStatus = $"gRPC Error: {ex.StatusCode}";
                UpdateStatusBar($"Download failed for {fileToDownload.FileName} (gRPC Error)");
                MessageBox.Show($"An error occurred during download:\nStatus: {ex.StatusCode}\nDetail: {ex.Status.Detail}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch {} }
            }
            catch (OperationCanceledException)
            {
                DownloadStatus = "Download cancelled.";
                UpdateStatusBar($"Download cancelled for {fileToDownload.FileName}");
                if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch {} }
            }
            catch (Exception ex)
            {
                DownloadStatus = $"Error: {ex.Message}";
                UpdateStatusBar($"Download failed for {fileToDownload.FileName} (Error)");
                MessageBox.Show($"An unexpected error occurred during download:\n{ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                if (File.Exists(tempDownloadPath)) { try { File.Delete(tempDownloadPath); } catch {} }
            }
            finally
            {
                IsDownloading = false; // Triggers CanExecute changes
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
                 _logger?.LogInformation("Attempting to cancel download.");
                _downloadCts.Cancel();
                DownloadStatus = "Cancelling download...";
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDelete))]
        private async Task DeleteFileAsync()
        {
             if (_storageClient == null || SelectedFile == null)
            {
                 MessageBox.Show("gRPC client not initialized or no file selected for deletion.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fileToDelete = SelectedFile; // Capture selected file
            var result = MessageBox.Show($"Are you sure you want to delete '{fileToDelete.FileName}' (ID: {fileToDelete.FileId})?\nThis action attempts to remove the file from all nodes and cannot be easily undone.",
                                         "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

             UpdateStatusBar($"Deleting {fileToDelete.FileName}...");
             _logger?.LogInformation("Initiating delete request for File ID: {FileId}", fileToDelete.FileId);

             // Optionally disable interaction while deleting
             // IsDeleting = true; // Need IsDeleting property if used

            try
            {
                var request = new DeleteFileRequest { FileId = fileToDelete.FileId };
                var reply = await _storageClient.DeleteFileAsync(request, deadline: DateTime.UtcNow.AddSeconds(30)); // Add timeout


                if (reply.Success)
                {
                    UpdateStatusBar($"Successfully deleted {fileToDelete.FileName}. {reply.Message}");
                     MessageBox.Show($"File '{fileToDelete.FileName}' delete process initiated.\nServer message: {reply.Message}", "Delete Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshFilesListAsync(); // Refresh list
                }
                else
                {
                    UpdateStatusBar($"Delete failed for {fileToDelete.FileName}");
                     MessageBox.Show($"Server reported an error during deletion of '{fileToDelete.FileName}':\n{reply.Message}", "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
             catch (RpcException ex)
            {
                UpdateStatusBar($"Delete failed for {fileToDelete.FileName} (gRPC Error)");
                MessageBox.Show($"An error occurred during deletion:\nStatus: {ex.StatusCode}\nDetail: {ex.Status.Detail}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"Delete failed for {fileToDelete.FileName} (Error)");
                MessageBox.Show($"An unexpected error occurred during deletion:\n{ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                 // IsDeleting = false;
                 // Clear selection after attempt? Or keep it?
                 // SelectedFileForDelete = null; // Maybe clear only on success
            }
        }


        [RelayCommand(CanExecute = nameof(CanExecuteRefreshFilesList))] 
        private async Task RefreshFilesListAsync()
        {
            if (_storageClient == null)
            {
                UpdateStatusBar("Cannot refresh files: gRPC client not ready.");
                 MessageBox.Show("Cannot refresh file list because the connection to the server is not established.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatusBar("Refreshing file list...");
             _logger?.LogInformation("Refreshing file list...");
             // Consider showing a busy indicator

            try
            {
                var request = new ListFilesRequest();
                var reply = await _storageClient.ListFilesAsync(request, deadline: DateTime.UtcNow.AddSeconds(15));

                StoredFiles.Clear(); // Clear existing list before adding new items
                if (reply.Files != null)
                {
                    foreach (var fileProto in reply.Files.OrderBy(f => f.FileName))
                    {
                        // *** Corrected Mapping Here ***
                        StoredFiles.Add(new FileViewModel
                        {
                            FileId = fileProto.FileId,
                            FileName = fileProto.FileName,
                            FileSize = fileProto.FileSize,
                            // Ensure Timestamp is converted correctly and handle null
                            CreationTime = fileProto.CreationTime?.ToDateTime().ToLocalTime() ?? DateTime.MinValue,
                            ContentType = fileProto.ContentType,
                            // Convert the proto enum to a string for display
                            State = fileProto.State.ToString().Replace("FILE_STATE_", "") // Nicer display string
                        });
                    }
                }
                UpdateStatusBar($"File list refreshed. Found {StoredFiles.Count} files.");
            }
            catch (RpcException ex)
            {
                 _logger?.LogError(ex, "gRPC Error refreshing file list");
                UpdateStatusBar("Error refreshing file list (gRPC Error)");
                MessageBox.Show($"Failed to refresh file list:\nStatus: {ex.StatusCode}\nDetail: {ex.Status.Detail}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 StoredFiles.Clear(); // Clear potentially partial list
            }
            catch (Exception ex)
            {
                 _logger?.LogError(ex, "Unexpected error refreshing file list");
                UpdateStatusBar("Error refreshing file list");
                MessageBox.Show($"An unexpected error occurred while refreshing the file list:\n{ex.Message}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 StoredFiles.Clear(); // Clear potentially partial list
            }
            finally
            {
                 // Hide busy indicator
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteRefreshNodeStatus))]
        private async Task RefreshNodeStatusAsync()
        {
             if (_storageClient == null)
            {
                UpdateStatusBar("Cannot refresh node status: gRPC client not ready.");
                 MessageBox.Show("Cannot refresh node statuses because the connection to the server is not established.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsNodeStatusRefreshing = true; // Triggers CanExecute changes
            UpdateStatusBar("Refreshing node statuses...");
            _logger?.LogInformation("Refreshing node statuses...");

            NetworkNodes.Clear();
            NetworkNodes.Add(new NodeViewModel { NodeId = "Refreshing...", Status = "Busy"});

            try
            {
                var request = new GetNodeStatusesRequest();
                var reply = await _storageClient.GetNodeStatusesAsync(request, deadline: DateTime.UtcNow.AddSeconds(15));

                NetworkNodes.Clear(); // Clear "Refreshing..."
                if (reply.Nodes != null)
                {
                    foreach (var nodeProto in reply.Nodes.OrderBy(n => n.NodeId))
                    {
                        NetworkNodes.Add(new NodeViewModel
                        {
                            NodeId = nodeProto.NodeId,
                            Address = nodeProto.Address,
                            // Convert proto enum to string, potentially make it nicer
                            Status = nodeProto.Status.ToString().Replace("NODE_STATE_", ""),
                            StatusDetails = nodeProto.Details
                        });
                    }
                }
                UpdateStatusBar($"Node status refreshed. Found {NetworkNodes.Count} nodes.");
            }
            catch (RpcException ex)
            {
                 _logger?.LogError(ex, "gRPC Error refreshing node statuses");
                 NetworkNodes.Clear();
                 NetworkNodes.Add(new NodeViewModel { NodeId = "Error", Status = "Failed", StatusDetails = $"gRPC: {ex.StatusCode}"});
                UpdateStatusBar("Error refreshing node statuses (gRPC Error)");
                MessageBox.Show($"Failed to refresh node statuses:\nStatus: {ex.StatusCode}\nDetail: {ex.Status.Detail}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                 _logger?.LogError(ex, "Unexpected error refreshing node statuses");
                 NetworkNodes.Clear();
                 NetworkNodes.Add(new NodeViewModel { NodeId = "Error", Status = "Failed", StatusDetails = $"Error: {ex.Message}"});
                UpdateStatusBar("Error refreshing node statuses");
                MessageBox.Show($"An unexpected error occurred while refreshing node statuses:\n{ex.Message}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsNodeStatusRefreshing = false; // Triggers CanExecute changes
            }
        }

        #region Node Settings Loading Logic

        /// <summary>
        /// Asynchronously loads the node settings from the connected gRPC server.
        /// Should be called after a successful connection or manually via Refresh.
        /// </summary>
        private async Task LoadSettingsAsync()
        {
            if (_storageClient == null)
            {
                _logger?.LogWarning("Cannot load settings: StorageServiceClient is null (not connected).");
                // Reset settings properties to default/disconnected state
                SettingNodeId = "N/A";
                SettingListenAddress = "N/A";
                SettingStorageBasePath = "N/A";
                SettingReplicationFactor = 0;
                SettingDefaultChunkSize = 0;
                SettingsErrorMessage = "Not connected to a node.";
                HasSettingsError = true; // Indicate an error state (not connected)
                IsSettingsLoading = false;
                OnPropertyChanged(nameof(IsSettingsInteractionEnabled)); // Update dependent property
                return;
            }

            IsSettingsLoading = true;
            HasSettingsError = false;
            SettingsErrorMessage = string.Empty;
            UpdateStatusBar("Loading node settings...");
            _logger?.LogInformation("Attempting to load node settings...");
            // Notify UI that interaction might be disabled
            RefreshSettingsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsSettingsInteractionEnabled));

            try
            {
                var request = new GetNodeConfigurationRequest();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                var reply = await _storageClient.GetNodeConfigurationAsync(request, deadline: deadline);

                if (reply.Success)
                {
                    _logger?.LogInformation("Successfully loaded node settings.");
                    // Update properties (ObservableObject handles UI thread dispatching)
                    SettingNodeId = reply.NodeId;
                    SettingListenAddress = reply.ListenAddress;
                    SettingStorageBasePath = reply.StorageBasePath;
                    SettingReplicationFactor = reply.ReplicationFactor;
                    SettingDefaultChunkSize = reply.DefaultChunkSize;
                    UpdateStatusBar("Node settings loaded.");
                }
                else
                {
                    SettingsErrorMessage = $"Failed to load settings: {reply.ErrorMessage}";
                    HasSettingsError = true;
                    _logger?.LogError("Failed to load node settings from server: {Error}", reply.ErrorMessage);
                    // Keep previous values or set to "Error"? Let's keep previous for now.
                    UpdateStatusBar($"Failed to load settings: {reply.ErrorMessage}");
                }
            }
            catch (RpcException rpcex)
            {
                SettingsErrorMessage = $"gRPC Error loading settings: {rpcex.StatusCode}";
                HasSettingsError = true;
                _logger?.LogError(rpcex, "gRPC error loading node settings: {StatusCode}", rpcex.StatusCode);
                UpdateStatusBar($"Error loading settings (gRPC: {rpcex.StatusCode})");
            }
            catch (Exception ex)
            {
                SettingsErrorMessage = $"Error loading settings: {ex.Message}";
                HasSettingsError = true;
                _logger?.LogError(ex, "Unexpected error loading node settings.");
                UpdateStatusBar("Error loading settings.");
            }
            finally
            {
                IsSettingsLoading = false;
                // Notify UI about state change
                RefreshSettingsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsSettingsInteractionEnabled));
            }
        }

        // Command to manually refresh settings
        [RelayCommand(CanExecute = nameof(CanRefreshSettings))]
        private async Task RefreshSettingsAsync()
        {
            await LoadSettingsAsync();
        }

        private bool CanRefreshSettings()
        {
            // Can refresh if connected and not already loading
            return _storageClient != null && !IsSettingsLoading;
        }

        #endregion

        [RelayCommand]
        private void StartSimulation()
        {
            MessageBox.Show("Network simulation functionality not implemented yet.", "Simulation", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void ViewLogs()
        {
             MessageBox.Show("Log viewing functionality not implemented yet.", "Logs", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void ShowAbout()
        {
            MessageBox.Show("VKR Distributed Storage Client v0.1.1\n\nDeveloped for testing purposes.", "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

    }
}
