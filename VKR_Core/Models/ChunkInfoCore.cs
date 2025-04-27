using System.ComponentModel.DataAnnotations; // Keep if needed for other validation frameworks

namespace VKR_Core.Models
{
    /// <summary>
    /// Represents the core metadata for a single chunk of a file (used internally and for DB mapping).
    /// Changed to a record class to support 'with' expressions.
    /// </summary>
    public record class ChunkInfoCore // MODIFIED: Changed from class to record class
    {
        /// <summary>
        /// Unique identifier for the chunk (e.g., hash of chunk content).
        /// Part of the composite primary key in the database.
        /// </summary>
        public required string ChunkId { get; set; }

        /// <summary>
        /// Identifier of the parent file this chunk belongs to.
        /// Part of the composite primary key and foreign key in the database.
        /// </summary>
        public required string FileId { get; set; }

        /// <summary>
        /// The sequential index (order) of this chunk within the file (0-based).
        /// </summary>
        public int ChunkIndex { get; set; }

        /// <summary>
        /// Size of this specific chunk in bytes (can be smaller for the last chunk).
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// Hash of the chunk data (optional, for integrity checks).
        /// </summary>
        public string? ChunkHash { get; set; } // Example: SHA256 hash as hex string

        /// <summary>
        /// The ID of the node where this specific instance/replica of the chunk is stored.
        /// This property is crucial for the `SaveChunkMetadataAsync` logic which creates `ChunkLocationEntity`.
        /// It indicates WHICH node's metadata database this entry pertains to.
        /// **NOTE:** When getting general chunk metadata (not location specific), this might be empty.
        /// When used for local operations (like DeleteChunkAsync), it should be set to the local node ID.
        /// </summary>
        public required string StoredNodeId { get; set; }

        // Timestamps related to this specific chunk instance (optional)
        // public DateTime CreationTime { get; set; } // When this replica was created

        // Navigation properties are typically not included in Core DTOs/records if handled by Persistence layer
        // public virtual FileMetadataCore File { get; set; } = null!;
    }
}