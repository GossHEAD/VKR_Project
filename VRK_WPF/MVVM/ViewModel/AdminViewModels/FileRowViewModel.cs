using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class FileRowViewModel : BaseRowViewModel
    {
        [ObservableProperty] private string _fileId = string.Empty;
        [ObservableProperty] private string _fileName = string.Empty;
        [ObservableProperty] private long _fileSize;
        [ObservableProperty] private DateTime _creationTime;
        [ObservableProperty] private DateTime _modificationTime;
        [ObservableProperty] private string? _contentType;
        [ObservableProperty] private long _chunkSize;
        [ObservableProperty] private int _totalChunks;
        [ObservableProperty] private int _state;
    
        partial void OnFileNameChanged(string value) => IsModified = true;
        partial void OnFileSizeChanged(long value) => IsModified = true;
        partial void OnContentTypeChanged(string? value) => IsModified = true;
        partial void OnChunkSizeChanged(long value) => IsModified = true;
        partial void OnTotalChunksChanged(int value) => IsModified = true;
        partial void OnStateChanged(int value) => IsModified = true;
    }
}