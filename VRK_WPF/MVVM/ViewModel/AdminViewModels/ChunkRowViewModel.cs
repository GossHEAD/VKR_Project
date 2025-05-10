using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class ChunkRowViewModel : ObservableObject, IModifiableRow
{
    [ObservableProperty] private string _fileId = string.Empty;
    [ObservableProperty] private string _chunkId = string.Empty;
    [ObservableProperty] private int _chunkIndex;
    [ObservableProperty] private long _size;
    [ObservableProperty] private string? _chunkHash;
    [ObservableProperty] private bool _isModified;
        
    partial void OnChunkIndexChanged(int value) => IsModified = true;
    partial void OnSizeChanged(long value) => IsModified = true;
    partial void OnChunkHashChanged(string? value) => IsModified = true;
}