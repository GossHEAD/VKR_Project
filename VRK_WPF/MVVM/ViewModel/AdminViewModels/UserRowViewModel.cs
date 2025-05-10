using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class UserRowViewModel : ObservableObject, IModifiableRow
{
    [ObservableProperty] private int _userId;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private int _role;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private DateTime _creationTime;
    [ObservableProperty] private bool _isModified;
        
    partial void OnUsernameChanged(string value) => IsModified = true;
    partial void OnRoleChanged(int value) => IsModified = true;
    partial void OnIsActiveChanged(bool value) => IsModified = true;
}