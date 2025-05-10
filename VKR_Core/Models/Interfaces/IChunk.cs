namespace VKR_Core.Models.Interfaces;

public interface IChunk
{
    string ChunkId { get; }
    string FileId { get; }
    int ChunkIndex { get; }
    long Size { get; }
    string? ChunkHash { get; }
}