// In VKR_WPF project
namespace VRK_WPF.MVVM.Model
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string NodeId { get; set; }
        public string FullText { get; set; }
        
        public bool IsError => Level?.Contains("ERR", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsWarning => Level?.Contains("WARN", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsInfo => Level?.Contains("INFO", StringComparison.OrdinalIgnoreCase) == true;
        public bool IsDebug => Level?.Contains("DEBUG", StringComparison.OrdinalIgnoreCase) == true;
        
        // For log coloring
        public System.Windows.Media.Brush GetLevelBrush()
        {
            return Level?.ToUpperInvariant() switch
            {
                var l when l.Contains("ERR") => System.Windows.Media.Brushes.Red,
                var l when l.Contains("WARN") => System.Windows.Media.Brushes.Orange,
                var l when l.Contains("INFO") => System.Windows.Media.Brushes.Green,
                var l when l.Contains("DEBUG") => System.Windows.Media.Brushes.Gray,
                _ => System.Windows.Media.Brushes.Black
            };
        }
    }
}