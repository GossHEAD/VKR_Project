using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View
{
    public partial class LoginWindow : Window
    {
        private readonly LoginWindowViewModel _viewModel;
        public event EventHandler LoginSucceeded;
        
        public LoginWindow()
        {
            InitializeComponent();
            _viewModel = new LoginWindowViewModel();
            DataContext = _viewModel;
            
            _viewModel.LoginSucceeded += (sender, e) =>
            {
                if (AuthService.CurrentUser != null)
                {
                    LoginSucceeded?.Invoke(this, new LoginEventArgs(
                        AuthService.CurrentUser.Username,
                        AuthService.CurrentUser.Role));
                }
            
                if (_viewModel.LoginSuccessful)
                {
                    DialogResult = true;
                }
            };
            
            
            Loaded += (s, e) => {
                UsernameTextBox.Focus();
            };
        }
        
        private void OnLoginSucceeded(object sender, EventArgs e)
        {
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _viewModel.LoginSucceeded -= OnLoginSucceeded;
            
            base.OnClosed(e);
        }
    }
    
    public class LoginEventArgs : EventArgs
    {
        public string Username { get; set; } = string.Empty;
        public VKR_Core.Enums.UserRole Role { get; set; }
    
        public LoginEventArgs(string username, VKR_Core.Enums.UserRole role)
        {
            Username = username;
            Role = role;
        }
    }
}