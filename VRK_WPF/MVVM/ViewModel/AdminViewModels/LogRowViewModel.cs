using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public partial class LogRowViewModel : BaseRowViewModel
    {
        [ObservableProperty] private long _id;
        [ObservableProperty] private DateTime _timestamp;
        [ObservableProperty] private string _level = string.Empty;
        [ObservableProperty] private string _message = string.Empty;
        [ObservableProperty] private string? _exceptionDetails;
    }
}