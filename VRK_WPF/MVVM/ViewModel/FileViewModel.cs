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
        public FileViewModel() { }

        public FileViewModel(VKR_Core.Models.FileModel coreData)
        {
            FileId = coreData.FileId;
            FileName = coreData.FileName;
            FileSize = coreData.FileSize;
            CreationTime = coreData.CreationTime.ToLocalTime(); 
            ContentType = coreData.ContentType;
            State = coreData.State.ToString(); 
        }

        public FileViewModel(VKR.Protos.FileMetadata protoData)
        {
            FileId = protoData.FileId;
            FileName = protoData.FileName;
            FileSize = protoData.FileSize;
            CreationTime = protoData.CreationTime?.ToDateTime().ToLocalTime() ?? DateTime.MinValue;
            ContentType = protoData.ContentType;
            State = protoData.State.ToString(); 
        }

        public override string ToString()
        {
            return $"{FileName ?? "N/A"} ({FileSize} bytes) - {State ?? "N/A"}";
        }
    }