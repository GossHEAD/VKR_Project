using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VRK_WPF.MVVM.ViewModel.AdminViewModels
{
    public abstract class BaseRowViewModel : ObservableObject, IModifiableRow, IEditableObject
    {
        private bool _isModified;
        private Dictionary<string, object?> _originalValues = new();
        private bool _isEditing;

        public bool IsModified
        {
            get => _isModified;
            set => SetProperty(ref _isModified, value);
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            if (!_isEditing && e.PropertyName != nameof(IsModified))
            {
                IsModified = true;
            }
        }

        public void BeginEdit()
        {
            if (_isEditing) return;
            
            _isEditing = true;
            _originalValues.Clear();
            
            foreach (var prop in GetType().GetProperties())
            {
                if (prop.CanRead && prop.CanWrite)
                {
                    _originalValues[prop.Name] = prop.GetValue(this);
                }
            }
        }

        public void CancelEdit()
        {
            if (!_isEditing) return;
            
            foreach (var kvp in _originalValues)
            {
                var prop = GetType().GetProperty(kvp.Key);
                prop?.SetValue(this, kvp.Value);
            }
            
            _isEditing = false;
            IsModified = false;
        }

        public void EndEdit()
        {
            _isEditing = false;
            _originalValues.Clear();
        }
    }
}