using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration;

public class StorageOptions
{
    public string BasePath { get; set; } = "ChunkData";
    
    public long MaxSizeBytes { get; set; } 
    public int ChunkSize { get; set; } = 1048576; // 1BM
    public int DefaultReplicationFactor { get; set; }
}
