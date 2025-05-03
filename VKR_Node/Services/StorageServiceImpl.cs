using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Enums;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos;
using VKR_Node.Services.FileService.FileInterface;
using VKR_Node.Services.NodeServices.NodeInterfaces;


namespace VKR_Node.Services
{
    public class StorageServiceImpl : StorageService.StorageServiceBase
    {
        private readonly IFileStorageService _fileService;
        private readonly INodeStatusService _nodeStatusService;
        private readonly INodeConfigService _nodeConfigService;
        private readonly ILogger<StorageServiceImpl> _logger;
        private readonly IMetadataManager _metadataManager;
        private readonly IReplicationManager _replicationManager;
        private readonly DhtOptions  _dhtOptions;
        
        public StorageServiceImpl(
            IFileStorageService fileService,
            INodeStatusService nodeStatusService,
            INodeConfigService nodeConfigService,
            ILogger<StorageServiceImpl> logger,
            IMetadataManager metadataManager,
            IReplicationManager replicationManager,
            IOptions<DhtOptions> dhtOptions)
        {
            _fileService = fileService;
            _nodeStatusService = nodeStatusService;
            _nodeConfigService = nodeConfigService;
            _logger = logger;
            _metadataManager = metadataManager;
            _replicationManager = replicationManager;
            _dhtOptions = dhtOptions.Value;
        }

        public override Task<ListFilesReply> ListFiles(
            ListFilesRequest request,
            ServerCallContext context)
        {
            return _fileService.ListFiles(request, context);
        }

        public override Task<UploadFileReply> UploadFile(
            IAsyncStreamReader<UploadFileRequest> requestStream,
            ServerCallContext context)
        {
            return _fileService.UploadFile(requestStream, context);
        }

        public override Task DownloadFile(
            DownloadFileRequest request,
            IServerStreamWriter<DownloadFileReply> responseStream,
            ServerCallContext context)
        {
            return _fileService.DownloadFile(request, responseStream, context);
        }

        public override Task<DeleteFileReply> DeleteFile(
            DeleteFileRequest request,
            ServerCallContext context)
        {
            return _fileService.DeleteFile(request, context);
        }

        public override Task<GetFileStatusReply> GetFileStatus(
            GetFileStatusRequest request,
            ServerCallContext context)
        {
            return _fileService.GetFileStatus(request, context);
        }

        public override Task<GetNodeStatusesReply> GetNodeStatuses(
            GetNodeStatusesRequest request,
            ServerCallContext context)
        {
            return _nodeStatusService.GetNodeStatuses(request, context);
        }

        public override Task<GetNodeConfigurationReply> GetNodeConfiguration(
            GetNodeConfigurationRequest request,
            ServerCallContext context)
        {
            return _nodeConfigService.GetNodeConfiguration(request, context);
        }
        
