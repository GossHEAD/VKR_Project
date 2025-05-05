using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly LoginWindowViewModel _viewModel;
        
        public LoginWindow()
        {
            InitializeComponent();
            _viewModel = new LoginWindowViewModel();
            
            _viewModel.LoginSucceeded += OnLoginSucceeded;
            
            DataContext = _viewModel;
            
            Loaded += (s, e) => {
                UsernameTextBox.Focus();
            };
        }
        
        private void OnLoginSucceeded(object sender, EventArgs e)
        {
            this.DialogResult = true;
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _viewModel.LoginSucceeded -= OnLoginSucceeded;
            
            base.OnClosed(e);
        }
    }
}