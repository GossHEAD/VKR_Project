using VKR_Core.Enums;
using VKR_Core.Models;

namespace VKR_Core.Services;

/// <summary>
/// Управляет хранением и извлечением метаданных о файлах, чанках и состоянии узла
/// в персистентном хранилище (например, SQLite).
/// </summary>
public interface IMetadataManager : IAsyncInitializable
{
    new Task InitializeAsync(CancellationToken cancellationToken = default); 
    Task SaveFileMetadataAsync(FileModel metadata, CancellationToken cancellationToken = default);
    Task<FileModel?> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileModel>> ListFilesAsync(CancellationToken cancellationToken = default);
    Task DeleteFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task UpdateFileStateAsync(string fileId, FileStateCore newState, CancellationToken cancellationToken = default);

    Task SaveChunkMetadataAsync(ChunkModel chunkInfo, IEnumerable<string> initialNodeIds, CancellationToken cancellationToken = default);
    Task<ChunkModel?> GetChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ChunkModel>> GetChunksMetadataForFileAsync(string fileId, CancellationToken cancellationToken = default);
    Task DeleteChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default);

    Task AddChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> RemoveChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default); // Changed return type? Assume Task
    Task<IEnumerable<string>> GetChunkStorageNodesAsync(string fileId, string chunkId, CancellationToken cancellationToken = default); // Changed signature
    Task UpdateChunkStorageNodesAsync(string fileId, string chunkId, IEnumerable<string> currentNodeIds, CancellationToken cancellationToken = default); // Changed signature
    Task<IEnumerable<ChunkModel>> GetChunksStoredLocallyAsync(CancellationToken cancellationToken = default); // Parameter is just CancellationToken? Needs NodeId.

    // Backup
    Task<bool> BackupDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates the state of a specific node (including load metrics).
    /// Used by nodes to report their own status.
    /// </summary>
    Task SaveNodeStateAsync(NodeModel  nodeState, CancellationToken cancellationToken = default); // Assuming NodeStateEntity is defined in Core or accessible

    /// <summary>
    /// Retrieves the state information for multiple specified nodes.
    /// </summary>
    /// <param name="nodeIds">The IDs of the nodes to retrieve status for.</param>
    /// <returns>A collection of NodeStateEntity objects for the found nodes.</returns>
    Task<IEnumerable<NodeModel >> GetNodeStatesAsync(IEnumerable<string> nodeIds, CancellationToken cancellationToken = default); // Assuming NodeStateEntity is defined in Core or accessible

    /// <summary>
    /// Retrieves the state information for all known nodes (excluding potentially self based on implementation).
    /// </summary>
    Task<IEnumerable<NodeModel >> GetAllNodeStatesAsync(CancellationToken cancellationToken = default); // Useful alternative
}
