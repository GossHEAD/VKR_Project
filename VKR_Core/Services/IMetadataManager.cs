using VKR_Core.Enums;
using VKR_Core.Models;

namespace VKR_Core.Services;

/// <summary>
/// Управляет хранением и извлечением метаданных о файлах, чанках и состоянии узла
/// в персистентном хранилище (например, SQLite).
/// </summary>
public interface IMetadataManager : IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken = default); // From IAsyncInitializable
    Task SaveFileMetadataAsync(FileMetadataCore metadata, CancellationToken cancellationToken = default);
    Task<FileMetadataCore?> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task<IEnumerable<FileMetadataCore>> ListFilesAsync(CancellationToken cancellationToken = default);
    Task DeleteFileMetadataAsync(string fileId, CancellationToken cancellationToken = default); // Changed return type? Assume Task
    Task UpdateFileStateAsync(string fileId, FileStateCore newState, CancellationToken cancellationToken = default);

    Task SaveChunkMetadataAsync(ChunkInfoCore chunkInfo, IEnumerable<string> initialNodeIds, CancellationToken cancellationToken = default); // Changed signature
    Task<ChunkInfoCore?> GetChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default); // Changed signature
    Task<IEnumerable<ChunkInfoCore>> GetChunksMetadataForFileAsync(string fileId, CancellationToken cancellationToken = default);
    Task DeleteChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default); // Changed return type? Assume Task

    Task AddChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default);
    Task<bool> RemoveChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default); // Changed return type? Assume Task
    Task<IEnumerable<string>> GetChunkStorageNodesAsync(string fileId, string chunkId, CancellationToken cancellationToken = default); // Changed signature
    Task UpdateChunkStorageNodesAsync(string fileId, string chunkId, IEnumerable<string> currentNodeIds, CancellationToken cancellationToken = default); // Changed signature
    Task<IEnumerable<ChunkInfoCore>> GetChunksStoredLocallyAsync(CancellationToken cancellationToken = default); // Parameter is just CancellationToken? Needs NodeId.

    // Backup
    Task<bool> BackupDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates the state of a specific node (including load metrics).
    /// Used by nodes to report their own status.
    /// </summary>
    Task SaveNodeStateAsync(NodeStateCoreInfo  nodeState, CancellationToken cancellationToken = default); // Assuming NodeStateEntity is defined in Core or accessible

    /// <summary>
    /// Retrieves the state information for multiple specified nodes.
    /// </summary>
    /// <param name="nodeIds">The IDs of the nodes to retrieve status for.</param>
    /// <returns>A collection of NodeStateEntity objects for the found nodes.</returns>
    Task<IEnumerable<NodeStateCoreInfo >> GetNodeStatesAsync(IEnumerable<string> nodeIds, CancellationToken cancellationToken = default); // Assuming NodeStateEntity is defined in Core or accessible

    /// <summary>
    /// Retrieves the state information for all known nodes (excluding potentially self based on implementation).
    /// </summary>
    Task<IEnumerable<NodeStateCoreInfo >> GetAllNodeStatesAsync(CancellationToken cancellationToken = default); // Useful alternative
}
