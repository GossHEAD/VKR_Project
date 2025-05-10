using System.Windows.Controls;
using VKR.Protos;
using VRK_WPF.MVVM.ViewModel.AdminViewModels;

namespace VRK_WPF.MVVM.View.AdminPages;

public partial class NodeConfigPage : Page
{
    private readonly NodeConfigViewModel _viewModel;
        
    public NodeConfigPage()
    {
        InitializeComponent();
        _viewModel = new NodeConfigViewModel();
        DataContext = _viewModel;
    }
        
    public void SetClient(StorageService.StorageServiceClient client)
    {
        //_viewModel.SetClient(client);
    }
}