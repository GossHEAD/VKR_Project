using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VRK_WPF.MVVM.ViewModel;

namespace VRK_WPF.MVVM.View;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        System.Diagnostics.Debug.WriteLine($"DataContext set to: {DataContext?.GetType().Name}"); // Add this line
    
    }
    
    private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
    {
        DocumentationPage documentationPage = new DocumentationPage();
        ShowPage(documentationPage);
    }

    private void OpenAboutProgram_Click(object sender, RoutedEventArgs e)
    {
        AboutPage aboutProgramPage = new AboutPage();
        ShowPage(aboutProgramPage);
    }
    
    private void ShowPage(Page page)
    {
        Window window = new Window
        {
            Title = page.Title,
            Content = page,
            Width = 600,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.ShowDialog();
    }
}