using VKR_Core.Enums;
using VKR_Core.Models.Interfaces;

namespace VKR_Core.DTOs;

/// <summary>
/// DTO for transferring file data to/from persistence layer.
/// </summary>
public class FilePersistenceDto : IFile
{
    public required string FileId { get; set; }
    public required string FileName { get; set; }
    public long FileSize { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime ModificationTime { get; set; }
    public string? ContentType { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public FileStateCore State { get; set; } = FileStateCore.Unknown;
}