using VKR_Core.Enums;
using VKR_Core.Models;
namespace VKR_Core.Services;
public interface IMetadataManager : IAsyncInitializable
{
    Task SaveFileMetadataAsync(FileModel metadata, CancellationToken cancellationToken = default);
    Task<FileModel?> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileModel>> ListFilesAsync(CancellationToken cancellationToken = default);
    Task DeleteFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task UpdateFileStateAsync(string fileId, FileStateCore newState, CancellationToken cancellationToken = default);
    Task SaveChunkMetadataAsync(ChunkModel chunkInfo, IEnumerable<string> initialNodeIds, CancellationToken cancellationToken = default);
    Task<ChunkModel?> GetChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChunkModel>> GetChunksMetadataForFileAsync(string fileId, CancellationToken cancellationToken = default);
    Task AddChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> RemoveChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default); 
    Task<IEnumerable<string>> GetChunkStorageNodesAsync(string fileId, string chunkId, CancellationToken cancellationToken = default); 
    Task UpdateChunkStorageNodesAsync(string fileId, string chunkId, IEnumerable<string> currentNodeIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChunkModel>> GetChunksStoredLocallyAsync(CancellationToken cancellationToken = default); 
    Task SaveNodeStateAsync(NodeModel  nodeState, CancellationToken cancellationToken = default); 
    Task<IEnumerable<NodeModel >> GetNodeStatesAsync(IEnumerable<string> nodeIds, CancellationToken cancellationToken = default); 
    Task<IEnumerable<NodeModel >> GetAllNodeStatesAsync(CancellationToken cancellationToken = default); 
}
