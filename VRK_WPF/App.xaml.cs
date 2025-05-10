using System.Windows;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.View;

namespace VRK_WPF;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            var loginWindow = new LoginWindow();
        
            loginWindow.SizeToContent = SizeToContent.WidthAndHeight;
        
            loginWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        
            bool? loginResult = loginWindow.ShowDialog();
        
            if (loginResult != true)
            {
                Shutdown();
                return;
            }
        
            if (AuthService.CurrentUser == null)
            {
                MessageBox.Show("Ошибка авторизации. Пользователь не найден. Приложение будет закрыто.", 
                    "Ошибка авторизации", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }
        
            var mainWindow = new MainWindow();
        
            this.MainWindow = mainWindow;
        
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при запуске приложения: {ex.Message}\n\nДетали: {ex.StackTrace}", 
                "Ошибка", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
            Shutdown();
        }
    }
    // In App.xaml.cs
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
    
        Window tempWindow = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden
        };
    
        tempWindow.Show();
    
        try
        {
            var loginWindow = new LoginWindow
            {
                Owner = tempWindow,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
        
            bool? loginResult = loginWindow.ShowDialog();
        
            if (loginResult != true || AuthService.CurrentUser == null)
            {
                tempWindow.Close();
                Shutdown();
                return;
            }
        
            // Create the main window
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
        
            // Close the temp window and show the main window
            tempWindow.Close();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при запуске приложения: {ex.Message}", 
                "Ошибка", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
            tempWindow.Close();
            Shutdown();
        }
    }
}