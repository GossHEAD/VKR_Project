using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Services.Utilities;
using VKR.Protos;
using Google.Protobuf;
using System.Diagnostics;

namespace VKR_Node.Services
{
    /// <summary>
    /// Manages the replication of data chunks across network nodes to ensure data redundancy and availability
    /// </summary>
    public class BackgroundReplicationManager : IReplicationManager
    {
        private readonly ILogger<BackgroundReplicationManager> _logger;
        private readonly IMetadataManager _metadataManager;
        private readonly IDataManager _dataManager;
        private readonly INodeClient _nodeClient;
        private readonly string _localNodeId;
        private readonly DhtOptions _dhtOptions;
        private readonly StorageOptions _storageOptions;
        private readonly NetworkOptions _networkOptions;
        
        private readonly ConcurrentDictionary<string, (bool IsOnline, DateTime LastChecked)> _nodeStatusCache = new();
        private readonly TimeSpan _nodeStatusCacheTtl = TimeSpan.FromSeconds(30);
        
        private readonly SemaphoreSlim _replicationSemaphore;
        
        private readonly ConcurrentDictionary<string, Task> _activeReplicationTasks = new();

        public BackgroundReplicationManager(
            ILogger<BackgroundReplicationManager> logger,
            IMetadataManager metadataManager,
            IDataManager dataManager,
            INodeClient nodeClient,
            IOptions<NodeIdentityOptions> nodeIdentityOptions,
            IOptions<NetworkOptions> networkOptions,
            IOptions<StorageOptions> storageOptions,
            IOptions<DhtOptions> dhtOptions)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _metadataManager = metadataManager ?? throw new ArgumentNullException(nameof(metadataManager));
            _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
            _nodeClient = nodeClient ?? throw new ArgumentNullException(nameof(nodeClient));
            
            var nodeIdOptions = nodeIdentityOptions.Value ?? throw new ArgumentNullException(nameof(nodeIdentityOptions));
            _localNodeId = nodeIdOptions.NodeId ?? throw new InvalidOperationException("NodeId is not configured");
            
            _networkOptions = networkOptions.Value ?? throw new ArgumentNullException(nameof(networkOptions));
            _storageOptions = storageOptions.Value ?? throw new ArgumentNullException(nameof(storageOptions));
            _dhtOptions = dhtOptions.Value ?? throw new ArgumentNullException(nameof(dhtOptions));
            
            int maxParallelism = _dhtOptions.ReplicationMaxParallelism > 0 ? 
                _dhtOptions.ReplicationMaxParallelism : 10;
            _replicationSemaphore = new SemaphoreSlim(maxParallelism);
            
            _logger.LogInformation("BackgroundReplicationManager initialized for Node {NodeId} with max parallelism {MaxParallelism}", 
                _localNodeId, maxParallelism);
        }

        /// <summary>
        /// Ensures replication level for a specific chunk, repairing if necessary
        /// </summary>
        public async Task EnsureChunkReplicationAsync(string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(fileId)) throw new ArgumentException("File ID cannot be empty", nameof(fileId));
            if (string.IsNullOrEmpty(chunkId)) throw new ArgumentException("Chunk ID cannot be empty", nameof(chunkId));
            
            string operationKey = $"{fileId}:{chunkId}";
            
            if (_activeReplicationTasks.TryGetValue(operationKey, out var existingTask))
            {
                _logger.LogDebug("Replication already in progress for Chunk {ChunkId}. Waiting for completion.", chunkId);
                try
                {
                    await existingTask;
                    return;
                }
                catch
                {
                    // Previous task failed, we'll proceed with a new attempt
                    _activeReplicationTasks.TryRemove(operationKey, out _);
                }
            }
            
            // Create a new replication task
            var replicationTask = EnsureChunkReplicationInternalAsync(fileId, chunkId, cancellationToken);
            
            // Register the task in the active operations
            _activeReplicationTasks[operationKey] = replicationTask;
            
