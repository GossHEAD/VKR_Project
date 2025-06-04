using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
            LoginCommand = new RelayCommand(ExecuteLogin, CanExecuteLogin);
        }

        public RelayCommand LoginCommand { get; }

        private bool CanExecuteLogin() => 
            !string.IsNullOrWhiteSpace(Username) && 
            !IsLoggingIn;

        private void ExecuteLogin()
        {
            IsLoggingIn = true;
            IsErrorVisible = false;
            ErrorMessage = string.Empty;
            
            try
            {
                if (AuthService.Login(Username, out string errorMsg))
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
                    ErrorMessage = string.IsNullOrEmpty(errorMsg) ? "Неверное имя пользователя" : errorMsg;
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