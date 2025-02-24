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
/// Interaction logic for TransactionWindow.xaml
/// </summary>
public partial class TransactionHistoryWindow : Window
{
    public TransactionHistoryWindow()
    {
        InitializeComponent();
        DataContext = new TransactionHistoryViewModel();
    }
}