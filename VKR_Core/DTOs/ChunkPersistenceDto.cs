using VKR_Core.Models.Interfaces;

namespace VKR_Core.DTOs;

/// <summary>
/// DTO for transferring chunk data to/from persistence layer.
/// </summary>
public class ChunkPersistenceDto : IChunk
{
    public required string ChunkId { get; set; }
    public required string FileId { get; set; }
    public int ChunkIndex { get; set; }
    public long Size { get; set; }
    public string? ChunkHash { get; set; }
    public required string StoredNodeId { get; set; }
}