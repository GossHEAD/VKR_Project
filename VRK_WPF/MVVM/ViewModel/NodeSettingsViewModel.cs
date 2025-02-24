using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using VKR_Common.Models;
using VKR_Node.Models;

namespace VRK_WPF.MVVM.ViewModel;

public class NodeSettingsViewModel
{
    public Node Node { get; }

    public ICommand StartNodeCommand { get; }

    public NodeSettingsViewModel()
    {
        Node = new Node("127.0.0.1", 5000);
        StartNodeCommand = new RelayCommand(StartNode);
    }

    private void StartNode()
    {
        // Логика запуска узла
    }
}