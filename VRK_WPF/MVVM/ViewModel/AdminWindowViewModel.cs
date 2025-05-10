using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using VKR.Protos;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.View;

namespace VRK_WPF.MVVM.ViewModel
{
    public partial class AdminWindowViewModel : ObservableObject
    {
        private readonly ILogger<AdminWindowViewModel> _logger;
        private GrpcChannel? _currentChannel;
        private StorageService.StorageServiceClient? _storageClient;
        
        // Properties for UI binding
        [ObservableProperty]
        private ObservableCollection<NodeViewModel> _availableNodes = new();
        
        [ObservableProperty]
        private NodeViewModel? _selectedNode;
        
        [ObservableProperty]
        private string _selectedNodeName = "Не подключено";
        
        [ObservableProperty]
        private string _currentUserInfo = "Не авторизован";
        
        [ObservableProperty]
        private string _statusMessage = "Готово";
        
        [ObservableProperty]
        private bool _isProcessing;
        
        // Selected table for DB management
        [ObservableProperty]
        private string _selectedTable = "Files";
        
        // Properties for node status
        [ObservableProperty]
        private string _nodeStatus = "Не подключено";
        
        [ObservableProperty]
        private double _cpuUsage;
        
        [ObservableProperty]
        private string _memoryUsage = "N/A";
        
        [ObservableProperty]
        private string _diskSpace = "N/A";
        
        // Constructor
        public AdminWindowViewModel(ILogger<AdminWindowViewModel>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminWindowViewModel>.Instance;
            
            // Set current user info from auth service
            if (AuthService.CurrentUser != null)
            {
                CurrentUserInfo = $"{AuthService.CurrentUser.FullName} ({AuthService.CurrentUser.Role})";
            }
            
            // Initialize commands
            LogoutCommand = new RelayCommand(Logout);
            ConnectToNodeCommand = new RelayCommand(async () => await ConnectToNodeAsync(), CanConnectToNode);
            RefreshStatusCommand = new RelayCommand(async () => await RefreshNodeStatusAsync(), () => _storageClient != null);
            
            // Load available nodes
            LoadAvailableNodes();
        }
        
        // Commands
        public RelayCommand LogoutCommand { get; }
        public RelayCommand ConnectToNodeCommand { get; }
        public RelayCommand RefreshStatusCommand { get; }
        
        // Helper methods
        private void LoadAvailableNodes()
        {
            // In a real application, this would come from a configuration or database
            // For this example, we'll add some hardcoded nodes
            AvailableNodes.Clear();
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node1", Address = "http://localhost:5001" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node2", Address = "http://localhost:5002" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node3", Address = "http://localhost:5003" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node4", Address = "http://localhost:5004" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node5", Address = "http://localhost:5005" });
            
            // Default to first node
            if (AvailableNodes.Count > 0)
            {
                SelectedNode = AvailableNodes[0];
            }
        }
        
        private bool CanConnectToNode()
        {
            return SelectedNode != null && !string.IsNullOrWhiteSpace(SelectedNode.Address);
        }
        
        private async Task ConnectToNodeAsync()
        {
            if (SelectedNode == null || string.IsNullOrWhiteSpace(SelectedNode.Address))
            {
                MessageBox.Show("Выберите узел для подключения", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            IsProcessing = true;
            StatusMessage = $"Подключение к узлу {SelectedNode.NodeId}...";
            
            try
            {
                // Close existing connection
                _currentChannel?.Dispose();
                _currentChannel = null;
                _storageClient = null;
                
                // Create new connection
                _currentChannel = GrpcChannel.ForAddress(SelectedNode.Address);
                _storageClient = new StorageService.StorageServiceClient(_currentChannel);
                
                // Test connection
                var request = new GetNodeConfigurationRequest();
                var reply = await _storageClient.GetNodeConfigurationAsync(request, deadline: DateTime.UtcNow.AddSeconds(5));
                
                if (reply.Success)
                {
                    SelectedNodeName = $"Узел: {reply.NodeId}";
                    StatusMessage = $"Подключено к узлу {reply.NodeId}";
                    
                    // Update node status
                    CpuUsage = reply.CpuUsagePercent;
                    MemoryUsage = $"{FormatBytes(reply.MemoryUsedBytes)} / {FormatBytes(reply.MemoryTotalBytes)}";
                    DiskSpace = $"{FormatBytes(reply.DiskSpaceAvailableBytes)} свободно из {FormatBytes(reply.DiskSpaceTotalBytes)}";
                    NodeStatus = "Онлайн";
                    
                    // Refresh commands' CanExecute
                    ConnectToNodeCommand.NotifyCanExecuteChanged();
                    RefreshStatusCommand.NotifyCanExecuteChanged();
                }
                else
                {
                    StatusMessage = $"Ошибка подключения: {reply.ErrorMessage}";
                    MessageBox.Show($"Ошибка подключения к узлу: {reply.ErrorMessage}", "Ошибка подключения", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (RpcException ex)
            {
                StatusMessage = $"Ошибка подключения: {ex.Status.Detail}";
                MessageBox.Show($"Ошибка подключения к узлу: {ex.Status.Detail}", "Ошибка подключения", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentChannel?.Dispose();
                _currentChannel = null;
                _storageClient = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentChannel?.Dispose();
                _currentChannel = null;
                _storageClient = null;
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private async Task RefreshNodeStatusAsync()
        {
            if (_storageClient == null) return;
            
            IsProcessing = true;
            StatusMessage = "Обновление статуса узла...";
            
            try
            {
                var request = new GetNodeConfigurationRequest();
                var reply = await _storageClient.GetNodeConfigurationAsync(request, deadline: DateTime.UtcNow.AddSeconds(5));
                
                if (reply.Success)
                {
                    // Update node status
                    CpuUsage = reply.CpuUsagePercent;
                    MemoryUsage = $"{FormatBytes(reply.MemoryUsedBytes)} / {FormatBytes(reply.MemoryTotalBytes)}";
                    DiskSpace = $"{FormatBytes(reply.DiskSpaceAvailableBytes)} свободно из {FormatBytes(reply.DiskSpaceTotalBytes)}";
                    NodeStatus = "Онлайн";
                    StatusMessage = "Статус узла обновлен";
                }
                else
                {
                    StatusMessage = $"Ошибка обновления: {reply.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }
        
        private string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
        
        private void Logout()
        {
            // Close the connection
            _currentChannel?.Dispose();
            _currentChannel = null;
            _storageClient = null;
            
            // Logout from auth service
            AuthService.Logout();
            
            // Close the admin window and show login window
            foreach (Window window in Application.Current.Windows)
            {
                if (window is AdminWindow)
                {
                    window.Close();
                    
                    // Show login window
                    var loginWindow = new LoginWindow();
                    loginWindow.Show();
                    
                    // If login window is closed without successful login, exit the application
                    if (loginWindow.DialogResult != true)
                    {
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        // If login successful, show main window
                        var mainWindow = new MainWindow();
                        mainWindow.Show();
                    }
                    
                    break;
                }
            }
        }
    }
}