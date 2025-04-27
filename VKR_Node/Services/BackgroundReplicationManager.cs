using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos; // For ReplicateChunkRequest
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using VKR_Core.Enums;
using VKR.Node.Persistance.Entities; // For ByteString

namespace VKR_Node.Services
{
    public class BackgroundReplicationManager : IReplicationManager
    {
        private readonly ILogger<BackgroundReplicationManager> _logger;
        private readonly IMetadataManager _metadataManager;
        private readonly IDataManager _dataManager;
        private readonly INodeClient _nodeClient;
        private readonly NodeOptions _nodeOptions;
        private readonly DhtOptions _dhtOptions;

        // Constructor injection
        public BackgroundReplicationManager(
            ILogger<BackgroundReplicationManager> logger,
            IMetadataManager metadataManager,
            IDataManager dataManager,
            INodeClient nodeClient,
            IOptions<NodeOptions> nodeOptions,
            IOptions<DhtOptions> dhtOptions)
        {
            _logger = logger;
            _metadataManager = metadataManager;
            _dataManager = dataManager;
            _nodeClient = nodeClient;
            _nodeOptions = nodeOptions.Value;
            _dhtOptions = dhtOptions.Value;
        }

        // --- Core Method to Check and Heal a Single Chunk ---
        public async Task EnsureChunkReplicationAsync(string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Ensuring replication level for Chunk {ChunkId} (File {FileId})", chunkId, fileId);
            int desiredReplicas = _dhtOptions.ReplicationFactor > 0 ? _dhtOptions.ReplicationFactor : 3;
            try
            {
                var storedOnNodeIds = (await _metadataManager.GetChunkStorageNodesAsync(fileId, chunkId, cancellationToken)).ToList();
                if (!storedOnNodeIds.Any())
                {
                    _logger.LogWarning("Cannot ensure replication for Chunk {ChunkId}: No storage locations found in metadata.", chunkId);
                    return; // Should not happen if called by health service checking local chunks, but safety check.
                }

                // Ping nodes listed in metadata to get current online status
                var onlineNodesHoldingChunk = new ConcurrentBag<string>();
                var checkTasks = storedOnNodeIds.Select(nodeId => Task.Run(async () => {
                    if (nodeId == _nodeOptions.NodeId) {
                         onlineNodesHoldingChunk.Add(nodeId);
                         return;
                    }
                    var nodeInfo = _nodeOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
                    if (nodeInfo != null && !string.IsNullOrEmpty(nodeInfo.Address)) {
                         try {
                             var pingRequest = new PingRequest { SenderNodeId = _nodeOptions.NodeId };
                             var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(5));
                             // Assumes PingNodeAsync exists and respects cancellation/deadline
                             var reply = await _nodeClient.PingNodeAsync(nodeInfo.Address, pingRequest);
                             if (reply.Success) onlineNodesHoldingChunk.Add(nodeId);
                         } catch (Exception ex) {
                             _logger.LogWarning(ex, "Ping failed for node {NodeId} ({Address}) while checking current replicas.", nodeId, nodeInfo.Address);
                             // Treat as offline if ping fails
                         }
                    }
                })).ToList();
                await Task.WhenAll(checkTasks);
                var onlineNodeIdList = onlineNodesHoldingChunk.Distinct().ToList(); // Use Distinct just in case

                int currentOnlineReplicas = onlineNodeIdList.Count;
                _logger.LogDebug("Chunk {ChunkId}: Desired={Desired}, FoundInMeta={FoundTotal}, Online={OnlineCount}",
                                 chunkId, desiredReplicas, storedOnNodeIds.Count, currentOnlineReplicas);

                if (currentOnlineReplicas >= desiredReplicas)
                {
                     _logger.LogTrace("Chunk {ChunkId} has sufficient online replicas ({OnlineCount}/{Desired}).", chunkId, currentOnlineReplicas, desiredReplicas);
                    return; // Enough replicas online
                }

                // --- Need to Re-replicate ---
                int replicasToAdd = desiredReplicas - currentOnlineReplicas;
                _logger.LogInformation("Chunk {ChunkId} is under-replicated ({OnlineCount}/{Desired}). Need to add {ReplicasToAdd} replica(s).", chunkId, currentOnlineReplicas, desiredReplicas, replicasToAdd);

                // Check if WE (the current node) have the chunk data locally and are online
                if (!onlineNodeIdList.Contains(_nodeOptions.NodeId))
                {
                    _logger.LogWarning("Cannot initiate re-replication for Chunk {ChunkId}: Local copy not found or this node is offline.", chunkId);
                    return; // Cannot source the data from this node
                }

                var chunkInfoForRetrieval = await _metadataManager.GetChunkMetadataAsync(fileId, chunkId, cancellationToken);
                if (chunkInfoForRetrieval == null)
                {
                    _logger.LogError("Cannot re-replicate Chunk {ChunkId}: Failed to retrieve its metadata.", chunkId);
                    return;
                }
                chunkInfoForRetrieval.StoredNodeId = _nodeOptions.NodeId; // We are retrieving it locally

                // Retrieve local data
                byte[]? chunkData = await GetLocalChunkDataAsync(chunkInfoForRetrieval, cancellationToken); // Use existing helper
                if (chunkData == null)
                {
                    _logger.LogError("Failed to get local data for Chunk {ChunkId}. Cannot re-replicate.", chunkId);
                    return;
                }

                // Fetch Parent Metadata to send with replica
                FileMetadataCore? fileMetadataCore = null;
                try {
                    fileMetadataCore = await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);
                    if (fileMetadataCore == null) {
                         _logger.LogWarning("Cannot find FileMetadata for {FileId} during re-replication of Chunk {ChunkId}. Metadata will not be sent.", fileId, chunkId);
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to retrieve FileMetadata for {FileId} during re-replication. Cannot send metadata.", fileId);
                }

                // Find potential target nodes (KnownNodes excluding self and those already storing the chunk)
                var potentialTargetNodeInfo = (_nodeOptions.KnownNodes ?? new List<ChunkStorageNode>())
                    .Where(n => n.NodeId != _nodeOptions.NodeId &&
                                !storedOnNodeIds.Contains(n.NodeId) && // Don't already have it
                                !string.IsNullOrEmpty(n.Address))
                    .ToList();
                _logger.LogDebug("Chunk {ChunkId}: Found {Count} potential peer nodes for re-replication.", chunkId, potentialTargetNodeInfo.Count);

                // Check which potential targets are online via Ping
                var onlineTargets = new ConcurrentBag<ChunkStorageNode>();
                var checkOnlineTasks = potentialTargetNodeInfo.Select(peer => Task.Run(async () => {
                    try {
                        var pingRequest = new PingRequest { SenderNodeId = _nodeOptions.NodeId };
                        var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(5));
                        var reply = await _nodeClient.PingNodeAsync(peer.Address, pingRequest);
                        if (reply.Success) onlineTargets.Add(peer);
                    } catch (Exception ex) { _logger.LogWarning(ex, "Error pinging potential re-replication target {NodeId} ({Address}).", peer.NodeId, peer.Address); }
                })).ToList();
                await Task.WhenAll(checkOnlineTasks);
                var suitableTargets = onlineTargets.ToList();

