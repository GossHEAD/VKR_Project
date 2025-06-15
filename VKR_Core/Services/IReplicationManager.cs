using VKR_Core.Models;

namespace VKR_Core.Services;

public interface IReplicationManager
{
    Task ReplicateChunkAsync(ChunkModel chunkInfo, Func<Task<Stream>> sourceDataStreamFactory, int replicationFactor, CancellationToken cancellationToken = default);
    Task EnsureChunkReplicationAsync(string fileId, string chunkId, CancellationToken cancellationToken = default);
}