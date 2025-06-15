namespace VKR_Core.Models;

public record ChunkModel

{
    public required string ChunkId { get; init; }
    public required string FileId { get; init; }
    public int ChunkIndex { get; init; }
    public long Size { get; set; }
    public string? ChunkHash { get; init; }
    public required string StoredNodeId { get; set; }
}