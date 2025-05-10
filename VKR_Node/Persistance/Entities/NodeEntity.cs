using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VKR_Node.Persistance.Entities;

[Table("NodeStates")]
public class NodeEntity
{
    [Key]
    public required string NodeId { get; set; }
    public required string Address { get; set; }
    public int State { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime? LastSuccessfulPingTimestamp { get; set; }
    public long? DiskSpaceAvailableBytes { get; set; }
    public long? DiskSpaceTotalBytes { get; set; }
    public int? StoredChunkCount { get; set; }
}