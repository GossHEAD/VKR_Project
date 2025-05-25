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
using AutoMapper;

namespace VKR_Node.Services
{
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
        private readonly IMapper _mapper;
        
        private readonly ConcurrentDictionary<string, Task> _activeReplicationTasks = new();

        public BackgroundReplicationManager(
            ILogger<BackgroundReplicationManager> logger,
            IMetadataManager metadataManager,
            IDataManager dataManager,
            INodeClient nodeClient,
            IOptions<NodeIdentityOptions> nodeIdentityOptions,
            IOptions<NetworkOptions> networkOptions,
            IOptions<StorageOptions> storageOptions,
            IOptions<DhtOptions> dhtOptions,
            IMapper mapper)
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
            _mapper = mapper;
            
            _logger.LogInformation("BackgroundReplicationManager initialized for Node {NodeId} with max parallelism {MaxParallelism}", 
                _localNodeId, maxParallelism);
        }
        
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
                    _activeReplicationTasks.TryRemove(operationKey, out _);
                }
            }
            
            var replicationTask = EnsureChunkReplicationInternalAsync(fileId, chunkId, cancellationToken);
            
            _activeReplicationTasks[operationKey] = replicationTask;
            
            try
            {
                await replicationTask;
            }
            finally
            {
                
                _activeReplicationTasks.TryRemove(operationKey, out _);
            }
        }
        
        private async Task EnsureChunkReplicationInternalAsync(string fileId, string chunkId, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Ensuring replication level for Chunk {ChunkId} (File {FileId})", chunkId, fileId);
            int desiredReplicas = GetDesiredReplicaCount();
            try
            {
                await _replicationSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var storedNodeIds = await GetChunkStorageNodesAsync(fileId, chunkId, cancellationToken);
                    if (!storedNodeIds.Any())
                    {
                        _logger.LogWarning("Cannot ensure replication for Chunk {ChunkId}: No storage locations found", chunkId);
                        return;
                    }

                    var onlineNodeIds = await FindOnlineNodesAsync(storedNodeIds, cancellationToken);
                    
                    int currentOnlineReplicas = onlineNodeIds.Count;
                    _logger.LogDebug("Chunk {ChunkId}: Desired={Desired}, Found={FoundTotal}, Online={OnlineCount}",
                                    chunkId, desiredReplicas, storedNodeIds.Count, currentOnlineReplicas);

                    if (currentOnlineReplicas >= desiredReplicas)
                    {
                        _logger.LogTrace("Chunk {ChunkId} has sufficient online replicas ({OnlineCount}/{Desired})", 
                            chunkId, currentOnlineReplicas, desiredReplicas);
                        return;
                    }
                    
                    await CreateAdditionalReplicasAsync(
                        fileId, 
                        chunkId, 
                        onlineNodeIds, 
                        desiredReplicas - currentOnlineReplicas, 
                        cancellationToken);
                }
                finally
                {
                    
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
                
                await _replicationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    
                    var existingNodeIds = await GetChunkStorageNodesAsync(
                        chunkInfo.FileId, chunkInfo.ChunkId, cancellationToken);
                    
                    
                    if (existingNodeIds.Count >= effectiveReplicationFactor)
                    {
                        _logger.LogInformation("Chunk {ChunkId} already has sufficient replicas ({ExistingCount}/{Required})",
                            chunkInfo.ChunkId, existingNodeIds.Count, effectiveReplicationFactor);
                        return;
                    }

                    
                    var allOnlineNodes = await FindAllOnlineNodesAsync(cancellationToken);
                    
                    
                    var availableNodes = allOnlineNodes
                        .Where(n => !existingNodeIds.Contains(n.NodeId))
                        .ToList();
                    
                    if (!availableNodes.Any())
                    {
                        _logger.LogWarning("No additional nodes available for replication of Chunk {ChunkId}", chunkInfo.ChunkId);
                        return;
                    }
                    
                    int replicasToAdd = Math.Min(
                        effectiveReplicationFactor - existingNodeIds.Count,
                        availableNodes.Count);
                    
                    if (replicasToAdd <= 0) return;
                    
                    
                    byte[] chunkData;
                    await using (var dataStream = await sourceDataStreamFactory())
                    {
                        using (var ms = new MemoryStream())
                        {
                            await dataStream.CopyToAsync(ms, cancellationToken);
                            chunkData = ms.ToArray();
                        }
                    }
                    
                    
                    var fileMetadata = await _metadataManager.GetFileMetadataAsync(
                        chunkInfo.FileId, cancellationToken);
                    
                    
                    var targetNodes = ReplicationUtility.SelectReplicaTargets(
                        availableNodes, chunkInfo.ChunkId, replicasToAdd);
                    
                    _logger.LogInformation("Replicating Chunk {ChunkId} to {Count} additional node(s): {Nodes}",
                        chunkInfo.ChunkId, targetNodes.Count, string.Join(", ", targetNodes.Select(n => n.NodeId)));
                    
                    var successfulReplicationNodes = new ConcurrentBag<string>();
                    
                    
                    var replicationTasks = targetNodes.Select(async node => 
                    {
                        try
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
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error replicating to node {NodeId}", node.NodeId);
                        }
                    }).ToList();
                    
                    
                    await Task.WhenAll(replicationTasks);
                    
                    
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
                    _replicationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replicating Chunk {ChunkId}", chunkInfo.ChunkId);
                throw;
            }
        }
        
        public async Task EnsureReplicationLevelAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            _logger.LogInformation("Checking replication level for all chunks of File {FileId}", fileId);
            try
            {
                var chunks = await _metadataManager.GetChunksMetadataForFileAsync(fileId, cancellationToken);
                
                _logger.LogDebug("Found {Count} chunks for File {FileId}", chunks.Count(), fileId);
                
                var replicationTasks = new List<Task>();
                var sw = Stopwatch.StartNew();
                
                int maxConcurrentChunks = _dhtOptions.ReplicationMaxParallelism;
                var semaphore = new SemaphoreSlim(maxConcurrentChunks);
                
                foreach (var chunk in chunks)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    
                    await semaphore.WaitAsync(cancellationToken);
                    
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
                            
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }
                
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
                long chunkSize = chunkInfo.Size > 0 ? chunkInfo.Size : dataStream.Length;
                long availableSpace = await _dataManager.GetFreeDiskSpaceAsync(cancellationToken);
                
                long requiredSpace = (long)(chunkSize * 1.1);
                
                if (availableSpace < requiredSpace)
                {
                    _logger.LogError("Insufficient disk space to store incoming replica Chunk {ChunkId}. " +
                                    "Required: {Required}, Available: {Available}", 
                                    chunkInfo.ChunkId, FormatBytes(requiredSpace), FormatBytes(availableSpace));
                    throw new IOException($"Insufficient disk space to store replica. Required: {FormatBytes(requiredSpace)}, Available: {FormatBytes(availableSpace)}");
                }
                
                string originalNodeId = chunkInfo.StoredNodeId;
                chunkInfo.StoredNodeId = _localNodeId;
                
                await _dataManager.StoreChunkAsync(
                    chunkInfo, 
                    dataStream, 
                    cancellationToken);
                
                await _metadataManager.SaveChunkMetadataAsync(
                    chunkInfo, 
                    new[] { _localNodeId }, 
                    cancellationToken);
                
                _logger.LogInformation("Successfully processed incoming replica for Chunk {ChunkId} from Node {OriginalNode}", 
                    chunkInfo.ChunkId, originalNodeId);
                
                if (!string.IsNullOrEmpty(originalNodeId) && originalNodeId != _localNodeId)
                {
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
        
        public async Task HandleDeleteNotificationAsync(string chunkId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            
            _logger.LogInformation("Processing delete notification for Chunk {ChunkId}", chunkId);
            try
            {
                var chunks = await _metadataManager.GetChunksStoredLocallyAsync(cancellationToken);
                var localChunk = chunks?.FirstOrDefault(c => c.ChunkId == chunkId);
                
                if (localChunk == null)
                {
                    _logger.LogInformation("Chunk {ChunkId} not found locally, nothing to delete", chunkId);
                    return;
                }
                
                localChunk.StoredNodeId = _localNodeId;
                
                await _dataManager.DeleteChunkAsync(localChunk, cancellationToken);
                
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
        
        private int GetDesiredReplicaCount()
        {
            return _dhtOptions.ReplicationFactor > 0 ? _dhtOptions.ReplicationFactor : 
                _storageOptions.DefaultReplicationFactor > 0 ? _storageOptions.DefaultReplicationFactor : 3;
        }
        
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
        
        private async Task<List<string>> FindOnlineNodesAsync(
            List<string> nodeIds, 
            CancellationToken cancellationToken)
        {
            var onlineNodes = new ConcurrentBag<string>();
            
            await Task.WhenAll(nodeIds.Select(async nodeId => {
                
                if (nodeId == _localNodeId)
                {
                    onlineNodes.Add(nodeId);
                    return;
                }
                
                if (await IsNodeOnlineAsync(nodeId, cancellationToken))
                {
                    onlineNodes.Add(nodeId);
                }
            }));
            return onlineNodes.ToList();
        }
        
        private async Task<bool> IsNodeOnlineAsync(string nodeId, CancellationToken cancellationToken)
        {
            if (nodeId == _localNodeId) return true;
            
            if (_nodeStatusCache.TryGetValue(nodeId, out var status) && 
                (DateTime.UtcNow - status.LastChecked) < _nodeStatusCacheTtl)
            {
                return status.IsOnline;
            }
            
            var nodeInfo = _networkOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
            if (nodeInfo == null || string.IsNullOrEmpty(nodeInfo.Address))
            {
                _logger.LogWarning("Cannot check status for Node {NodeId}: Not found in known nodes", nodeId);
                return false;
            }
            
            bool isOnline = await ReplicationUtility.IsNodeOnlineAsync(
                nodeId, 
                nodeInfo.Address, 
                _localNodeId,
                _nodeClient,
                _logger,
                cancellationToken);
            _nodeStatusCache[nodeId] = (isOnline, DateTime.UtcNow);
            
            return isOnline;
        }
        
        private async Task<List<KnownNodeOptions>> FindAllOnlineNodesAsync(CancellationToken cancellationToken)
        {
            var onlineNodes = new ConcurrentBag<KnownNodeOptions>();
            var nodesToCheck = new List<KnownNodeOptions>();
            
            foreach (var node in _networkOptions.KnownNodes)
            {
                if (_nodeStatusCache.TryGetValue(node.NodeId, out var status) && 
                    (DateTime.UtcNow - status.LastChecked) < _nodeStatusCacheTtl)
                {
                    if (status.IsOnline)
                    {
                        onlineNodes.Add(node);
                    }
                }
                else
                {
                    nodesToCheck.Add(node);
                }
            }
    
            if (nodesToCheck.Any())
            {
                var pingTasks = nodesToCheck.Select(async node => {
                    bool isOnline = await IsNodeOnlineAsync(node.NodeId, cancellationToken);
                    _nodeStatusCache[node.NodeId] = (isOnline, DateTime.UtcNow);
            
                    if (isOnline)
                    {
                        onlineNodes.Add(node);
                    }
                });
        
                await Task.WhenAll(pingTasks);
            }
    
            return onlineNodes.ToList();
        }
        private async Task CreateAdditionalReplicasAsync(
            string fileId, 
            string chunkId, 
            List<string> onlineNodeIds, 
            int replicasToAdd, 
            CancellationToken cancellationToken)
        {
            bool hasLocalData = onlineNodeIds.Contains(_localNodeId);
            if (!hasLocalData)
            {
                _logger.LogWarning("Cannot initiate re-replication for Chunk {ChunkId}: Local copy not available", chunkId);
                return;
            }
            
            var chunkInfo = await _metadataManager.GetChunkMetadataAsync(fileId, chunkId, cancellationToken);
            if (chunkInfo == null)
            {
                _logger.LogError("Cannot re-replicate Chunk {ChunkId}: Failed to retrieve its metadata", chunkId);
                return;
            }
            
            chunkInfo.StoredNodeId = _localNodeId;
            
            byte[]? chunkData = await GetLocalChunkDataAsync(chunkInfo, cancellationToken);
            if (chunkData == null)
            {
                _logger.LogError("Failed to get local data for Chunk {ChunkId}. Cannot re-replicate", chunkId);
                return;
            }
            
            var fileMetadata = await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);
            
            var potentialTargets = _networkOptions.KnownNodes
                .Where(n => n.NodeId != _localNodeId && !onlineNodeIds.Contains(n.NodeId))
                .ToList();
            
            var onlineTargets = new List<KnownNodeOptions>();
            foreach (var target in potentialTargets)
            {
                if (await IsNodeOnlineAsync(target.NodeId, cancellationToken))
                {
                    onlineTargets.Add(target);
                    if (onlineTargets.Count >= replicasToAdd) break;
                }
            }
            
            var selectedTargets = ReplicationUtility.SelectReplicaTargets(
                onlineTargets, chunkId, replicasToAdd);
            
            if (selectedTargets.Count == 0)
            {
                _logger.LogWarning("No suitable targets found for re-replication of Chunk {ChunkId}", chunkId);
                return;
            }
            
            _logger.LogInformation("Re-replicating Chunk {ChunkId} to {Count} nodes: {Nodes}", 
                chunkId, selectedTargets.Count, string.Join(", ", selectedTargets.Select(n => n.NodeId)));
            
            var successfulReplicas = new ConcurrentBag<string>();
            
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
            cts.CancelAfter(TimeSpan.FromSeconds(60)); 
            
            try
            {
                
                var metadata = new ReplicateChunkMetadata
                {
                    FileId = chunkInfo.FileId,
                    ChunkId = chunkInfo.ChunkId,
                    ChunkIndex = chunkInfo.ChunkIndex,
                    Size = chunkInfo.Size,
                    OriginalNodeId = _localNodeId,
                    ParentFileMetadata = fileMetadata != null ? _mapper.Map<FileMetadata>(fileMetadata) : null
                };
                
                
                using (var dataStream = new MemoryStream(chunkData))
                {
                    var reply = await _nodeClient.ReplicateChunkToNodeStreamingAsync(
                        targetNode.Address, 
                        metadata,
                        dataStream,
                        cts.Token);
            
                    if (!reply.Success)
                    {
                        _logger.LogWarning("Failed to replicate Chunk {ChunkId} to Node {NodeId}: {Message}", 
                            chunkInfo.ChunkId, targetNode.NodeId, reply.Message);
                        return false;
                    }
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
        
        private async Task SendReplicaAcknowledgementAsync(
            string originalNodeId,
            string fileId,
            string chunkId,
            string replicaNodeId)
        {
            try
            {
                
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
                    OriginalSenderNodeId = originalNodeId 
                };
                
                _logger.LogDebug("Sending replica acknowledgement for Chunk {ChunkId} to Node {NodeId}", 
                    chunkId, originalNodeId);
                
                
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
                
                _logger.LogWarning(ex, "Failed to send replica acknowledgement for Chunk {ChunkId} to Node {NodeId}", 
                    chunkId, originalNodeId);
            }
        }
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"]; 
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