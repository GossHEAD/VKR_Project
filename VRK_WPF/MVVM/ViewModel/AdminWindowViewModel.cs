using System.Collections.ObjectModel;
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

        [ObservableProperty]
        private ObservableCollection<NodeViewModel> _availableNodes = new();

        [ObservableProperty]
        private NodeViewModel? _selectedNode;

        [ObservableProperty]
        private string _selectedNodeName = "Не подключен";

        [ObservableProperty]
        private string _currentUserInfo = "Не аутентифицирован";

        [ObservableProperty]
        private string _statusMessage = "Готово";

        [ObservableProperty]
        private bool _isProcessing;

        [ObservableProperty]
        private string _selectedTable = "Files";

        [ObservableProperty]
        private string _nodeStatus = "Не подключен";

        [ObservableProperty]
        private double _cpuUsage;

        [ObservableProperty]
        private string _memoryUsage = "Н/Д";

        [ObservableProperty]
        private string _diskSpace = "Н/Д";


        public StorageService.StorageServiceClient? StorageClient => _storageClient;

        public AdminWindowViewModel(ILogger<AdminWindowViewModel>? logger = null)
        {
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AdminWindowViewModel>.Instance;

            if (AuthService.CurrentUser != null)
            {
                CurrentUserInfo = $"{AuthService.CurrentUser.FullName} ({AuthService.CurrentUser.Role})";
            }

            LogoutCommand = new RelayCommand(Logout);
            ConnectToNodeCommand = new RelayCommand(async () => await ConnectToNodeAsync(), CanConnectToNode);
            RefreshStatusCommand = new RelayCommand(async () => await RefreshNodeStatusAsync(), () => _storageClient != null);

            LoadAvailableNodes();
        }

        public RelayCommand LogoutCommand { get; }
        public RelayCommand ConnectToNodeCommand { get; }
        public RelayCommand RefreshStatusCommand { get; }

        private void LoadAvailableNodes()
        {
            AvailableNodes.Clear();
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node1", Address = "http://localhost:5001" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node2", Address = "http://localhost:5002" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node3", Address = "http://localhost:5003" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node4", Address = "http://localhost:5004" });
            AvailableNodes.Add(new NodeViewModel { NodeId = "Node5", Address = "http://localhost:5005" });

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
                MessageBox.Show("Пожалуйста, выберите узел для подключения", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsProcessing = true;
            StatusMessage = $"Подключение к узлу {SelectedNode.NodeId}...";

            try
            {
                _currentChannel?.Dispose();
                _currentChannel = null;
                _storageClient = null;

                _currentChannel = GrpcChannel.ForAddress(SelectedNode.Address);
                _storageClient = new StorageService.StorageServiceClient(_currentChannel);

                var request = new GetNodeConfigurationRequest();
                var reply = await _storageClient.GetNodeConfigurationAsync(request, deadline: DateTime.UtcNow.AddSeconds(5));

                if (reply.Success)
                {
                    SelectedNodeName = $"Узел: {reply.NodeId}";
                    StatusMessage = $"Подключено к узлу {reply.NodeId}";

                    CpuUsage = reply.CpuUsagePercent;
                    MemoryUsage = $"{FormatBytes(reply.MemoryUsedBytes)} / {FormatBytes(reply.MemoryTotalBytes)}";
                    DiskSpace = $"{FormatBytes(reply.DiskSpaceAvailableBytes)} свободно из {FormatBytes(reply.DiskSpaceTotalBytes)}";
                    NodeStatus = "Онлайн";

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
            if (bytes <= 0) return "0 Б";

            string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
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
            _currentChannel?.Dispose();
            _currentChannel = null;
            _storageClient = null;

            AuthService.Logout();

            foreach (Window window in Application.Current.Windows)
            {
                if (window is AdminWindow)
                {
                    window.Close();

                    var loginWindow = new LoginWindow();
                    loginWindow.Show();

                    if (loginWindow.DialogResult != true)
                    {
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        var mainWindow = new MainWindow();
                        mainWindow.Show();
                    }

                    break;
                }
            }
        }
    }
}