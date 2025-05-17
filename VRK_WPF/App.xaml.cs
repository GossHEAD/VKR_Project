using System.Windows;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.View;

namespace VRK_WPF;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var loginWindow = new LoginWindow();
        Current.MainWindow = loginWindow;
    
        loginWindow.Loaded += (s, args) => {
            loginWindow.Activate(); 
        };

        loginWindow.LoginSucceeded += (s, args) => {
            Dispatcher.BeginInvoke(new Action(() => {
                try
                {
                    if (AuthService.CurrentUser != null && 
                        AuthService.CurrentUser.Role == VKR_Core.Enums.UserRole.Administrator)
                    {
                        var adminWindow = new AdminWindow();
                        Current.MainWindow = adminWindow;
                        adminWindow.Show();
                    }
                    else
                    {
                        var mainWindow = new MainWindow();
                        Current.MainWindow = mainWindow;
                        mainWindow.Show();
                    }
                
                    loginWindow.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при создании окна: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                }
            }));
        };

        loginWindow.Show(); 
    }
}