using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VKR_Node.Persistance.Entities;

/// <summary>
/// Entity representing a chunk in the database.
/// </summary>
[Table("ChunksMetadata")]
public class ChunkEntity
{
    [Key, Column(Order = 1)]
    public required string ChunkId { get; set; }
        
    [Key, Column(Order = 0)]
    public required string FileId { get; set; }
        
    public int ChunkIndex { get; set; }
        
    public long Size { get; set; }
        
    public string? ChunkHash { get; set; }
        
    public virtual FileEntity File { get; set; } = null!;
        
    public virtual ICollection<ChunkLocationEntity> Locations { get; set; } = new List<ChunkLocationEntity>();
}