                // Optional: Add load balancing filtering here later if needed, using GetNodeStatesAsync and filtering suitableTargets further

                _logger.LogDebug("Chunk {ChunkId}: Found {SuitableCount} suitable (online) targets for re-replication.", chunkId, suitableTargets.Count);

                // --- Trigger Re-replication Tasks ---
                var replicationTasks = new List<Task>();
                int addedReplicas = 0;

                // Select targets (e.g., use helper or just take N)
                var targetsToReplicate = SelectReplicaTargetsFromInfo(suitableTargets, chunkId, replicasToAdd); // Use helper if defined

                foreach (var targetNode in targetsToReplicate)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    string? targetAddress = targetNode.Address; // Get address from targetNode
                    if (string.IsNullOrEmpty(targetAddress)) continue;

                    _logger.LogInformation("Attempting to re-replicate Chunk {ChunkId} to target Node {TargetNodeId} ({TargetNodeAddress})", chunkId, targetNode.NodeId, targetAddress);
                    addedReplicas++;

                    // Create ReplicateChunkRequest INCLUDING parent metadata
                    var replicateRequest = new ReplicateChunkRequest
                    {
                        FileId = fileId,
                        ChunkId = chunkId,
                        ChunkIndex = chunkInfoForRetrieval.ChunkIndex,
                        Data = ByteString.CopyFrom(chunkData),
                        OriginalNodeId = _nodeOptions.NodeId, // Indicate origin node
                        // Populate Parent File Metadata (ASSUMES PROTO UPDATED)
                        ParentFileMetadata = MapCoreToProtoMetadataPartial(fileMetadataCore) // Use mapping helper
                    };

