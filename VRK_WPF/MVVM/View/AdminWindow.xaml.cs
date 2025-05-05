using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VRK_WPF.MVVM.View.AdminPages;
using VRK_WPF.MVVM.ViewModel;
using VRK_WPF.Services;

namespace VRK_WPF.MVVM.View
{
    /// <summary>
    /// Interaction logic for AdminWindow.xaml
    /// </summary>
    public partial class AdminWindow : Window
    {
        private readonly AdminWindowViewModel _viewModel;
        
        // Pages for navigation
        private readonly NodeStatusPage _nodeStatusPage;
        private readonly DatabaseManagementPage _databaseManagementPage;
        private readonly NodeConfigPage _nodeConfigPage;
        private readonly LogViewerPage _logViewerPage;
        private readonly SimulationPage _simulationPage;
        
        public AdminWindow()
        {
            InitializeComponent();
            
            // Check if user is authorized as admin
            if (AuthService.CurrentUser == null || !AuthService.HasRole(Model.UserRole.Administrator))
            {
                MessageBox.Show("Для доступа к административной панели необходимы права администратора.",
                                "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }
            
            // Create and store pages
            _nodeStatusPage = new NodeStatusPage();
            _databaseManagementPage = new DatabaseManagementPage();
            _nodeConfigPage = new NodeConfigPage();
            _logViewerPage = new LogViewerPage();
            _simulationPage = new SimulationPage();
            
            // Initialize ViewModel
            _viewModel = new AdminWindowViewModel();
            DataContext = _viewModel;
            
            // Set initial page
            AdminContentFrame.Content = _nodeStatusPage;
            
            // Subscribe to window events
            Loaded += AdminWindow_Loaded;
            Closing += AdminWindow_Closing;
        }
        
        private void AdminWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initial setup after window is loaded
            Title = $"Административная панель - {AuthService.CurrentUser?.FullName ?? "Неизвестный пользователь"}";
        }
        
        private void AdminWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cleanup resources if needed
        }
        
        private void NavMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NavMenu.SelectedItem == null) return;
            
            // Navigate to selected page
            switch (NavMenu.SelectedIndex)
            {
                case 0:
                    AdminContentFrame.Content = _nodeStatusPage;
                    break;
                case 1:
                    AdminContentFrame.Content = _databaseManagementPage;
                    break;
                case 2:
                    AdminContentFrame.Content = _nodeConfigPage;
                    break;
                case 3:
                    AdminContentFrame.Content = _logViewerPage;
                    break;
                case 4:
                    AdminContentFrame.Content = _simulationPage;
                    break;
                default:
                    break;
            }
        }
    }
}