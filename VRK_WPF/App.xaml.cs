using System.Windows;
using VRK_WPF.MVVM.View;
using VRK_WPF.MVVM.Services;

namespace VRK_WPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            ShowLoginWindow();
        }
        
        private void ShowLoginWindow()
        {
            try
            {
                var loginWindow = new LoginWindow();
                
                loginWindow.LoginSucceeded += (sender, args) =>
                {
                    loginWindow.Close();
                };
                
                loginWindow.Closed += (sender, args) =>
                {
                    if (AuthService.CurrentUser == null)
                    {
                        Shutdown();
                    }
                };
                
                loginWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка при запуске приложения: {ex.Message}",
                    "Ошибка при запуске",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                    
                Shutdown();
            }
        }
        
        protected override void OnExit(ExitEventArgs e)
        {
            AuthService.Logout();
            base.OnExit(e);
        }
    }
}