                    replicationTasks.Add(ReplicateToNodeTask(targetAddress, replicateRequest, cancellationToken)); // Use existing helper
                }

                await Task.WhenAll(replicationTasks);

                if (addedReplicas < replicasToAdd)
                {
                    _logger.LogWarning("Could not find enough suitable online targets for Chunk {ChunkId}. Added {AddedCount}/{NeededCount}", chunkId, addedReplicas, replicasToAdd);
                } else {
                     _logger.LogInformation("Successfully initiated {AddedCount} re-replications for Chunk {ChunkId}.", addedReplicas, chunkId);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("EnsureChunkReplicationAsync cancelled for Chunk {ChunkId}.", chunkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring replication level for Chunk {ChunkId}", chunkId);
            }
        }
        
        // Helper to get local chunk data
        private async Task<byte[]?> GetLocalChunkDataAsync(ChunkInfoCore chunkInfo, CancellationToken cancellationToken)
        {
            Stream? localDataStream = null;
            try
            {
                localDataStream = await _dataManager.RetrieveChunkAsync(chunkInfo, cancellationToken);
                if (localDataStream == null)
                {
                    _logger.LogError("Cannot get local data for Chunk {ChunkId}: RetrieveChunkAsync returned null (Node {NodeId}).", chunkInfo.ChunkId, _nodeOptions.NodeId);
                    return null;
                }
                using (var ms = new MemoryStream())
                {
                    await localDataStream.CopyToAsync(ms, cancellationToken);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read local data stream for Chunk {ChunkId}.", chunkInfo.ChunkId);
                return null;
            }
            finally
            {
                if (localDataStream != null) await localDataStream.DisposeAsync();
            }
        }

        // Helper task for sending replication request
        private async Task ReplicateToNodeTask(string targetAddress, ReplicateChunkRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                // Note: Cancellation token might not be easily passed down through INodeClient depending on its implementation
                var reply = await _nodeClient.ReplicateChunkToNodeAsync(targetAddress, request);
                if (!reply.Success)
                    _logger.LogWarning("Re-replication of chunk {ChunkId} to {TargetNodeAddress} failed: {Message}",
                        request.ChunkId, targetAddress, reply.Message);
                else
                    _logger.LogInformation("Re-replication call for chunk {ChunkId} to {TargetNodeAddress} completed.",
                        request.ChunkId, targetAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Exception during background re-replication task for chunk {ChunkId} to {TargetNodeAddress}.",
                    request.ChunkId, targetAddress);
            }
        }

        
        private List<ChunkStorageNode> SelectReplicaTargetsFromInfo(List<ChunkStorageNode> onlinePeers, string chunkId, int replicasNeeded)
        {
            if (onlinePeers == null || !onlinePeers.Any() || replicasNeeded <= 0)
            {
                return new List<ChunkStorageNode>();
            }
            if (onlinePeers.Count <= replicasNeeded)
            {
                return onlinePeers; // Not enough suitable peers, return all
            }
            int hashCode = Math.Abs(chunkId.GetHashCode());
            int startIndex = hashCode % onlinePeers.Count;
            var selectedTargets = new List<ChunkStorageNode>();
            for (int i = 0; i < replicasNeeded; i++)
            {
                selectedTargets.Add(onlinePeers[(startIndex + i) % onlinePeers.Count]);
            }
            return selectedTargets;
        }

