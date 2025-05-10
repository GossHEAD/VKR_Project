using System.ComponentModel;
using System.Windows.Media;

namespace VRK_WPF.MVVM.Model
{
    public class LogEntry : INotifyPropertyChanged
    {
        private DateTime _timestamp;
        private string _level = string.Empty;
        private string _message = string.Empty;
        private string _nodeId = string.Empty;
        
        public DateTime Timestamp 
        { 
            get => _timestamp; 
            set
            {
                _timestamp = value;
                OnPropertyChanged(nameof(Timestamp));
            }
        }
        
        public string Level 
        { 
            get => _level; 
            set
            {
                _level = value;
                OnPropertyChanged(nameof(Level));
                OnPropertyChanged(nameof(IsInfo));
                OnPropertyChanged(nameof(IsWarning));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(IsDebug));
                OnPropertyChanged(nameof(GetLevelBrush));
            }
        }
        
        public string Message 
        { 
            get => _message; 
            set
            {
                _message = value;
                OnPropertyChanged(nameof(Message));
            }
        }
        
        public string NodeId 
        { 
            get => _nodeId; 
            set
            {
                _nodeId = value;
                OnPropertyChanged(nameof(NodeId));
            }
        }
        
        public string FullText => $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {NodeId} {Message}";
        
        public bool IsInfo => Level.ToUpperInvariant().Contains("INFO");
        public bool IsWarning => Level.ToUpperInvariant().Contains("WARN");
        public bool IsError => Level.ToUpperInvariant().Contains("ERR") || Level.ToUpperInvariant().Contains("FAIL");
        public bool IsDebug => Level.ToUpperInvariant().Contains("DEBUG") || Level.ToUpperInvariant().Contains("TRACE");
        
        public Brush GetLevelBrush
        {
            get
            {
                return Level.ToUpperInvariant() switch
                {
                    var l when l.Contains("INFO") => Brushes.Green,
                    var l when l.Contains("WARN") => Brushes.Orange,
                    var l when l.Contains("ERR") || l.Contains("FAIL") => Brushes.Red,
                    var l when l.Contains("DEBUG") => Brushes.Blue,
                    var l when l.Contains("TRACE") => Brushes.Gray,
                    _ => Brushes.Black
                };
            }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}