namespace VRK_WPF.MVVM.ViewModel.AdminViewModels;

public class TableViewModel
{
    public string TableName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool CanEdit { get; set; }
}

public interface IModifiableRow
{
    bool IsModified { get; set; }
}