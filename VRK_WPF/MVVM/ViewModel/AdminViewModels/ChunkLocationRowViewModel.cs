using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class ChunkLocationRowViewModel : ObservableObject, IModifiableRow
{
    [ObservableProperty] private string _fileId = string.Empty;
    [ObservableProperty] private string _chunkId = string.Empty;
    [ObservableProperty] private string _storedNodeId = string.Empty;
    [ObservableProperty] private DateTime _replicationTime;
    [ObservableProperty] private bool _isModified;
        
    partial void OnStoredNodeIdChanged(string value) => IsModified = true;
    partial void OnReplicationTimeChanged(DateTime value) => IsModified = true;
}