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
    }
    private void ViewTransactionHistory_Click(object sender, RoutedEventArgs e)
    {
        var transactionWindow = new TransactionHistoryWindow();
        transactionWindow.Show();
    }
    
    
    private void OpenNodeSettings_Click(object sender, RoutedEventArgs e)
    {
        var transactionWindow = new NodeSettingsView();
        transactionWindow.Show();
    }
}