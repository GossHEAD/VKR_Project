using System.Collections.ObjectModel;
namespace VRK_WPF.MVVM.ViewModel;

public class MainWindowViewModel
{
    public ObservableCollection<NodeViewModel> NodeList { get; set; }

    public MainWindowViewModel()
    {
        NodeList = new ObservableCollection<NodeViewModel>
        {
            new NodeViewModel { Id = "Node1", Address = "192.168.1.1", Status = "Active" },
            new NodeViewModel { Id = "Node2", Address = "192.168.1.2", Status = "Inactive" },
            new NodeViewModel { Id = "Node3", Address = "192.168.1.3", Status = "Active" },
            new NodeViewModel { Id = "Node4", Address = "192.168.1.4", Status = "Active" },
            new NodeViewModel { Id = "Node5", Address = "192.168.1.5", Status = "Inactive" }
        };
    }
}
