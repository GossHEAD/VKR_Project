using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.View.AdminPages;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View
{
    public partial class AdminWindow : Window
    {
        private readonly AdminWindowViewModel _viewModel;
        
        private DatabaseManagementPage _databaseManagementPage;
        private NodeConfigPage _nodeConfigPage;
        private LogViewerPage _logViewerPage;
        private bool _isInitialized = false;
        
        public AdminWindow()
        {
            InitializeComponent();
            
            if (AuthService.CurrentUser == null || 
                !AuthService.HasRole(VKR_Core.Enums.UserRole.Administrator))
            {
                MessageBox.Show("Для доступа к административной панели необходимы права администратора.",
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
            
            // Set initial page
            AdminContentFrame.Content = _nodeConfigPage;
            
            _isInitialized = true;
        }
        
        private void AdminWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        }
        
        private void InitializePages()
        {
            _databaseManagementPage = new DatabaseManagementPage();
            _nodeConfigPage = new NodeConfigPage();
            _logViewerPage = new LogViewerPage();
        }
        
        private void NavMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip if not initialized or NavMenu selection is null
            if (!_isInitialized || NavMenu.SelectedItem == null) 
                return;
            
            // Make sure pages are initialized
            if (_databaseManagementPage == null || _nodeConfigPage == null || _logViewerPage == null)
            {
                InitializePages();
            }
            
            switch (NavMenu.SelectedIndex)
            {
                case 0: // Node Status
                    AdminContentFrame.Content = _nodeConfigPage;
                    break;
                case 1: // Database Management
                    AdminContentFrame.Content = _databaseManagementPage;
                    break;
                case 2: // Node Configuration
                    AdminContentFrame.Content = _nodeConfigPage;
                    break;
                case 3: // Logs
                    AdminContentFrame.Content = _logViewerPage;
                    break;
                case 4:
                    MessageBox.Show("Simulation functionality not yet implemented.", 
                        "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                default:
                    break;
            }
        }
    }
}