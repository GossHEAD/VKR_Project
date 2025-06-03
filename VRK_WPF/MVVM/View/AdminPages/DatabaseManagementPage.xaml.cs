using System.Windows.Controls;
using VRK_WPF.MVVM.Services;
using VRK_WPF.MVVM.ViewModel.AdminViewModels;

namespace VRK_WPF.MVVM.View.AdminPages;

public partial class DatabaseManagementPage : Page
{
    private readonly DatabaseManagementViewModel _viewModel;
    public DatabaseManagementPage()
    {
        InitializeComponent();
        _viewModel = new DatabaseManagementViewModel();
        DataContext = _viewModel;
            
        Unloaded += DatabaseManagementPage_Unloaded;
    }
        
    private void DatabaseManagementPage_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        //_viewModel.Dispose();
    }
}