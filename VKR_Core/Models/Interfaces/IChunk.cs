namespace VKR_Core.Models.Interfaces;

/// <summary>
/// Defines the core properties of a file chunk.
/// </summary>
public interface IChunk
{
    string ChunkId { get; }
    string FileId { get; }
    int ChunkIndex { get; }
    long Size { get; }
    string? ChunkHash { get; }
}