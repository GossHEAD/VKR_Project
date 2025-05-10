using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using VKR_Core.Enums;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            try
            {
                InitializeFrames();
        
                string roleName = AuthService.CurrentUser.Role.ToString();
                MessageBox.Show($"Добро пожаловать, {AuthService.CurrentUser.FullName}!\nРоль: {roleName}", 
                    "Успешная авторизация", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
        
                ILogger<MainWindowViewModel>? logger = null;
        
                DataContext = new MainWindowViewModel(logger)
                {
                    CurrentUserName = AuthService.CurrentUser.FullName,
                    CurrentUserRole = AuthService.CurrentUser.Role.ToString()
                };
        
                ConfigureUIBasedOnUserRole();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при запуске приложения: {ex.Message}", 
                    "Ошибка", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }
        
        private void HandleLogin()
        {
            var loginWindow = new LoginWindow();
            bool? loginResult = loginWindow.ShowDialog();
            
            if (loginResult != true)
            {
                Application.Current.Shutdown();
                return;
            }
            
            if (AuthService.CurrentUser == null)
            {
                MessageBox.Show("Ошибка авторизации. Приложение будет закрыто.", 
                    "Ошибка авторизации", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }
        
        /// <summary>
        /// Initialize frames with their content pages
        /// </summary>
        private void InitializeFrames()
        {
            try
            {
                var documentationPage = new DocumentationPage();
                var aboutPage = new AboutPage();
                
                if (DocumentationFrame != null)
                {
                    DocumentationFrame.Content = documentationPage;
                }
                
                if (AboutFrame != null)
                {
                    AboutFrame.Content = aboutPage;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing frames: {ex.Message}");
                MessageBox.Show($"Ошибка при инициализации страниц: {ex.Message}", 
                    "Ошибка", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Configures the UI elements based on the current user's role
        /// </summary>
        private void ConfigureUIBasedOnUserRole()
        {
            try
            {
                var userRole = AuthService.CurrentUser?.Role ?? UserRole.ITSpecialist;
                
                TabItem? simulationTab = FindTabByHeader("Симуляция");
                if (simulationTab != null)
                {
                    simulationTab.Visibility = AuthService.CanAccessModule("Simulation") 
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                
                TabItem? settingsTab = FindTabByHeader("Настройки");
                if (settingsTab != null)
                {
                    settingsTab.Visibility = AuthService.CanAccessModule("Settings") 
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                
                TabItem? logsTab = FindTabByHeader("Журнал событий");
                if (logsTab != null)
                {
                    logsTab.Visibility = AuthService.CanAccessModule("Logs") 
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                
                UpdateStatusBarWithUserInfo();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error configuring UI: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a tab by its header text
        /// </summary>
        private TabItem? FindTabByHeader(string header)
        {
            var mainTabControl = this.FindName("MainTabControl") as TabControl;
            
            if (mainTabControl != null)
            {
                foreach (TabItem tab in mainTabControl.Items)
                {
                    if (tab.Header != null && tab.Header.ToString() == header)
                    {
                        return tab;
                    }
                }
            }
            
            return null;
        }

        /// <summary>
        /// Updates the status bar with user information
        /// </summary>
        private void UpdateStatusBarWithUserInfo()
        {
            if (AuthService.CurrentUser == null)
                return;
                
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.StatusBarText = $"Пользователь: {AuthService.CurrentUser.FullName} | Роль: {AuthService.CurrentUser.Role}";
            }
        }
        
        /// <summary>
        /// Switch to the Documentation tab
        /// </summary>
        public void ShowDocumentation()
        {
            if (MainTabControl != null && DocumentationTab != null)
            {
                MainTabControl.SelectedItem = DocumentationTab;
            }
        }
        
        /// <summary>
        /// Switch to the About tab
        /// </summary>
        public void ShowAbout()
        {
            if (MainTabControl != null && AboutTab != null)
            {
                MainTabControl.SelectedItem = AboutTab;
            }
        }
        
        private void ShowPage(Page page)
        {
            Window window = new Window
            {
                Title = page.Title,
                Content = page,
                Width = 650,
                Height = 550,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize,
                SizeToContent = SizeToContent.Manual,
                Owner = this 
            };
            
            window.ShowDialog();
        }
        
        public void HandleMenuActions(string action)
        {
            switch (action)
            {
                case "OpenDocumentation":
                    ShowDocumentation();
                    break;
                case "OpenAbout":
                    ShowAbout();
                    break;
                default:
                    break;
            }
        }
    }
}