using System.Windows;
using System.Windows.Controls;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View;

public partial class LoginWindow : Window
{
    private readonly LoginWindowModel _viewModel;
    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = new LoginWindowModel();
        DataContext = _viewModel;
    }
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = ((PasswordBox)sender).Password;
    }
}