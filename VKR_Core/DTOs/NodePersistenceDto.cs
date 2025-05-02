using VKR_Core.Enums;
using VKR_Core.Models.Interfaces;

namespace VKR_Core.DTOs;

public class NodePersistenceDto : INode
{
    public required string Id { get; set; }
    public required string Address { get; set; }
    public NodeStateCore State { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime? LastSuccessfulPingTimestamp { get; set; }
    public long? DiskSpaceAvailableBytes { get; set; }
    public long? DiskSpaceTotalBytes { get; set; }
    public int? StoredChunkCount { get; set; }
}