        // Mapping helper method (Ensure correct namespaces for Proto types)
        private static VKR.Protos.FileMetadata? MapCoreToProtoMetadataPartial(FileMetadataCore? core)
        {
            if (core == null) return null;

            return new VKR.Protos.FileMetadata // Use generated Proto type
            {
                FileName = core.FileName ?? string.Empty,
                FileSize = core.FileSize,
                CreationTime = Timestamp.FromDateTime(core.CreationTime.ToUniversalTime()), // Ensure UTC
                ModificationTime = Timestamp.FromDateTime(core.ModificationTime.ToUniversalTime()), // Ensure UTC
                ContentType = core.ContentType ?? string.Empty,
                ChunkSize = core.ChunkSize,
                TotalChunks = core.TotalChunks,
                State = (VKR.Protos.FileState)core.State // Cast Core enum to Proto enum
            };
        }

        // Helper to check node status (can use cache for efficiency within one cycle)
        private async Task<bool> IsNodeOnlineAsync(string nodeId, CancellationToken cancellationToken,
            ConcurrentDictionary<string, bool> statusCache)
        {
            if (nodeId == _nodeOptions.NodeId) return true; 

            // Check cache first
            if (statusCache.TryGetValue(nodeId, out bool cachedStatus))
            {
                _logger.LogTrace("Using cached status for Node {NodeId}: Online={Status}", nodeId, cachedStatus);
                return cachedStatus;
            }

            var nodeInfo = _nodeOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
            if (nodeInfo == null || string.IsNullOrEmpty(nodeInfo.Address))
            {
                _logger.LogTrace("Cannot check status for Node {NodeId}: Not found in KnownNodes or address missing.",
                    nodeId);
                statusCache[nodeId] = false; 
                return false;
            }

            bool isOnline = false;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5)); 

                var pingRequest = new PingRequest { SenderNodeId = _nodeOptions.NodeId };
                // Assuming PingNodeAsync exists and handles cancellation internally or via options
                var reply = await _nodeClient.PingNodeAsync(nodeInfo.Address, pingRequest);
                isOnline = reply?.Success ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ping failed for node {NodeId} ({NodeAddress}) during online check.", nodeId,
                    nodeInfo.Address);
                isOnline = false;
            }

            statusCache[nodeId] = isOnline; // Cache result
            _logger.LogTrace("Ping result for Node {NodeId}: Online={Status}", nodeId, isOnline);
            return isOnline;
        }


        // --- Other IReplicationManager Methods (Implement further as needed) ---
        // These might remain simple if all logic is driven by the health check for now

        public Task ReplicateChunkAsync(ChunkInfoCore chunkInfo, Func<Task<Stream>> sourceDataStreamFactory,
            int replicationFactor, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning(
                "IReplicationManager.ReplicateChunkAsync not fully implemented. Replication triggered elsewhere (Upload/Health Check).");
            return Task.CompletedTask;
        }

        public Task EnsureReplicationLevelAsync(string fileId, CancellationToken cancellationToken = default)
        {
            // This could iterate through all chunks of a file and call EnsureChunkReplicationAsync
            _logger.LogWarning(
                "IReplicationManager.EnsureReplicationLevelAsync not fully implemented. Checks done per chunk by health service.");
            return Task.CompletedTask;
        }

        public Task HandleIncomingReplicaAsync(ChunkInfoCore chunkInfo, Stream dataStream,
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning(
                "IReplicationManager.HandleIncomingReplicaAsync not fully implemented. Handled by gRPC service.");
            return Task.CompletedTask;
        }

        public Task HandleDeleteNotificationAsync(string chunkId, CancellationToken cancellationToken = default)
        {
            // Might need implementation if delete process needs central coordination via this manager
            _logger.LogWarning(
                "IReplicationManager.HandleDeleteNotificationAsync not fully implemented. Handled by gRPC service.");
            return Task.CompletedTask;
        }
    }
}