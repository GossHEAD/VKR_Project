namespace VKR_Core.Models;

public record ChunkLocationModel
{
    public string FileId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public DateTime ReplicationTime { get; set; } = DateTime.UtcNow;
}