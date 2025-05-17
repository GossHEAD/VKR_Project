using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel;

public partial class SimulationFileStatusViewModel  : ObservableObject
{
    [ObservableProperty]
    private string? _fileName;
    
    [ObservableProperty]
    private string? _availability;
    
    [ObservableProperty]
    private string? _replicationStatus;
    
    [ObservableProperty]
    private Brush? _statusColor;
}