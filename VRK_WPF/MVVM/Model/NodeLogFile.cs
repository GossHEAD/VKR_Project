namespace VRK_WPF.MVVM.Model;

public class NodeLogFile
{
    public string FilePath { get; set; }
    public string NodeId { get; set; }
    public DateTime LastWriteTime { get; set; }
    
    public override string ToString()
    {
        return $"{NodeId} - {LastWriteTime:yyyy-MM-dd HH:mm:ss}";
    }
}