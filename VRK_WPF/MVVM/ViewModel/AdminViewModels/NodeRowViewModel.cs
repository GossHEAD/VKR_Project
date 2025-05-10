using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class NodeRowViewModel : ObservableObject, IModifiableRow
{
    [ObservableProperty] private string _nodeId = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private int _state;
    [ObservableProperty] private DateTime _lastSeen;
    [ObservableProperty] private long? _diskSpaceAvailableBytes;
    [ObservableProperty] private long? _diskSpaceTotalBytes;
    [ObservableProperty] private int? _storedChunkCount;
    [ObservableProperty] private bool _isModified;
        
    partial void OnAddressChanged(string value) => IsModified = true;
    partial void OnStateChanged(int value) => IsModified = true;
}