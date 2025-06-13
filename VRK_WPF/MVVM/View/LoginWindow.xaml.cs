// Updated LoginWindow.xaml.cs

using System;
using System.Windows;
using System.Windows.Controls;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel;
using VKR_Core.Enums;

namespace VRK_WPF.MVVM.View
{
    public partial class LoginWindow : Window
    {
        private readonly LoginWindowViewModel _viewModel;
        public event EventHandler<LoginEventArgs> LoginSucceeded;
        
        public LoginWindow()
        {
            InitializeComponent();
            _viewModel = new LoginWindowViewModel();
            DataContext = _viewModel;
            
            _viewModel.LoginSucceeded += ViewModel_LoginSucceeded;
            
            Loaded += (s, e) => {
                UsernameTextBox.Focus();
            };
        }
        
        private void ViewModel_LoginSucceeded(object sender, EventArgs e)
        {
            if (AuthService.CurrentUser != null)
            {
                // Show appropriate window based on user role
                ShowWindowBasedOnRole();
                
                // Fire event for any listeners
                LoginSucceeded?.Invoke(this, new LoginEventArgs(
                    AuthService.CurrentUser.Username,
                    AuthService.CurrentUser.Role));
                
                // Close login window
                DialogResult = true;
            }
        }
        
        private void ShowWindowBasedOnRole()
        {
            if (AuthService.CurrentUser == null) return;
            
            try
            {
                Window targetWindow = null;
                
                // Determine which window to show based on role
                switch (AuthService.CurrentUser.Role)
                {
                    case UserRole.Administrator:
                        targetWindow = new AdminWindow();
                        break;
                        
                    case UserRole.ITSpecialist:
                    default:
                        targetWindow = new MainWindow();
                        break;
                }
                
                if (targetWindow != null)
                {
                    // Show the appropriate window
                    targetWindow.Show();
                    
                    // Show welcome message
                    string roleName = AuthService.CurrentUser.Role.ToString();
                    string windowType = targetWindow is AdminWindow ? "административную панель" : "главное окно";
                    
                    MessageBox.Show(
                        $"Добро пожаловать, {AuthService.CurrentUser.FullName}!\n" +
                        $"Роль: {roleName}\n" +
                        $"Открывается {windowType}.",
                        "Вход выполнен успешно",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка при открытии окна приложения: {ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                    
                // Fallback to main window
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }
        
        private void DemoUser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                var parts = tag.Split('|');
                if (parts.Length == 2)
                {
                    var username = parts[0];
                    var password = parts[1];
                    
                    // Fill the form
                    UsernameTextBox.Text = username;
                    PasswordBox.Password = password;
                    
                    // Update the view model
                    _viewModel.Username = username;
                    
                    // Show which window will open
                    string windowType = username == "admin" ? "административную панель" : "главное окно";
                    
                    // Optionally auto-login
                    var result = MessageBox.Show(
                        $"Автоматически войти как '{username}'?\n" +
                        $"Откроется {windowType}.", 
                        "Автовход", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        _viewModel.LoginCommand.Execute(PasswordBox);
                    }
                }
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _viewModel.LoginSucceeded -= ViewModel_LoginSucceeded;
            base.OnClosed(e);
        }
    }
    
    public class LoginEventArgs : EventArgs
    {
        public string Username { get; set; } = string.Empty;
        public UserRole Role { get; set; }
    
        public LoginEventArgs(string username, UserRole role)
        {
            Username = username;
            Role = role;
        }
    }
}