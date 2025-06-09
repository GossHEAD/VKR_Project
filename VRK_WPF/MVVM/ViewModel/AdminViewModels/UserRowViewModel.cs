using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public partial class UserRowViewModel : BaseRowViewModel
{
    [ObservableProperty] private int _userId;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private int _role;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private DateTime _creationTime;
    [ObservableProperty] private bool _isModified;
    
    private string? _newPassword;
    private bool _passwordChanged;
    
    public string? NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                _passwordChanged = !string.IsNullOrEmpty(value);
                IsModified = true;
            }
        }
    }
    
    public bool PasswordChanged => _passwordChanged;
    
    public string RoleDisplay => Role switch
    {
        0 => "IT Специалист",
        1 => "Администратор",
        _ => "Неизвестно"
    };
    
    partial void OnUsernameChanged(string value) => IsModified = true;
    partial void OnPasswordChanged(string value) => IsModified = true;
    partial void OnRoleChanged(int value) => IsModified = true;
    partial void OnIsActiveChanged(bool value) => IsModified = true;
}