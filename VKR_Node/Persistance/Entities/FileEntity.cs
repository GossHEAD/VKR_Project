using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VKR_Node.Persistance.Entities;

/// <summary>
/// Entity representing a file in the database.
/// </summary>
[Table("FilesMetadata")]
public class FileEntity
{
    [Key]
    public required string FileId { get; set; }
        
    public required string FileName { get; set; }
        
    public long FileSize { get; set; }
        
    public DateTime CreationTime { get; set; }
        
    public DateTime ModificationTime { get; set; }
        
    public string? ContentType { get; set; }
        
    public long ChunkSize { get; set; }
        
    public int TotalChunks { get; set; }
        
    public int State { get; set; }
        
    public virtual ICollection<ChunkEntity> Chunks { get; set; } = new List<ChunkEntity>();
}