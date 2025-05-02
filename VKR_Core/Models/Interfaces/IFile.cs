using VKR_Core.Enums;

namespace VKR_Core.Models.Interfaces;

/// <summary>
/// Defines the core properties of a file.
/// </summary>
public interface IFile
{
    string FileId { get; }
    string FileName { get; }
    long FileSize { get; }
    DateTime CreationTime { get; }
    DateTime ModificationTime { get; }
    string? ContentType { get; }
    int ChunkSize { get; }
    int TotalChunks { get; }
    FileStateCore State { get; }
}