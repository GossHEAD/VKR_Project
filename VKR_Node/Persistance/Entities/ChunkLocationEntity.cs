using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VKR_Node.Persistance.Entities
{
    /// <summary>
    /// Represents the location (a specific node) where a replica of a chunk is stored.
    /// Maps to the 'ChunkLocations' table.
    /// </summary>
    [Table("ChunkLocations")]
    public class ChunkLocationEntity
    {
        /// <summary>
        /// Identifier of the parent file. Part of the composite primary key and foreign key.
        /// </summary>
        [Key, Column(Order = 0)] // Define composite key order
        [Required]
        public required string FileId { get; set; }

        /// <summary>
        /// Identifier of the chunk. Part of the composite primary key and foreign key.
        /// </summary>
        [Key, Column(Order = 1)] // Define composite key order
        [Required]
        public required string ChunkId { get; set; }

        /// <summary>
        /// Identifier of the node where this chunk replica is stored. Part of the composite primary key.
        /// </summary>
        [Key, Column(Order = 2)] // Define composite key order
        [Required]
        public required string StoredNodeId { get; set; }

        /// <summary>
        /// Timestamp indicating when this replica was confirmed to be stored (optional).
        /// </summary>
        public DateTime? ReplicationTime { get; set; } // Added optional timestamp

        // --- Navigation Properties ---

        /// <summary>
        /// Navigation property back to the associated ChunkMetadataEntity.
        /// Configured via Fluent API in NodeDbContext.
        /// </summary>
        public virtual ChunkMetadataEntity ChunkMetadataEntity { get; set; } = null!; // Required relationship
    }
}