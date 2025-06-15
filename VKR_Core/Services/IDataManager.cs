using VKR_Core.Models;

namespace VKR_Core.Services;


public interface IDataManager
{
    Task<string> StoreChunkAsync(ChunkModel chunkInfo, Stream dataStream, CancellationToken cancellationToken = default);
    
    Task<Stream?> RetrieveChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default);
    
    Task<bool> DeleteChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default);
    
    Task<bool> ChunkExistsAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default);
    
    Task<long> GetFreeDiskSpaceAsync(CancellationToken cancellationToken = default);
    
    Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default);
}