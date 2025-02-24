using System.Collections.ObjectModel;
using VKR_Common.Models;
using VKR_Node.Models;

namespace VRK_WPF.MVVM.ViewModel;

public class NodeListViewModel
{
    public ObservableCollection<Node> Nodes { get; } = new();

    public NodeListViewModel()
    {
        // Инициализация узлов (для тестирования)
        Nodes.Add(new Node("127.0.0.1", 5000));
        Nodes.Add(new Node( "127.0.0.2", 5001));
    }
}