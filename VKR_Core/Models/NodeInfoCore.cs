using System.ComponentModel.DataAnnotations;

namespace VKR_Core.Models;

public record NodeInfoCore
{
    [Key]
    public required string Id { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }
    public string Address => $"{IpAddress}:{Port}";
        
    public int State { get; set; } 
    public DateTime LastSeen { get; set; }
    
    public DateTime? LastSuccessfulPingTimestamp { get; set; }
        
    public long? DiskSpaceAvailableBytes { get; set; }
        
    public long? DiskSpaceTotalBytes { get; set; }

    public int? StoredChunkCount { get; set; }
}