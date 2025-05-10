using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            
            _viewModel.LoginSucceeded += (s, e) => {
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            };
            
            DataContext = _viewModel;
            
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
}