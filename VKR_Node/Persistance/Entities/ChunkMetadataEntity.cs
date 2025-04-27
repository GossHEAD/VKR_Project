using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VKR_Node.Persistance.Entities
{
    /// <summary>
    /// Represents the metadata for a single chunk of a file in the database.
    /// Maps to the 'ChunksMetadata' table.
    /// </summary>
    [Table("ChunksMetadata")]
    public class ChunkMetadataEntity
    {
        /// <summary>
        /// Unique identifier for the chunk. Part of the composite primary key.
        /// </summary>
        [Key, Column(Order = 1)] // Define composite key order
        [Required]
        public required string ChunkId { get; set; }

        /// <summary>
        /// Identifier of the parent file. Part of the composite primary key and foreign key.
        /// </summary>
        [Key, Column(Order = 0)] // Define composite key order
        [Required]
        public required string FileId { get; set; }

        /// <summary>
        /// The sequential index (order) of this chunk within the file (0-based).
        /// </summary>
        [Required]
        public int ChunkIndex { get; set; }

        /// <summary>
        /// Size of this specific chunk in bytes.
        /// </summary>
        [Required]
        public int Size { get; set; }

        /// <summary>
        /// Hash of the chunk data (optional, for integrity checks). E.g., SHA256 hex string.
        /// </summary>
        public string? ChunkHash { get; set; }

        // --- Navigation Properties ---

        /// <summary>
        /// Navigation property back to the parent FileMetadataEntity.
        /// Configured via Fluent API in NodeDbContext.
        /// </summary>
        public virtual FileMetadataEntity File { get; set; } = null!; // Required relationship

        /// <summary>
        /// Collection navigation property representing the locations (nodes) where this chunk is stored.
        /// Configured for cascade delete in NodeDbContext.
        /// </summary>
        public virtual ICollection<ChunkLocationEntity> Locations { get; set; } = new List<ChunkLocationEntity>();
    }
}
