using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel;

public partial class NodeViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _nodeId;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private string? _statusDetails;
    
    [ObservableProperty]
    private bool _isSelected;
   
    public NodeViewModel(string nodeId, string address, string status = "Unknown", string statusDetails = "")
    {
        _nodeId = nodeId;
        _address = address;
        _status = status;
        _statusDetails = statusDetails;
    }
   
    public NodeViewModel() {
        _nodeId = "DesignTime Node";
        _address = "localhost:0000";
        _status = "DesignTime Status";
        _statusDetails = "DesignTime Details";
    }
    
    public override string ToString()
    {
        return $"Node: {NodeId ?? "N/A"} ({Address ?? "N/A"}) - Status: {Status ?? "N/A"}";
    }
    
    partial void OnIsSelectedChanged(bool value)
    {
        
    }
}
