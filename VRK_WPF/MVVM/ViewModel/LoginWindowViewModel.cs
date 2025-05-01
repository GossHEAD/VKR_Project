using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRK_WPF.MVVM.ViewModel
{
    public partial class LoginWindowModel : ObservableObject
    {
        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        [ObservableProperty]
        private bool isErrorVisible = false;

        public LoginWindowModel()
        {
            LoginCommand = new RelayCommand(ExecuteLogin, CanExecuteLogin);
        }

        public RelayCommand LoginCommand { get; }

        private bool CanExecuteLogin() => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

        private void ExecuteLogin()
        {
            // Простая проверка (замени на проверку через БД)
            if (Username == "admin" && Password == "12345")
            {
                MessageBox.Show("Вход выполнен успешно!", "Авторизация", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Windows[0]?.Close();
            }
            else
            {
                ErrorMessage = "Неверный логин или пароль!";
                IsErrorVisible = true;
            }
        }
    }
}