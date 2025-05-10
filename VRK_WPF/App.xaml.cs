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
                // try {
                //     
                //     var mainWindow = new MainWindow();
                //     Current.MainWindow = mainWindow; 
                //     mainWindow.Show();
                //
                //     
                //     loginWindow.Close();
                // }
                // catch (Exception ex) {
                //     MessageBox.Show($"Error creating main window: {ex.Message}", "Error", 
                //         MessageBoxButton.OK, MessageBoxImage.Error);
                //     Shutdown(1);
                // }
                var mainWindow = new MainWindow();
                Current.MainWindow = mainWindow; 
                mainWindow.Show();
                
                    
                loginWindow.Close();
            }));
        };
    
        
        loginWindow.Show(); 
    }
}