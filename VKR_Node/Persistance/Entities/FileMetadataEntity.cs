using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VKR_Core.Enums;

namespace VKR_Node.Persistance.Entities;

/// <summary>
    /// Represents the metadata for a file stored in the database.
    /// Maps directly to the 'FilesMetadata' table.
    /// </summary>
    [Table("FilesMetadata")] // Explicitly map to table name
    public class FileMetadataEntity
    {
        /// <summary>
        /// Unique identifier for the file. Primary Key.
        /// </summary>
        [Key] // Define as primary key
        [Required] // Cannot be null
        public required string FileId { get; set; }

        /// <summary>
        /// Original name of the file.
        /// </summary>
        [Required]
        public required string FileName { get; set; }

        /// <summary>
        /// Total size of the file in bytes.
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// Timestamp when the file was added (UTC).
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// Timestamp of the last modification (UTC).
        /// </summary>
        public DateTime ModificationTime { get; set; }

        /// <summary>
        /// MIME type of the file (optional).
        /// </summary>
        public string? ContentType { get; set; }

        /// <summary>
        /// Size of each chunk in bytes used for this file.
        /// </summary>
        public int ChunkSize { get; set; }

        /// <summary>
        /// Total number of chunks comprising this file.
        /// </summary>
        public int TotalChunks { get; set; }

        /// <summary>
        /// Current state of the file, stored as an integer.
        /// Maps to the VKR.Core.Enums.FileStateCore enum.
        /// </summary>
        [Required]
        public int State { get; set; } // Stored as int, mapped from/to FileStateCore enum

        // --- Navigation Properties ---

        /// <summary>
        /// Collection navigation property representing the chunks associated with this file.
        /// Configured for cascade delete in NodeDbContext.
        /// </summary>
        public virtual ICollection<ChunkMetadataEntity> Chunks { get; set; } = new List<ChunkMetadataEntity>();
    }