using System.Windows.Controls;
using VKR.Protos;
using VRK_WPF.MVVM.ViewModel.AdminViewModels;

namespace VRK_WPF.MVVM.View.AdminPages;

public partial class LogViewerPage : System.Windows.Controls.Page
{
    private readonly LogViewerViewModel _viewModel;
        
    public LogViewerPage()
    {
        InitializeComponent();
        _viewModel = new LogViewerViewModel();
        DataContext = _viewModel;
    }
        
    public void SetClient(StorageService.StorageServiceClient client)
    {
        //_viewModel.SetClient(client);
    }
}