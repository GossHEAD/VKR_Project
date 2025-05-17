using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using VKR_Core.Enums;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View
{
    public partial class MainWindow : Window
    {
        private NodeConfigurationManager _nodeConfigManager;
        private NodeProcessManager _nodeProcessManager;
        
        public MainWindow()
        {
            InitializeComponent();
            if (LogEventsTab != null)
            {
                LogEventsTab.IsEnabled = true;
            }
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
                InitializeNodeManager();
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
            
                    if (logsTab.Visibility == Visibility.Visible)
                    {
                        logsTab.IsEnabled = true;
                    }
                }
                
                UpdateStatusBarWithUserInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка настройки интерфейса: {ex.Message}");
            }
        }
        
        private TabItem? FindTabByHeader(string header)
        {
            var mainTabControl = FindName("MainTabControl") as TabControl;
            
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
        
        private void UpdateStatusBarWithUserInfo()
        {
            if (AuthService.CurrentUser == null)
                return;
                
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.StatusBarText = $"Пользователь: {AuthService.CurrentUser.FullName} | Роль: {AuthService.CurrentUser.Role}";
            }
        }

        public void ShowDocumentation()
        {
            if (MainTabControl != null && DocumentationTab != null)
            {
                MainTabControl.SelectedItem = DocumentationTab;
            }
        }
        
        public void ShowAbout()
        {
            if (MainTabControl != null && AboutTab != null)
            {
                MainTabControl.SelectedItem = AboutTab;
            }
        }
        
        private void InitializeNodeManager()
        {
            _nodeConfigManager = new NodeConfigurationManager();
            _nodeProcessManager = new NodeProcessManager(_nodeConfigManager);
            
            _nodeProcessManager.NodeOutputReceived += NodeProcess_OutputReceived;
            _nodeProcessManager.NodeErrorReceived += NodeProcess_ErrorReceived;
            _nodeProcessManager.NodeExited += NodeProcess_Exited;
            
            UpdateNodeStatusUI();
            PopulateConfigDropdown();
        }

        private void PopulateConfigDropdown()
        {
            var configs = _nodeConfigManager.GetAvailableConfigs();
            cmbNodeConfigs.ItemsSource = configs;
            
            var currentConfig = configs.FirstOrDefault(c => c.IsCurrentNode);
            if (currentConfig != null)
            {
                cmbNodeConfigs.SelectedItem = currentConfig;
            }
        }

        private void UpdateNodeStatusUI()
        {
            bool isRunning = _nodeProcessManager.IsNodeRunning;
            
            cmbNodeConfigs.Text = isRunning ? "Запущен" : "Не запущен";
            cmbNodeConfigs.Foreground = isRunning ? Brushes.Green : Brushes.Red;
            
            btnStartNode.IsEnabled = !isRunning;
            btnStopNode.IsEnabled = isRunning;
            
            txtNodeId.Text = _nodeConfigManager.CurrentNodeId;
        }

        private void NodeProcess_OutputReceived(object sender, string data)
        {
            Dispatcher.Invoke(() =>
            {
                txtNodeOutput.AppendText(data + Environment.NewLine);
                txtNodeOutput.ScrollToEnd();
            });
        }

        private void NodeProcess_ErrorReceived(object sender, string data)
        {
            Dispatcher.Invoke(() =>
            {
                txtNodeOutput.AppendText($"Ошибка: {data}" + Environment.NewLine);
                txtNodeOutput.ScrollToEnd();
            });
        }

        private void NodeProcess_Exited(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                txtNodeOutput.AppendText("Процесс узла завершился." + Environment.NewLine);
                UpdateNodeStatusUI();
            });
        }

        private void BtnStartNode_Click(object sender, RoutedEventArgs e)
        {
            if (_nodeProcessManager.StartNode())
            {
                txtNodeStatusBar.Text = "Узел успешно запущен";
                UpdateNodeStatusUI();
            }
            else
            {
                txtNodeStatusBar.Text = "Ошибка запуска узла";
            }
        }

        private void BtnStopNode_Click(object sender, RoutedEventArgs e)
        {
            _nodeProcessManager.StopNode();
            txtNodeStatusBar.Text = "Узел остановлен";
            UpdateNodeStatusUI();
        }

        private void CmbNodeConfigs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbNodeConfigs.SelectedItem is NodeConfig config)
            {
                _nodeConfigManager.SetCurrentConfig(config.ConfigPath);
                txtNodeId.Text = _nodeConfigManager.CurrentNodeId;
                txtNodeStatusBar.Text = $"Выбрана конфигурация: {config.NodeId}";
            }
        }

        private void BtnCreateConfig_Click(object sender, RoutedEventArgs e)
        {
            string configPath = _nodeConfigManager.CreateDefaultConfig();
            txtNodeStatusBar.Text = $"Создана новая конфигурация: {configPath}";
            PopulateConfigDropdown();
        }

        private void BtnEditConfig_Click(object sender, RoutedEventArgs e)
        {
            if (cmbNodeConfigs.SelectedItem is NodeConfig config)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = config.ConfigPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка открытия конфигурации: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_nodeProcessManager.IsNodeRunning)
            {
                _nodeProcessManager.StopNode();
            }
            
            base.OnClosing(e);
        }
    }
}