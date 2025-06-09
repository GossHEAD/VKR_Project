using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF.MVVM.ViewModel
{
    public partial class LoginWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _username = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isErrorVisible = false;
        
        [ObservableProperty]
        private bool _isLoggingIn = false;
        
        public bool LoginSuccessful { get; private set; } = false;

        public event EventHandler? LoginSucceeded;

        public LoginWindowViewModel()
        {
            LoginCommand = new RelayCommand<object>(ExecuteLogin, CanExecuteLogin);
        }

        public RelayCommand<object> LoginCommand { get; }

        private bool CanExecuteLogin(object? parameter) => 
            !string.IsNullOrWhiteSpace(Username) && 
            !IsLoggingIn;

        private async void ExecuteLogin(object? parameter)
        {
            IsLoggingIn = true;
            IsErrorVisible = false;
            ErrorMessage = string.Empty;
            
            try
            {
                string password = "";
                if (parameter is PasswordBox passwordBox)
                {
                    password = passwordBox.Password;
                }
                
                if (string.IsNullOrWhiteSpace(password))
                {
                    ErrorMessage = "Введите пароль";
                    IsErrorVisible = true;
                    return;
                }
                
                bool loginSuccess = await AuthService.LoginAsync(Username, password);
                
                if (loginSuccess)
                {
                    LoginSuccessful = true;
                    
                    var currentUser = AuthService.CurrentUser;
                    if (currentUser != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Пользователь авторизован: {currentUser.Username} (Роль: {currentUser.Role})");
                    }
                    
                    LoginSucceeded?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ErrorMessage = "Неверное имя пользователя или пароль";
                    IsErrorVisible = true;
                    LoginSuccessful = false;
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка авторизации: {ex.Message}";
                IsErrorVisible = true;
                LoginSuccessful = false;
            }
            finally
            {
                IsLoggingIn = false;
                LoginCommand.NotifyCanExecuteChanged();
            }
        }
    }
}