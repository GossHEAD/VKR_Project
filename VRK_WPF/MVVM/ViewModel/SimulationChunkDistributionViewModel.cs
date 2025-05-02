using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel;

public partial class SimulationChunkDistributionViewModel : ObservableObject
{
    [ObservableProperty]
    private string _chunkId;
    
    [ObservableProperty]
    private string _fileName;
    
    [ObservableProperty]
    private string _nodeLocations;
    
    [ObservableProperty]
    private int _replicaCount;
}