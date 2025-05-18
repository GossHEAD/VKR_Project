using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.View.AdminPages;
using VRK_WPF.MVVM.ViewModel;
using VKR_Core.Enums;
using System.ComponentModel;

namespace VRK_WPF.MVVM.View
{
    public partial class AdminWindow : Window
    {
        private readonly AdminWindowViewModel _viewModel;

        private DatabaseManagementPage _databaseManagementPage;
        private NodeConfigPage _nodeConfigPage;
        private LogViewerPage _logViewerPage;
        private NodeStatusPage _nodeStatusPage;
        private bool _isInitialized = false;

        public AdminWindow()
        {
            InitializeComponent();

            if (AuthService.CurrentUser == null ||
                !AuthService.HasRole(UserRole.Administrator))
            {
                MessageBox.Show("Для доступа к административной панели требуются права администратора.",
                    "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _viewModel = new AdminWindowViewModel();
            DataContext = _viewModel;

            Loaded += AdminWindow_Loaded;
            Closing += AdminWindow_Closing;
        }

        private void AdminWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"Административная панель - {AuthService.CurrentUser?.FullName ?? "Неизвестный пользователь"}";

            InitializePages();

            AdminContentFrame.Content = _nodeStatusPage;

            _isInitialized = true;
        }

        private void AdminWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_databaseManagementPage != null &&
                _databaseManagementPage.DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }
        }

        private void InitializePages()
        {
            try
            {
                _nodeStatusPage = new NodeStatusPage();
                _databaseManagementPage = new DatabaseManagementPage();
                _nodeConfigPage = new NodeConfigPage();
                _logViewerPage = new LogViewerPage();

                if (_viewModel.StorageClient != null)
                {
                    _logViewerPage.SetClient(_viewModel.StorageClient);
                    _nodeConfigPage.SetClient(_viewModel.StorageClient);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации страниц администратора: {ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NavMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || NavMenu.SelectedItem == null)
                return;

            if (_databaseManagementPage == null || _nodeConfigPage == null ||
                _logViewerPage == null || _nodeStatusPage == null)
            {
                InitializePages();
            }

            _viewModel.StatusMessage = $"Переход к {((ListBoxItem)NavMenu.SelectedItem).Content}...";

            switch (NavMenu.SelectedIndex)
            {
                case 0:
                    AdminContentFrame.Content = _nodeStatusPage;
                    _viewModel.StatusMessage = "Статус узла";
                    break;
                case 1:
                    AdminContentFrame.Content = _databaseManagementPage;
                    _viewModel.StatusMessage = "Управление базой данных";
                    break;
                case 2:
                    AdminContentFrame.Content = _nodeConfigPage;
                    _viewModel.StatusMessage = "Конфигурация узла";
                    break;
                case 3:
                    AdminContentFrame.Content = _logViewerPage;
                    _viewModel.StatusMessage = "Журналы событий";
                    break;
                default:
                    AdminContentFrame.Content = _nodeStatusPage;
                    _viewModel.StatusMessage = "Статус узла";
                    break;
            }
        }
    }
}