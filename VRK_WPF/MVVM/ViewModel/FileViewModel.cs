using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel;


    public partial class FileViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? _fileId;

        [ObservableProperty]
        private string? _fileName;

        [ObservableProperty]
        private long _fileSize; 

        [ObservableProperty]
        private DateTime _creationTime;

        [ObservableProperty]
        private string? _contentType;

        [ObservableProperty]
        private string? _state; 
    }