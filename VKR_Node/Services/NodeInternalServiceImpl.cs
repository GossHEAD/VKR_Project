using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Enums;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos;

namespace VKR_Node.Services;

// --- gRPC Service---
// Наследуются от сгенерированных баз и реализуют методы API
public class NodeInternalServiceImpl : NodeInternalService.NodeInternalServiceBase
{
    private readonly ILogger<NodeInternalServiceImpl> _logger;
    private readonly IMetadataManager _metadataManager;
    private readonly IDataManager _dataManager;
    private readonly INodeClient _nodeClient;
    private readonly NodeOptions _nodeOptions;

    public NodeInternalServiceImpl(
        ILogger<NodeInternalServiceImpl> logger,
        IMetadataManager metadataManager,
        IDataManager dataManager,
        INodeClient nodeClient,
        IOptions<NodeOptions> nodeOptions)
    {
        _logger = logger;
        _metadataManager = metadataManager;
        _dataManager = dataManager;
        _nodeClient = nodeClient;
        _nodeOptions = nodeOptions.Value;
    }

    public override async Task<ReplicateChunkReply> ReplicateChunk(ReplicateChunkRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "Node {NodeId} received ReplicateChunk request for Chunk {ChunkId} of File {FileId} from Node {OriginalNodeId}",
            _nodeOptions.NodeId, request.ChunkId, request.FileId, request.OriginalNodeId);

