using VKR_Core.Models.Interfaces;

namespace VKR_Core.Models;

public record class ChunkModel : IChunk
{
    public required string ChunkId { get; init; }
    public required string FileId { get; init; }
    public int ChunkIndex { get; init; }
    public long Size { get; init; }
    public string? ChunkHash { get; init; }
    public required string StoredNodeId { get; set; }
}