            try
            {
                await replicationTask;
            }
            finally
            {
                // Remove the task when completed
                _activeReplicationTasks.TryRemove(operationKey, out _);
            }
        }
        
        /// <summary>
        /// Internal implementation of chunk replication logic
        /// </summary>
        private async Task EnsureChunkReplicationInternalAsync(string fileId, string chunkId, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Ensuring replication level for Chunk {ChunkId} (File {FileId})", chunkId, fileId);
            int desiredReplicas = GetDesiredReplicaCount();
            
            try
            {
                // Acquire semaphore to limit concurrent operations
                await _replicationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    // Step 1: Find out where the chunk is currently stored
                    var storedNodeIds = await GetChunkStorageNodesAsync(fileId, chunkId, cancellationToken);
                    if (!storedNodeIds.Any())
                    {
                        _logger.LogWarning("Cannot ensure replication for Chunk {ChunkId}: No storage locations found", chunkId);
                        return;
                    }

                    // Step 2: Find which nodes are currently online
                    var onlineNodeIds = await FindOnlineNodesAsync(storedNodeIds, cancellationToken);
                    
                    // Step 3: Check if we need more replicas
                    int currentOnlineReplicas = onlineNodeIds.Count;
                    _logger.LogDebug("Chunk {ChunkId}: Desired={Desired}, Found={FoundTotal}, Online={OnlineCount}",
                                    chunkId, desiredReplicas, storedNodeIds.Count, currentOnlineReplicas);

                    if (currentOnlineReplicas >= desiredReplicas)
                    {
                        _logger.LogTrace("Chunk {ChunkId} has sufficient online replicas ({OnlineCount}/{Desired})", 
                            chunkId, currentOnlineReplicas, desiredReplicas);
                        return;
                    }

                    // Step 4: Create more replicas if needed
                    await CreateAdditionalReplicasAsync(
                        fileId, 
                        chunkId, 
                        onlineNodeIds, 
                        desiredReplicas - currentOnlineReplicas, 
                        cancellationToken);
                }
                finally
                {
                    // Always release the semaphore
                    _replicationSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("EnsureChunkReplicationAsync cancelled for Chunk {ChunkId}", chunkId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring replication level for Chunk {ChunkId}", chunkId);
                throw;
            }
        }

        /// <summary>
        /// Replicates a chunk to specified number of nodes
        /// </summary>
        public async Task ReplicateChunkAsync(
            ChunkModel chunkInfo, 
            Func<Task<Stream>> sourceDataStreamFactory, 
            int replicationFactor, 
            CancellationToken cancellationToken = default)
        {
            if (chunkInfo == null) throw new ArgumentNullException(nameof(chunkInfo));
            if (sourceDataStreamFactory == null) throw new ArgumentNullException(nameof(sourceDataStreamFactory));
            if (string.IsNullOrEmpty(chunkInfo.FileId)) throw new ArgumentException("FileId cannot be empty", nameof(chunkInfo));
            if (string.IsNullOrEmpty(chunkInfo.ChunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkInfo));
            
            _logger.LogInformation("Initiating replication for Chunk {ChunkId} (File {FileId})", 
                chunkInfo.ChunkId, chunkInfo.FileId);
            
            int effectiveReplicationFactor = replicationFactor > 0 ? 
                replicationFactor : GetDesiredReplicaCount();
            
            try
            {
                // Acquire semaphore to limit concurrent operations
                await _replicationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    // Step 1: Check existing replicas
                    var existingNodeIds = await GetChunkStorageNodesAsync(
                        chunkInfo.FileId, chunkInfo.ChunkId, cancellationToken);
                    
                    if (existingNodeIds.Count >= effectiveReplicationFactor)
                    {
                        _logger.LogInformation("Chunk {ChunkId} already has sufficient replicas ({ExistingCount}/{Required})",
                            chunkInfo.ChunkId, existingNodeIds.Count, effectiveReplicationFactor);
                        return;
                    }
                    
                    // Step 2: Find online nodes that don't already have the chunk
                    var allOnlineNodes = await FindAllOnlineNodesAsync(cancellationToken);
                    var availableNodes = allOnlineNodes
                        .Where(n => !existingNodeIds.Contains(n.NodeId))
                        .ToList();
                    
                    if (!availableNodes.Any())
                    {
                        _logger.LogWarning("No additional nodes available for replication of Chunk {ChunkId}", chunkInfo.ChunkId);
                        return;
                    }
                    
                    // Step 3: Determine how many new replicas we need
                    int replicasToAdd = Math.Min(
                        effectiveReplicationFactor - existingNodeIds.Count,
                        availableNodes.Count);
                    
                    if (replicasToAdd <= 0) return;
                    
                    // Step 4: Get the chunk data
                    byte[] chunkData;
                    using (var dataStream = await sourceDataStreamFactory())
                    {
                        if (dataStream == null)
                        {
                            _logger.LogError("Source data stream is null for Chunk {ChunkId}", chunkInfo.ChunkId);
                            return;
                        }
                        
                        using (var ms = new MemoryStream())
                        {
                            await dataStream.CopyToAsync(ms, cancellationToken);
                            chunkData = ms.ToArray();
                        }
                    }
                    
                    // Step 5: Get parent file metadata to send with replicas
                    var fileMetadata = await _metadataManager.GetFileMetadataAsync(
                        chunkInfo.FileId, cancellationToken);
                    
                    // Step 6: Select target nodes and replicate
                    var targetNodes = ReplicationUtility.SelectReplicaTargets(
                        availableNodes, chunkInfo.ChunkId, replicasToAdd);
                    
                    _logger.LogInformation("Replicating Chunk {ChunkId} to {Count} additional node(s): {Nodes}",
                        chunkInfo.ChunkId, targetNodes.Count, string.Join(", ", targetNodes.Select(n => n.NodeId)));
                    
                    // Create a list to track successful replications
                    var successfulReplicationNodes = new ConcurrentBag<string>();
                    
                    // Use a list of tasks for parallel replication
                    var replicationTasks = targetNodes.Select(async node => 
                    {
                        bool success = await ReplicateToNodeAsync(
                            node, 
                            chunkInfo, 
                            chunkData, 
                            fileMetadata, 
                            cancellationToken);
                            
                        if (success)
                        {
                            successfulReplicationNodes.Add(node.NodeId);
                        }
                    }).ToList();
                    
                    // Wait for all replication attempts to complete
                    await Task.WhenAll(replicationTasks);
                    
                    // Update metadata with successful replications
                    if (successfulReplicationNodes.Any())
                    {
                        var allStorageNodes = existingNodeIds.Concat(successfulReplicationNodes).Distinct().ToList();
                        await _metadataManager.UpdateChunkStorageNodesAsync(
                            chunkInfo.FileId, 
                            chunkInfo.ChunkId, 
                            allStorageNodes, 
                            cancellationToken);
                            
                        _logger.LogInformation("Successfully replicated Chunk {ChunkId} to {SuccessCount} additional nodes",
                            chunkInfo.ChunkId, successfulReplicationNodes.Count);
                    }
                    else
                    {
                        _logger.LogWarning("No successful replications for Chunk {ChunkId}", chunkInfo.ChunkId);
                    }
                }
                finally
                {
                    // Always release semaphore
                    _replicationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replicating Chunk {ChunkId}", chunkInfo.ChunkId);
                throw;
            }
        }

        /// <summary>
        /// Ensures all chunks of a file maintain the desired replication level
        /// </summary>
        public async Task EnsureReplicationLevelAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            
            _logger.LogInformation("Checking replication level for all chunks of File {FileId}", fileId);
            
            try
            {
                // Get all chunks for this file
                var chunks = await _metadataManager.GetChunksMetadataForFileAsync(fileId, cancellationToken);
                if (chunks == null || !chunks.Any())
                {
                    _logger.LogWarning("No chunks found for File {FileId}", fileId);
                    return;
                }
                
                _logger.LogDebug("Found {Count} chunks for File {FileId}", chunks.Count(), fileId);
                
                // Create a list to track all tasks
                var replicationTasks = new List<Task>();
                var sw = Stopwatch.StartNew();
                
                // Process chunks in batches to avoid overwhelming the system
                int maxConcurrentChunks = _dhtOptions.ReplicationMaxParallelism;
                var semaphore = new SemaphoreSlim(maxConcurrentChunks);
                
                foreach (var chunk in chunks)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    // Limit concurrent operations
                    await semaphore.WaitAsync(cancellationToken);
                    
                    // Add task to the list and continue
                    replicationTasks.Add(Task.Run(async () => 
                    {
                        try
                        {
                            await EnsureChunkReplicationAsync(
                                fileId, 
                                chunk.ChunkId, 
                                cancellationToken);
                        }
                        finally
                        {
                            // Always release the semaphore
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }
                
                // Wait for all tasks to complete
                await Task.WhenAll(replicationTasks);
                
                sw.Stop();
                _logger.LogInformation("Completed replication check for {ChunkCount} chunks of File {FileId} in {ElapsedMs}ms", 
                    chunks.Count(), fileId, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("EnsureReplicationLevelAsync cancelled for File {FileId}", fileId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring replication level for File {FileId}", fileId);
                throw;
            }
        }

        /// <summary>
        /// Handles incoming request to store a replica chunk on the current node
        /// </summary>
        public async Task HandleIncomingReplicaAsync(
            ChunkModel chunkInfo, 
            Stream dataStream, 
            CancellationToken cancellationToken = default)
        {
            if (chunkInfo == null) throw new ArgumentNullException(nameof(chunkInfo));
            if (dataStream == null) throw new ArgumentNullException(nameof(dataStream));
            if (string.IsNullOrEmpty(chunkInfo.FileId)) throw new ArgumentException("FileId cannot be empty", nameof(chunkInfo));
            if (string.IsNullOrEmpty(chunkInfo.ChunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkInfo));
            
            _logger.LogInformation("Handling incoming replica for Chunk {ChunkId} (File {FileId})", 
                chunkInfo.ChunkId, chunkInfo.FileId);
            
            try
            {
                // Step 1: Check if we have enough space
                long chunkSize = chunkInfo.Size > 0 ? chunkInfo.Size : dataStream.Length;
                long availableSpace = await _dataManager.GetFreeDiskSpaceAsync(cancellationToken);
                
                // Add a buffer (10%) to ensure we don't completely fill the disk
                long requiredSpace = (long)(chunkSize * 1.1);
                
                if (availableSpace < requiredSpace)
                {
                    _logger.LogError("Insufficient disk space to store incoming replica Chunk {ChunkId}. " +
                                    "Required: {Required}, Available: {Available}", 
                                    chunkInfo.ChunkId, FormatBytes(requiredSpace), FormatBytes(availableSpace));
                    throw new IOException($"Insufficient disk space to store replica. Required: {FormatBytes(requiredSpace)}, Available: {FormatBytes(availableSpace)}");
                }
                
                // Set the local node ID to ensure proper storage
                string originalNodeId = chunkInfo.StoredNodeId;
                chunkInfo.StoredNodeId = _localNodeId;
                
                // Step 2: Store the chunk data locally
                await _dataManager.StoreChunkAsync(
                    chunkInfo, 
                    dataStream, 
                    cancellationToken);
                
                // Step 3: Update metadata to reflect we now have this chunk
                await _metadataManager.SaveChunkMetadataAsync(
                    chunkInfo, 
                    new[] { _localNodeId }, 
                    cancellationToken);
                
                _logger.LogInformation("Successfully processed incoming replica for Chunk {ChunkId} from Node {OriginalNode}", 
                    chunkInfo.ChunkId, originalNodeId);
                
                // Step 4: Send acknowledgement to original sender if needed
                if (!string.IsNullOrEmpty(originalNodeId) && originalNodeId != _localNodeId)
                {
                    // Fire-and-forget acknowledgement task
                    _ = SendReplicaAcknowledgementAsync(
                        originalNodeId, chunkInfo.FileId, chunkInfo.ChunkId, _localNodeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling incoming replica for Chunk {ChunkId}", chunkInfo.ChunkId);
                throw;
            }
        }

        /// <summary>
        /// Handles notification that a chunk should be deleted
        /// </summary>
        public async Task HandleDeleteNotificationAsync(string chunkId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            
            _logger.LogInformation("Processing delete notification for Chunk {ChunkId}", chunkId);
            
            try
            {
                // Find chunks with this ID that are stored locally
                var chunks = await _metadataManager.GetChunksStoredLocallyAsync(cancellationToken);
                var localChunk = chunks?.FirstOrDefault(c => c.ChunkId == chunkId);
                
                if (localChunk == null)
                {
                    _logger.LogInformation("Chunk {ChunkId} not found locally, nothing to delete", chunkId);
                    return;
                }
                
                // Set the local node ID for deletion
                localChunk.StoredNodeId = _localNodeId;
                
                // Step 1: Delete the local chunk data
                await _dataManager.DeleteChunkAsync(localChunk, cancellationToken);
                
                // Step 2: Update metadata to reflect removal
                await _metadataManager.RemoveChunkStorageNodeAsync(
                    localChunk.FileId, 
                    localChunk.ChunkId, 
                    _localNodeId, 
                    cancellationToken);
                
                _logger.LogInformation("Successfully deleted local copy of Chunk {ChunkId}", chunkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling delete notification for Chunk {ChunkId}", chunkId);
                throw;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Gets the desired replica count from configuration or uses a default value
        /// </summary>
        private int GetDesiredReplicaCount()
        {
            return _dhtOptions.ReplicationFactor > 0 ? _dhtOptions.ReplicationFactor : 
                _storageOptions.DefaultReplicationFactor > 0 ? _storageOptions.DefaultReplicationFactor : 3;
        }

        /// <summary>
        /// Gets the list of nodes currently storing a specific chunk
        /// </summary>
        private async Task<List<string>> GetChunkStorageNodesAsync(
            string fileId, 
            string chunkId, 
            CancellationToken cancellationToken)
        {
            var nodes = await _metadataManager.GetChunkStorageNodesAsync(
                fileId, 
                chunkId, 
                cancellationToken);
            
            return nodes?.ToList() ?? new List<string>();
        }

        /// <summary>
        /// Finds which of the specified nodes are currently online
        /// </summary>
        private async Task<List<string>> FindOnlineNodesAsync(
            List<string> nodeIds, 
            CancellationToken cancellationToken)
        {
            var onlineNodes = new ConcurrentBag<string>();
            
            await Task.WhenAll(nodeIds.Select(async nodeId => {
                // Local node is always considered online
                if (nodeId == _localNodeId)
                {
                    onlineNodes.Add(nodeId);
                    return;
                }
                
                // Check if node is online
                if (await IsNodeOnlineAsync(nodeId, cancellationToken))
                {
                    onlineNodes.Add(nodeId);
                }
            }));
            
            return onlineNodes.ToList();
        }

        /// <summary>
        /// Checks if a node is online, using cache if available
        /// </summary>
        private async Task<bool> IsNodeOnlineAsync(string nodeId, CancellationToken cancellationToken)
        {
            // Local node is always considered online
            if (nodeId == _localNodeId) return true;
            
            // Check cache first
            if (_nodeStatusCache.TryGetValue(nodeId, out var status) && 
                (DateTime.UtcNow - status.LastChecked) < _nodeStatusCacheTtl)
            {
                return status.IsOnline;
            }
            
            // Get node info
            var nodeInfo = _networkOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
            if (nodeInfo == null || string.IsNullOrEmpty(nodeInfo.Address))
            {
                _logger.LogWarning("Cannot check status for Node {NodeId}: Not found in known nodes", nodeId);
                return false;
            }
            
            // Ping node
            bool isOnline = await ReplicationUtility.IsNodeOnlineAsync(
                nodeId, 
                nodeInfo.Address, 
                _localNodeId,
                _nodeClient,
                _logger,
                cancellationToken);
            
            // Update cache
            _nodeStatusCache[nodeId] = (isOnline, DateTime.UtcNow);
            
            return isOnline;
        }

        /// <summary>
        /// Finds all known nodes that are currently online
        /// </summary>
        private async Task<List<KnownNodeOptions>> FindAllOnlineNodesAsync(CancellationToken cancellationToken)
        {
            var onlineNodes = new ConcurrentBag<KnownNodeOptions>();
            
            // Add all nodes that respond to ping
            await Task.WhenAll(_networkOptions.KnownNodes.Select(async node => {
                if (await IsNodeOnlineAsync(node.NodeId, cancellationToken))
                {
                    onlineNodes.Add(node);
                }
            }));
            
            return onlineNodes.ToList();
        }

        /// <summary>
        /// Creates additional replicas for a chunk to meet the desired replication factor
        /// </summary>
        private async Task CreateAdditionalReplicasAsync(
            string fileId, 
            string chunkId, 
            List<string> onlineNodeIds, 
            int replicasToAdd, 
            CancellationToken cancellationToken)
        {
            // Check if we have the local data to replicate
            bool hasLocalData = onlineNodeIds.Contains(_localNodeId);
            if (!hasLocalData)
            {
                _logger.LogWarning("Cannot initiate re-replication for Chunk {ChunkId}: Local copy not available", chunkId);
                return;
            }
            
            // Get chunk info for local data retrieval
            var chunkInfo = await _metadataManager.GetChunkMetadataAsync(fileId, chunkId, cancellationToken);
            if (chunkInfo == null)
            {
                _logger.LogError("Cannot re-replicate Chunk {ChunkId}: Failed to retrieve its metadata", chunkId);
                return;
            }
            
            // Set node ID for local retrieval
            chunkInfo.StoredNodeId = _localNodeId;
            
            // Get the chunk data
            byte[]? chunkData = await GetLocalChunkDataAsync(chunkInfo, cancellationToken);
            if (chunkData == null)
            {
                _logger.LogError("Failed to get local data for Chunk {ChunkId}. Cannot re-replicate", chunkId);
                return;
            }
            
            // Get parent file metadata
            var fileMetadata = await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);
            
            // Find potential target nodes
            var potentialTargets = _networkOptions.KnownNodes
                .Where(n => n.NodeId != _localNodeId && !onlineNodeIds.Contains(n.NodeId))
                .ToList();
            
            // Find which potential targets are online
            var onlineTargets = new List<KnownNodeOptions>();
            foreach (var target in potentialTargets)
            {
                if (await IsNodeOnlineAsync(target.NodeId, cancellationToken))
                {
                    onlineTargets.Add(target);
                    if (onlineTargets.Count >= replicasToAdd) break;
                }
            }
            
            // Select target nodes
            var selectedTargets = ReplicationUtility.SelectReplicaTargets(
                onlineTargets, chunkId, replicasToAdd);
            
            if (selectedTargets.Count == 0)
            {
                _logger.LogWarning("No suitable targets found for re-replication of Chunk {ChunkId}", chunkId);
                return;
            }
            
            _logger.LogInformation("Re-replicating Chunk {ChunkId} to {Count} nodes: {Nodes}", 
                chunkId, selectedTargets.Count, string.Join(", ", selectedTargets.Select(n => n.NodeId)));
            
            // Track successful replications
            var successfulReplicas = new ConcurrentBag<string>();
            
            // Replicate to selected targets
            var replicationTasks = selectedTargets.Select(async node => 
            {
                bool success = await ReplicateToNodeAsync(
                    node, 
                    chunkInfo, 
                    chunkData, 
                    fileMetadata, 
                    cancellationToken);
                    
                if (success)
                {
                    successfulReplicas.Add(node.NodeId);
                }
            }).ToList();
            
            await Task.WhenAll(replicationTasks);
            
            // Update metadata with successful replications
            if (successfulReplicas.Any())
            {
                var allNodes = onlineNodeIds.Concat(successfulReplicas).Distinct().ToList();
                await _metadataManager.UpdateChunkStorageNodesAsync(
                    fileId, 
                    chunkId, 
                    allNodes, 
                    cancellationToken);
                    
                _logger.LogInformation("Updated storage nodes for Chunk {ChunkId}. Added {Count} new replicas.", 
                    chunkId, successfulReplicas.Count);
            }
        }

        /// <summary>
        /// Gets the data for a locally stored chunk
        /// </summary>
        private async Task<byte[]?> GetLocalChunkDataAsync(
            ChunkModel chunkInfo, 
            CancellationToken cancellationToken)
        {
            Stream? localDataStream = null;
            try
            {
                localDataStream = await _dataManager.RetrieveChunkAsync(chunkInfo, cancellationToken);
                if (localDataStream == null) return null;
                
                using var ms = new MemoryStream();
                await localDataStream.CopyToAsync(ms, cancellationToken);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read local data for Chunk {ChunkId}", chunkInfo.ChunkId);
                return null;
            }
            finally
            {
                if (localDataStream != null) await localDataStream.DisposeAsync();
            }
        }

        /// <summary>
        /// Replicates a chunk to a specific target node
        /// </summary>
        private async Task<bool> ReplicateToNodeAsync(
            KnownNodeOptions targetNode, 
            ChunkModel chunkInfo, 
            byte[] chunkData, 
            FileModel? fileMetadata, 
            CancellationToken cancellationToken)
        {
            if (targetNode == null) throw new ArgumentNullException(nameof(targetNode));
            if (string.IsNullOrEmpty(targetNode.Address)) throw new ArgumentException("Target node address is missing", nameof(targetNode));
            
            _logger.LogInformation("Replicating Chunk {ChunkId} to Node {NodeId} ({Address})", 
                chunkInfo.ChunkId, targetNode.NodeId, targetNode.Address);
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60)); // Set a reasonable timeout
            
            try
            {
                // Create replication request
                var replicateRequest = new ReplicateChunkRequest
                {
                    FileId = chunkInfo.FileId,
                    ChunkId = chunkInfo.ChunkId,
                    ChunkIndex = chunkInfo.ChunkIndex,
                    Data = ByteString.CopyFrom(chunkData),
                    OriginalNodeId = _localNodeId,
                    ParentFileMetadata = ReplicationUtility.MapCoreToProtoMetadata(fileMetadata)
                };
                
                // Send to target node
                var reply = await _nodeClient.ReplicateChunkToNodeAsync(
                    targetNode.Address, 
                    replicateRequest, 
                    cts.Token);
                
                if (!reply.Success)
                {
                    _logger.LogWarning("Failed to replicate Chunk {ChunkId} to Node {NodeId}: {Message}", 
                        chunkInfo.ChunkId, targetNode.NodeId, reply.Message);
                    return false;
                }
                
                _logger.LogInformation("Successfully replicated Chunk {ChunkId} to Node {NodeId}", 
                    chunkInfo.ChunkId, targetNode.NodeId);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Replication of Chunk {ChunkId} to Node {NodeId} timed out or was cancelled", 
                    chunkInfo.ChunkId, targetNode.NodeId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replicating Chunk {ChunkId} to Node {NodeId}", 
                    chunkInfo.ChunkId, targetNode.NodeId);
                return false;
            }
        }

        /// <summary>
        /// Sends an acknowledgement to a node that a replica was successfully stored 
        /// </summary>
        private async Task SendReplicaAcknowledgementAsync(
            string originalNodeId,
            string fileId,
            string chunkId,
            string replicaNodeId)
        {
            try
            {
                // Get the address of the original node
                var originalNode = _networkOptions.KnownNodes.FirstOrDefault(n => n.NodeId == originalNodeId);
                if (originalNode == null || string.IsNullOrEmpty(originalNode.Address))
                {
                    _logger.LogWarning("Cannot send acknowledgement: Address not found for Node {NodeId}", originalNodeId);
                    return;
                }
                
                var ackRequest = new AcknowledgeReplicaRequest
                {
                    FileId = fileId,
                    ChunkId = chunkId,
                    ReplicaNodeId = replicaNodeId,
                    OriginalSenderNodeId = originalNodeId // Optional field
                };
                
                _logger.LogDebug("Sending replica acknowledgement for Chunk {ChunkId} to Node {NodeId}", 
                    chunkId, originalNodeId);
                
                // Set a shorter timeout for acknowledgements
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                
                await _nodeClient.AcknowledgeReplicaAsync(
                    originalNode.Address, 
                    ackRequest, 
                    cts.Token);
                
                _logger.LogDebug("Successfully sent acknowledgement for Chunk {ChunkId} to Node {NodeId}", 
                    chunkId, originalNodeId);
            }
            catch (Exception ex)
            {
                // Log but don't throw - acknowledgements are best-effort
                _logger.LogWarning(ex, "Failed to send replica acknowledgement for Chunk {ChunkId} to Node {NodeId}", 
                    chunkId, originalNodeId);
            }
        }
        
        /// <summary>
        /// Formats a byte size into a human-readable string
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
        
        #endregion
    }
}