        // Validate request
        if (string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.ChunkId) || request.Data == null ||
            request.Data.IsEmpty)
        {
            _logger.LogWarning("Received invalid ReplicateChunk request: Missing required fields");
            return new ReplicateChunkReply { Success = false, Message = "Missing required fields" };
        }

        string localNodeId = _nodeOptions.NodeId ?? "Unknown";

        // Prepare ChunkInfoCore from the request
        var chunkInfo = new ChunkInfoCore
        {
            FileId = request.FileId,
            ChunkId = request.ChunkId,
            ChunkIndex = request.ChunkIndex,
            Size = request.Data.Length,
            StoredNodeId = localNodeId
        };

        try
        {
            // Store chunk data and update metadata
            bool success = await StoreChunkAndMetadataAsync(chunkInfo, request.Data, context.CancellationToken);
            if (!success)
            {
                return new ReplicateChunkReply { Success = false, Message = "Failed to store chunk data or metadata" };
            }

            // Process parent file metadata if provided
            if (request.ParentFileMetadata != null)
            {
                await ProcessParentFileMetadataAsync(request.FileId, request.ParentFileMetadata,
                    context.CancellationToken);
            }

            // Send acknowledgement to original sender if needed
            if (!string.IsNullOrEmpty(request.OriginalNodeId) && request.OriginalNodeId != localNodeId)
            {
                // Fire-and-forget acknowledgement task
                _ = SendReplicaAcknowledgementAsync(request.OriginalNodeId, request.FileId, request.ChunkId,
                    localNodeId);
            }

            return new ReplicateChunkReply { Success = true, Message = "Chunk replicated successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ReplicateChunk request for Chunk {ChunkId}", request.ChunkId);
            return new ReplicateChunkReply { Success = false, Message = $"Error: {ex.Message}" };
        }
    }

    private async Task<bool> StoreChunkAndMetadataAsync(ChunkInfoCore chunkInfo, ByteString data,
        CancellationToken cancellationToken)
    {
        bool storageSuccess = false;
        bool metadataSuccess = false;

        try
        {
            // 1. Store the chunk data locally
            await using (var dataStream = new MemoryStream(data.ToArray()))
            {
                await _dataManager.StoreChunkAsync(chunkInfo, dataStream, cancellationToken);
            }

            _logger.LogDebug("Chunk {ChunkId} data stored successfully", chunkInfo.ChunkId);
            storageSuccess = true;

            // 2. Store metadata about the chunk and its location (this node)
            await _metadataManager.SaveChunkMetadataAsync(
                chunkInfo,
                new[] { chunkInfo.StoredNodeId },
                cancellationToken);
            _logger.LogDebug("Chunk {ChunkId} metadata saved successfully", chunkInfo.ChunkId);
            metadataSuccess = true;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing chunk {ChunkId} data or metadata", chunkInfo.ChunkId);

            // Clean up data if metadata failed
            if (storageSuccess && !metadataSuccess)
            {
                try
                {
                    await _dataManager.DeleteChunkAsync(chunkInfo, CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Error during cleanup after metadata failure for Chunk {ChunkId}",
                        chunkInfo.ChunkId);
                }
            }

            return false;
        }
    }

    private async Task ProcessParentFileMetadataAsync(string fileId, FileMetadata protoMetadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var existingMetadata = await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);
            bool shouldSaveMetadata = ShouldUpdateFileMetadata(existingMetadata, protoMetadata);

            if (shouldSaveMetadata)
            {
                var coreMetadata = MapProtoToFileMetadataCore(fileId, protoMetadata);
                await _metadataManager.SaveFileMetadataAsync(coreMetadata, cancellationToken);
                _logger.LogDebug("Saved/updated FileMetadata for {FileId} based on replica request", fileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process parent file metadata for File {FileId}", fileId);
        }
    }

    private async Task SendReplicaAcknowledgementAsync(string originalNodeId, string fileId, string chunkId,
        string replicaNodeId)
    {
        try
        {
            string? senderAddress = await GetNodeAddressAsync(originalNodeId);
            if (string.IsNullOrEmpty(senderAddress))
            {
                _logger.LogWarning(
                    "Could not find address for original sender Node {OriginalNodeId} to send AcknowledgeReplica",
                    originalNodeId);
                return;
            }

            var ackRequest = new AcknowledgeReplicaRequest
            {
                FileId = fileId,
                ChunkId = chunkId,
                ReplicaNodeId = replicaNodeId
            };

            _logger.LogInformation(
                "Sending AcknowledgeReplica for Chunk {ChunkId} to Node {OriginalNodeId} at {SenderAddress}",
                chunkId, originalNodeId, senderAddress);

            await _nodeClient.AcknowledgeReplicaAsync(senderAddress, ackRequest, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AcknowledgeReplica for Chunk {ChunkId} to Node {OriginalNodeId}",
                chunkId, originalNodeId);
        }
    }

    private async Task<string?> GetNodeAddressAsync(string nodeId)
    {
        try
        {
            var nodeStates = await _metadataManager.GetNodeStatesAsync
                (new[] { nodeId }, CancellationToken.None);
            var nodeState = nodeStates?.FirstOrDefault();
            return nodeState?.Address;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get address for Node {NodeId}", nodeId);
            return null;
        }
    }

    private bool ShouldUpdateFileMetadata(FileMetadataCore? existingMetadata, FileMetadata protoMetadata)
    {
        // Always save if no existing metadata
        if (existingMetadata == null)
        {
            _logger.LogInformation("No local metadata found for File. Will save received metadata.");
            return true;
        }

        // Update if local state is Incomplete
        if (existingMetadata.State == FileStateCore.Incomplete &&
            (FileStateCore)protoMetadata.State != FileStateCore.Incomplete)
        {
            _logger.LogInformation("Local metadata is Incomplete. Will update with received metadata.");
            return true;
        }

        // Optional: Update if incoming is newer
        // if (protoMetadata.ModificationTime?.ToDateTime() > existingMetadata.ModificationTime)
        // {
        //     _logger.LogInformation("Incoming metadata is newer. Will update local record.");
        //     return true;
        // }

        return false;
    }

    private FileMetadataCore MapProtoToFileMetadataCore(string fileId, FileMetadata proto)
    {
        return new FileMetadataCore
        {
            FileId = fileId,
            FileName = proto.FileName,
            FileSize = proto.FileSize,
            CreationTime = proto.CreationTime?.ToDateTime() ?? DateTime.UtcNow,
            ModificationTime = proto.ModificationTime?.ToDateTime() ?? DateTime.UtcNow,
            ContentType = proto.ContentType,
            ChunkSize = proto.ChunkSize,
            TotalChunks = proto.TotalChunks,
            State = (FileStateCore)proto.State
        };
    }

    /// <summary>
    /// Handles requests to delete a specific chunk replica.
    /// </summary>
    public override async Task<DeleteChunkReply> DeleteChunk(DeleteChunkRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.ChunkId))
        {
            _logger.LogWarning("Received invalid DeleteChunk request: Missing file or chunk ID");
            return new DeleteChunkReply { Success = false, Message = "File ID and Chunk ID are required" };
        }

        _logger.LogInformation("Node {NodeId} received DeleteChunk request for Chunk {ChunkId} of File {FileId}",
            _nodeOptions.NodeId, request.ChunkId, request.FileId);

        try
        {
            var result = await DeleteLocalChunkAsync(request.FileId, request.ChunkId, context.CancellationToken);
            return new DeleteChunkReply { Success = result.Success, Message = result.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing DeleteChunk request for Chunk {ChunkId}", request.ChunkId);
            return new DeleteChunkReply
            {
                Success = false,
                Message = $"Error deleting chunk: {ex.Message}"
            };
        }
    }

    private async Task<(bool Success, string Message)> DeleteLocalChunkAsync(
        string fileId, string chunkId, CancellationToken cancellationToken)
    {
        var chunkInfo = new ChunkInfoCore
        {
            FileId = fileId,
            ChunkId = chunkId,
            StoredNodeId = _nodeOptions.NodeId
        };

        bool metadataRemoved = false;
        bool dataRemoved = false;

        // 1. Remove chunk location from metadata
        try
        {
            metadataRemoved = await _metadataManager.RemoveChunkStorageNodeAsync(
                fileId, chunkId, _nodeOptions.NodeId, cancellationToken);

            if (!metadataRemoved)
            {
                _logger.LogWarning("Failed to remove metadata location for Chunk {ChunkId} (or it was already gone)",
                    chunkId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing chunk metadata for Chunk {ChunkId}", chunkId);
            // Continue to attempt data removal anyway
        }

        // 2. Delete the actual chunk data file
        try
        {
            dataRemoved = await _dataManager.DeleteChunkAsync(chunkInfo, cancellationToken);

            if (!dataRemoved)
            {
                _logger.LogWarning("Failed to delete chunk data file for Chunk {ChunkId} (or it was already gone)",
                    chunkId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting chunk data for Chunk {ChunkId}", chunkId);
        }

        // Consider success if EITHER metadata or data was successfully removed
        bool overallSuccess = metadataRemoved || dataRemoved;
        string message = GetDeleteResultMessage(metadataRemoved, dataRemoved);

        return (overallSuccess, message);
    }

    private string GetDeleteResultMessage(bool metadataRemoved, bool dataRemoved)
    {
        if (metadataRemoved && dataRemoved)
            return "Chunk metadata and data successfully deleted";
        if (metadataRemoved)
            return "Chunk metadata removed but data deletion failed";
        if (dataRemoved)
            return "Chunk data deleted but metadata removal failed";
        return "Failed to delete chunk metadata or data (may not exist)";
    }

    /// <summary>
    /// Handles Ping requests from other nodes. Simply acknowledges the ping.
    /// </summary>
    /// <summary>
    /// Handles Ping requests from other nodes. Simply acknowledges the ping.
    /// </summary>
    public override Task<PingReply> Ping(PingRequest request, ServerCallContext context)
    {
        _logger.LogDebug("Node {NodeId} received Ping request from Node {SenderNodeId}.", _nodeOptions.NodeId,
            request.SenderNodeId ?? "Unknown");
        var reply = new PingReply
        {
            ResponderNodeId = _nodeOptions.NodeId,
            Success = true
        };
        return Task.FromResult(reply);
    }

    /// <summary>
    /// Handles requests from other nodes to stream a specific chunk's data.
    /// </summary>
    public override async Task RequestChunk(
        RequestChunkRequest request,
        IServerStreamWriter<RequestChunkReply> responseStream,
        ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.ChunkId))
        {
            _logger.LogWarning("Received invalid RequestChunk request: Missing file or chunk ID");
            throw new RpcException(new Status(StatusCode.InvalidArgument, "File ID and Chunk ID are required"));
        }

        _logger.LogInformation(
            "Node {NodeId} received RequestChunk for Chunk {ChunkId} of File {FileId} from peer {Peer}",
            _nodeOptions.NodeId, request.ChunkId, request.FileId, context.Peer);

        Stream? chunkDataStream = null;
        try
        {
            chunkDataStream =
                await GetLocalChunkDataStreamAsync(request.FileId, request.ChunkId, context.CancellationToken);

            if (chunkDataStream == null)
            {
                _logger.LogWarning("Chunk {ChunkId} not found locally on Node {NodeId}", request.ChunkId,
                    _nodeOptions.NodeId);
                throw new RpcException(new Status(StatusCode.NotFound, "Chunk not found locally"));
            }

            await StreamChunkDataAsync(chunkDataStream, responseStream, context.CancellationToken);

            _logger.LogInformation("Finished streaming chunk {ChunkId} to peer {Peer}", request.ChunkId, context.Peer);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming chunk {ChunkId} to {Peer} was cancelled", request.ChunkId, context.Peer);
            throw new RpcException(new Status(StatusCode.Cancelled, "Operation was cancelled"));
        }
        catch (RpcException)
        {
            // Re-throw RPC exceptions without wrapping
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chunk {ChunkId} to peer {Peer}", request.ChunkId, context.Peer);
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to stream chunk: {ex.Message}"));
        }
        finally
        {
            if (chunkDataStream != null)
            {
                await chunkDataStream.DisposeAsync();
            }
        }
    }

    private async Task<Stream?> GetLocalChunkDataStreamAsync(string fileId, string chunkId,
        CancellationToken cancellationToken)
    {
        try
        {
            var chunkInfo = new ChunkInfoCore
            {
                FileId = fileId,
                ChunkId = chunkId,
                StoredNodeId = _nodeOptions.NodeId
            };

            return await _dataManager.RetrieveChunkAsync(chunkInfo, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving local data for Chunk {ChunkId}", chunkId);
            return null;
        }
    }

    private async Task StreamChunkDataAsync(
        Stream dataStream,
        IServerStreamWriter<RequestChunkReply> responseStream,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Streaming chunk data, total size: {Size} bytes", dataStream.Length);

        const int bufferSize = 65536; // 64KB chunks for streaming
        byte[] buffer = new byte[bufferSize];
        int bytesRead;

        while ((bytesRead = await dataStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await responseStream.WriteAsync(new RequestChunkReply
            {
                Data = ByteString.CopyFrom(buffer, 0, bytesRead)
            }, cancellationToken);
        }
    }

    public override Task<FindSuccessorReply> FindSuccessor(FindSuccessorRequest request, ServerCallContext context)
    {
        throw new RpcException(Status.DefaultCancelled);
    }

    public override Task<GetPredecessorReply> GetPredecessor(GetPredecessorRequest request, ServerCallContext context)
    {
        throw new RpcException(Status.DefaultCancelled);
    }

    public override Task<NotifyReply> Notify(NotifyRequest request, ServerCallContext context)
    {
        throw new RpcException(Status.DefaultCancelled);
    }

    /// <summary>
    /// Handles requests from other nodes to get the list of files known locally.
    /// </summary>
    public override async Task<GetNodeFileListReply> GetNodeFileList(
        GetNodeFileListRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Node {NodeId} received GetNodeFileList request from peer {Peer}",
            _nodeOptions.NodeId, context.Peer);
    
        var reply = new GetNodeFileListReply();

        try
        {
            var localFiles = await _metadataManager.ListFilesAsync(context.CancellationToken);

            if (localFiles != null)
            {
                foreach (var fileCore in localFiles)
                {
                    // Skip files marked for deletion
                    if (fileCore.State == FileStateCore.Deleting || fileCore.State == FileStateCore.Deleted)
                        continue;
                
                    var fileProto = MapFileMetadataCoreToProto(fileCore);
                    reply.Files.Add(fileProto);
                }
            }
        
            _logger.LogInformation("Returning {Count} file metadata entries to peer {Peer}",
                reply.Files.Count, context.Peer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GetNodeFileList request for peer {Peer}", context.Peer);
            throw new RpcException(new Status(StatusCode.Internal, 
                $"Failed to retrieve local file list: {ex.Message}"));
        }
    
        return reply;
    }

    private FileMetadata MapFileMetadataCoreToProto(FileMetadataCore fileCore)
    {
        return new FileMetadata
        {
            FileId = fileCore.FileId,
            FileName = fileCore.FileName,
            FileSize = fileCore.FileSize,
            CreationTime = Timestamp.FromDateTime(
                fileCore.CreationTime.ToUniversalTime()),
            ModificationTime = Timestamp.FromDateTime(
                fileCore.ModificationTime.ToUniversalTime()),
            ContentType = fileCore.ContentType ?? "",
            ChunkSize = fileCore.ChunkSize,
            TotalChunks = fileCore.TotalChunks,
            State = (FileState)fileCore.State
        };
    }

    /// <summary>
    /// Handles incoming acknowledgements from nodes that have successfully stored a replica.
    /// Updates the local metadata manager with the new chunk location.
    /// </summary>
    public override async Task<Empty> AcknowledgeReplica(AcknowledgeReplicaRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.FileId) ||
            string.IsNullOrEmpty(request.ChunkId) ||
            string.IsNullOrEmpty(request.ReplicaNodeId))
        {
            _logger.LogWarning("Received invalid AcknowledgeReplica request with missing IDs from peer {Peer}",
                context.Peer);
            // Empty response per API contract, can't signal error except via logs
            return new Empty();
        }

        string localNodeId = _nodeOptions.NodeId ?? "Unknown";

        _logger.LogInformation("Node {LocalNodeId} received AcknowledgeReplica for Chunk {ChunkId} (File {FileId}) " +
                               "successfully stored on Node {ReplicaNodeId}",
            localNodeId, request.ChunkId, request.FileId, request.ReplicaNodeId);

        // Don't add self based on an ACK from elsewhere
        if (request.ReplicaNodeId == localNodeId)
        {
            _logger.LogDebug("Ignoring AcknowledgeReplica for local node (location should already exist)");
            return new Empty();
        }

        try
        {
            await _metadataManager.AddChunkStorageNodeAsync(
                request.FileId, request.ChunkId, request.ReplicaNodeId, context.CancellationToken);

            _logger.LogInformation("Successfully added Node {ReplicaNodeId} as location for Chunk {ChunkId}",
                request.ReplicaNodeId, request.ChunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AcknowledgeReplica for Chunk {ChunkId} from Node {ReplicaNodeId}",
                request.ChunkId, request.ReplicaNodeId);
            // Cannot propagate error with Empty reply, but logged
        }

        return new Empty();
    }

}