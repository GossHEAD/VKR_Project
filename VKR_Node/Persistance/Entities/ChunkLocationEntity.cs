using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VKR_Node.Persistance.Entities
{
    [Table("ChunkLocations")]
    public class ChunkLocationEntity
    {
        [Key, Column(Order = 0)]
        [Required]
        public required string FileId { get; set; }
        
        [Key, Column(Order = 1)]
        [Required]
        public required string ChunkId { get; set; }
        
        [Key, Column(Order = 2)]
        [Required]
        public required string StoredNodeId { get; set; }
        
        public DateTime? ReplicationTime { get; set; } 
        
        public virtual ChunkEntity ChunkEntity { get; set; } = null!; 
    }
}