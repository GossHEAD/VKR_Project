using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel;

/// <summary>
    /// ViewModel representing a file's metadata for display in the UI (e.g., in a ListView).
    /// Inherits from ObservableObject for property change notifications.
    /// </summary>
    public partial class FileViewModel : ObservableObject
    {
        // Use [ObservableProperty] attribute to auto-generate boilerplate
        // (backing field, property getter/setter, OnPropertyChanged call)

        [ObservableProperty]
        private string? _fileId;

        [ObservableProperty]
        private string? _fileName;

        [ObservableProperty]
        private long _fileSize; // Use long for potentially large files

        [ObservableProperty]
        private DateTime _creationTime;

        [ObservableProperty]
        private string? _contentType;

        [ObservableProperty]
        private string? _state; // Display state as a string (e.g., "Available", "Deleting")

        // Constructor (optional, properties can be set directly)
        public FileViewModel() { }

        // You might have a constructor accepting FileMetadataCore or the proto message
        // public FileViewModel(VKR.Core.Models.FileMetadataCore coreData)
        // {
        //     FileId = coreData.FileId;
        //     FileName = coreData.FileName;
        //     FileSize = coreData.FileSize;
        //     CreationTime = coreData.CreationTime.ToLocalTime(); // Convert to local time for display
        //     ContentType = coreData.ContentType;
        //     State = coreData.State.ToString(); // Convert enum to string
        // }

        // public FileViewModel(VKR.Protos.FileMetadata protoData)
        // {
        //     FileId = protoData.FileId;
        //     FileName = protoData.FileName;
        //     FileSize = protoData.FileSize;
        //     CreationTime = protoData.CreationTime?.ToDateTime().ToLocalTime() ?? DateTime.MinValue;
        //     ContentType = protoData.ContentType;
        //     State = protoData.State.ToString(); // Convert proto enum to string
        // }


        // Override ToString for easier debugging
        public override string ToString()
        {
            return $"{FileName ?? "N/A"} ({FileSize} bytes) - {State ?? "N/A"}";
        }
    }