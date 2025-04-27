using System.ComponentModel.DataAnnotations;
using System.Globalization;
using VKR_Core.Enums;
namespace VKR_Core.Models;

/// <summary>
/// Represents the core metadata for a file within the system (used internally and for DB mapping).
/// </summary>
public class FileMetadataCore
{
    /// <summary>
    /// Unique identifier for the file (e.g., hash of content or UUID).
    /// Primary Key in the database.
    /// </summary>
    public required string FileId { get; set; }

    /// <summary>
    /// Original name of the file.
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// Total size of the file in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Timestamp when the file was added to the system (UTC).
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
    /// Current state of the file within the system.
    /// Uses the core enum definition.
    /// </summary>
    public FileStateCore State { get; set; } = FileStateCore.Unknown; // Default state

    // Navigation properties (if using EF Core relationships directly in Core, otherwise handled in Persistence layer)
    // public virtual ICollection<ChunkMetadataCore> Chunks { get; set; } = new List<ChunkMetadataCore>();
}