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
    }
}