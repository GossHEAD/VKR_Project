using VKR_Core.Enums;
using VKR_Core.Models.Interfaces;

namespace VKR_Core.Models;

/// <summary>
/// Core domain model for a network node.
/// </summary>
public record class NodeModel : INode
{
    public required string Id { get; init; }
    public required string Address { get; init; }
    public NodeStateCore State { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime? LastSuccessfulPingTimestamp { get; set; }
    public long? DiskSpaceAvailableBytes { get; init; }
    public long? DiskSpaceTotalBytes { get; init; }
    public int? StoredChunkCount { get; init; }
    public double? CpuUsagePercent { get; init; }
    public long? MemoryUsedBytes { get; init; }
    public long? MemoryTotalBytes { get; init; }
}