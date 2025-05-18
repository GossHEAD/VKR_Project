using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VRK_WPF.MVVM.View.UserPages;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel;
using Microsoft.Extensions.Logging;
using VKR_Core.Enums;

namespace VRK_WPF.MVVM.View
{
    public partial class MainWindow : Window
    {
        private NodeConfigurationManager _nodeConfigManager;
        private NodeProcessManager _nodeProcessManager;
        private Button _currentActiveButton;

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                if (AuthService.CurrentUser == null)
                {
                    HandleLogin();
                }

                InitializeNodeManager();

                ConfigureUIBasedOnUserRole();

                ILogger<MainWindowViewModel>? logger = null;
                DataContext = new MainWindowViewModel(logger)
                {
                    CurrentUserName = AuthService.CurrentUser?.FullName ?? "Гость",
                    CurrentUserRole = AuthService.CurrentUser?.Role.ToString() ?? "Гость"
                };

                SetActiveNavButton(FilesButton);
                NavigateToPage("Файлы");

                string roleName = AuthService.CurrentUser?.Role.ToString() ?? "Гость";
                MessageBox.Show($"Добро пожаловать, {AuthService.CurrentUser?.FullName ?? "Гость"}!\nРоль: {roleName}",
                    "Вход выполнен успешно",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                ExitButton.Click += ExitButton_Click;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации приложения: {ex.Message}",
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
                MessageBox.Show("Аутентификация не удалась. Приложение будет закрыто.",
                    "Ошибка аутентификации",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void NavigationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                SetActiveNavButton(button);
                string pageName = button.Content.ToString();
                NavigateToPage(pageName);
            }
        }

        private void SetActiveNavButton(Button button)
        {
            if (_currentActiveButton != null)
            {
                _currentActiveButton.Background = Brushes.Transparent;
                _currentActiveButton.Foreground = Brushes.Black;
                _currentActiveButton.FontWeight = FontWeights.Normal;
            }

            button.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeights.SemiBold;
            _currentActiveButton = button;
        }

        private void NavigateToPage(string pageName)
        {
            Page page = null;

            switch (pageName)
            {
                case "Файлы":
                    page = new FilesPage();
                    break;
                case "Статус сети":
                    page = new NetworkPage();
                    break;
                case "Настройки":
                    var settingsPage = new SettingsPage();
                    if (_nodeConfigManager != null && _nodeProcessManager != null)
                    {
                        settingsPage.SetNodeManager(_nodeConfigManager, _nodeProcessManager);
                    }
                    page = settingsPage;
                    break;
                case "Симуляция":
                    page = new SimulationPage();
                    break;
                case "Аналитика":
                    page = new AnalyticsPage();
                    break;
                case "Документация":
                    page = new DocumentationPage();
                    break;
                case "О программе":
                    page = new AboutPage();
                    break;
                default:
                    page = new FilesPage();
                    break;
            }

            if (page != null)
            {
                page.DataContext = DataContext;
                ContentFrame.Navigate(page);
            }
        }

        private void ConfigureUIBasedOnUserRole()
        {
            try
            {
                var userRole = AuthService.CurrentUser?.Role ?? UserRole.ITSpecialist;

                SimulationButton.Visibility = AuthService.CanAccessModule("Simulation")
                    ? Visibility.Visible : Visibility.Collapsed;

                SettingsButton.Visibility = AuthService.CanAccessModule("Settings")
                    ? Visibility.Visible : Visibility.Collapsed;

                AnalyticsButton.Visibility = AuthService.CanAccessModule("Logs")
                    ? Visibility.Visible : Visibility.Collapsed;

                UpdateStatusBarWithUserInfo();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error configuring UI: {ex.Message}");
            }
        }

        private void UpdateStatusBarWithUserInfo()
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                if (!string.IsNullOrEmpty(viewModel.CurrentUserName))
                {
                    viewModel.StatusBarText = $"Пользователь: {viewModel.CurrentUserName} | Роль: {viewModel.CurrentUserRole} | Готов";
                }
                else
                {
                    viewModel.StatusBarText = "Готов";
                }
            }
        }

        private void InitializeNodeManager()
        {
            _nodeConfigManager = new NodeConfigurationManager();
            _nodeProcessManager = new NodeProcessManager(_nodeConfigManager);

            _nodeProcessManager.NodeOutputReceived += NodeProcess_OutputReceived;
            _nodeProcessManager.NodeErrorReceived += NodeProcess_ErrorReceived;
            _nodeProcessManager.NodeExited += NodeProcess_Exited;
        }

        private void NodeProcess_OutputReceived(object sender, string data)
        {
            Dispatcher.Invoke(() =>
            {
                if (ContentFrame.Content is SettingsPage settingsPage)
                {
                    settingsPage.AddNodeOutput(data);
                }
            });
        }

        private void NodeProcess_ErrorReceived(object sender, string data)
        {
            Dispatcher.Invoke(() =>
            {
                if (ContentFrame.Content is SettingsPage settingsPage)
                {
                    settingsPage.AddNodeError(data);
                }
            });
        }

        private void NodeProcess_Exited(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (ContentFrame.Content is SettingsPage settingsPage)
                {
                    settingsPage.NotifyNodeExited();
                }
            });
        }

        public void ShowDocumentation()
        {
            SetActiveNavButton(DocumentationButton);
            NavigateToPage("Документация");
        }

        public void ShowAbout()
        {
            SetActiveNavButton(AboutButton);
            NavigateToPage("О программе");
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_nodeProcessManager != null && _nodeProcessManager.IsNodeRunning)
            {
                _nodeProcessManager.StopNode();
            }

            if (DataContext is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }

            base.OnClosing(e);
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}