        public override async Task<SimulateNodeFailureReply> SimulateNodeFailure(
            SimulateNodeFailureRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received request to simulate failure of node: {NodeId}", request.NodeId);
            
            if (string.IsNullOrEmpty(request.NodeId))
            {
                return new SimulateNodeFailureReply 
                { 
                    Success = false, 
                    Message = "Node ID cannot be empty." 
                };
            }
            
            try
            {
                var nodeStates = await _metadataManager.GetNodeStatesAsync(
                    new[] { request.NodeId }, context.CancellationToken);
                
                var nodeState = nodeStates.FirstOrDefault();
                if (nodeState == null)
                {
                    return new SimulateNodeFailureReply 
                    { 
                        Success = false, 
                        Message = $"Node with ID {request.NodeId} not found." 
                    };
                }
                
                nodeState = nodeState with { State = NodeStateCore.Offline };
                nodeState = nodeState with { LastSeen = DateTime.UtcNow };
                
                await _metadataManager.SaveNodeStateAsync(nodeState, context.CancellationToken);
                
                _logger.LogInformation("Simulated failure of node {NodeId} by marking it as Offline", request.NodeId);
                
                await EnsureReplicationForAffectedChunksAsync(request.NodeId, context.CancellationToken);
                
                return new SimulateNodeFailureReply 
                { 
                    Success = true, 
                    Message = $"Node {request.NodeId} has been marked as offline for simulation." 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error simulating failure of node {NodeId}", request.NodeId);
                return new SimulateNodeFailureReply 
                { 
                    Success = false, 
                    Message = $"Error: {ex.Message}" 
                };
            }
        }

        public override async Task<RestoreAllNodesReply> RestoreAllNodes(
            RestoreAllNodesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received request to restore all nodes");
            
            try
            {
                // Get all nodes
                var allNodes = await _metadataManager.GetAllNodeStatesAsync(context.CancellationToken);
                var offlineNodes = allNodes.Where(n => n.State == NodeStateCore.Offline).ToList();
                
                if (!offlineNodes.Any())
                {
                    return new RestoreAllNodesReply 
                    { 
                        Success = true, 
                        Message = "No offline nodes found to restore." 
                    };
                }
                
                // Mark all offline nodes as Online
                foreach (var node in offlineNodes)
                {
                    node.State = NodeStateCore.Online;
                    node.LastSeen = DateTime.UtcNow;
                    node.LastSuccessfulPingTimestamp = DateTime.UtcNow;
                    
                    await _metadataManager.SaveNodeStateAsync(node, context.CancellationToken);
                    _logger.LogInformation("Restored node {NodeId} to Online state", node.Id);
                }
                
                return new RestoreAllNodesReply 
                { 
                    Success = true, 
                    Message = $"Restored {offlineNodes.Count} nodes to Online state." 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring nodes");
                return new RestoreAllNodesReply 
                { 
                    Success = false, 
                    Message = $"Error: {ex.Message}" 
                };
            }
        }

        public override async Task<GetFileStatusesReply> GetFileStatuses(
            GetFileStatusesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received request for file statuses");
            var reply = new GetFileStatusesReply();
            
            try
            {
                var files = await _metadataManager.ListFilesAsync(context.CancellationToken);
                
                foreach (var file in files)
                {
                    var chunks = await _metadataManager.GetChunksMetadataForFileAsync(file.FileId, context.CancellationToken);
                    bool isFileAvailable = true;
                    int currentReplication = int.MaxValue;
                    
                    foreach (var chunk in chunks)
                    {
                        var nodesForChunk = await _metadataManager.GetChunkStorageNodesAsync(
                            file.FileId, chunk.ChunkId, context.CancellationToken);
                        
                        var onlineNodes = await GetOnlineNodesAsync(nodesForChunk.ToList(), context.CancellationToken);
                        
                        if (!onlineNodes.Any())
                        {
                            isFileAvailable = false;
                        }
                        
                        currentReplication = Math.Min(currentReplication, onlineNodes.Count);
                    }
                    
                    if (!chunks.Any())
                    {
                        currentReplication = 0;
                    }
                    
                    reply.FileStatuses.Add(new FileStatusInfo
                    {
                        FileId = file.FileId,
                        FileName = file.FileName,
                        IsAvailable = isFileAvailable,
                        CurrentReplicationFactor = currentReplication == int.MaxValue ? 0 : currentReplication,
                        DesiredReplicationFactor = _dhtOptions.ReplicationFactor
                    });
                }
                
                return reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file statuses");
                return reply;
            }
        }

        public override async Task<GetChunkDistributionReply> GetChunkDistribution(
            GetChunkDistributionRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received request for chunk distribution");
            var reply = new GetChunkDistributionReply();
            
            try
            {
                var files = await _metadataManager.ListFilesAsync(context.CancellationToken);
                
                foreach (var file in files)
                {
                    var chunks = await _metadataManager.GetChunksMetadataForFileAsync(file.FileId, context.CancellationToken);
                    
                    foreach (var chunk in chunks)
                    {
                        var nodeIds = await _metadataManager.GetChunkStorageNodesAsync(
                            file.FileId, chunk.ChunkId, context.CancellationToken);
                        
                        var distributionInfo = new ChunkDistributionInfo
                        {
                            ChunkId = chunk.ChunkId,
                            FileId = file.FileId,
                            FileName = file.FileName
                        };
                        
                        distributionInfo.NodeIds.AddRange(nodeIds);
                        reply.ChunkDistributions.Add(distributionInfo);
                    }
                }
                
                return reply;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chunk distribution");
                return reply;
            }
        }

        private async Task<List<string>> GetOnlineNodesAsync(List<string> nodeIds, CancellationToken cancellationToken)
        {
            if (!nodeIds.Any()) return new List<string>();
            
            var nodeStates = await _metadataManager.GetNodeStatesAsync(nodeIds, cancellationToken);
            return nodeStates
                .Where(n => n.State == NodeStateCore.Online)
                .Select(n => n.Id)
                .ToList();
        }

        private async Task EnsureReplicationForAffectedChunksAsync(string nodeId, CancellationToken cancellationToken)
        {
            try
            {
                var allChunks = await _metadataManager.GetChunksStoredLocallyAsync(cancellationToken);
                var chunksOnFailedNode = allChunks
                    .Where(c => {
                        var nodesTask = _metadataManager.GetChunkStorageNodesAsync(
                            c.FileId, c.ChunkId, cancellationToken);
                        nodesTask.Wait(cancellationToken);
                        return nodesTask.Result.Contains(nodeId);
                    })
                    .ToList();
                
                _logger.LogInformation("Found {Count} chunks affected by failure of node {NodeId}", 
                    chunksOnFailedNode.Count, nodeId);
                
                foreach (var chunk in chunksOnFailedNode)
                {
                    await _replicationManager.EnsureChunkReplicationAsync(
                        chunk.FileId, chunk.ChunkId, cancellationToken);
                    
                    _logger.LogDebug("Triggered replication check for Chunk {ChunkId}", chunk.ChunkId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring replication for chunks on failed node {NodeId}", nodeId);
            }
        }
        
    }
}