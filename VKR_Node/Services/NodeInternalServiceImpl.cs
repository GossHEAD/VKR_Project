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
        
        public override async Task<ReplicateChunkReply> ReplicateChunk(ReplicateChunkRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Node {NodeId} received ReplicateChunk request for Chunk {ChunkId} of File {FileId} from Node {OriginalNodeId}.",
                _nodeOptions.NodeId, request.ChunkId, request.FileId, request.OriginalNodeId);

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
            
            bool storageSuccess = false;
            bool metadataSuccess = false;

            try
            {
                // 1. Store the chunk data locally
                await using (var dataStream = new MemoryStream(request.Data.ToByteArray()))
                {
                    await _dataManager.StoreChunkAsync(chunkInfo, dataStream, context.CancellationToken);
                }
                _logger.LogDebug("Chunk {ChunkId} data stored successfully by Node {LocalNodeId}.", request.ChunkId, localNodeId);
                storageSuccess = true;

                // 1.5 Process Parent File Metadata from Request (if provided)
                if (request.ParentFileMetadata != null)
                {
                    _logger.LogDebug("Processing ParentFileMetadata received with Chunk {ChunkId}", request.ChunkId);
                    var incomingMetadataCore = new FileMetadataCore // Map proto to core model
                    {
                        FileId = request.FileId, // Must match FileId from chunk info
                        FileName = request.ParentFileMetadata.FileName,
                        FileSize = request.ParentFileMetadata.FileSize,
                        CreationTime = request.ParentFileMetadata.CreationTime?.ToDateTime() ?? DateTime.MinValue,
                        ModificationTime = request.ParentFileMetadata.ModificationTime?.ToDateTime() ?? DateTime.UtcNow,
                        ContentType = request.ParentFileMetadata.ContentType,
                        ChunkSize = request.ParentFileMetadata.ChunkSize,
                        TotalChunks = request.ParentFileMetadata.TotalChunks,
                        State = (FileStateCore)request.ParentFileMetadata.State // Cast proto enum
                    };

                    // Check if we need to save/update this metadata locally
                    var existingMetadata = await _metadataManager.GetFileMetadataAsync(request.FileId, context.CancellationToken);
                    bool shouldSaveMetadata = false;
                    if (existingMetadata == null)
                    {
                        _logger.LogInformation("No local metadata found for File {FileId}. Saving metadata received from replica request.", request.FileId);
                        shouldSaveMetadata = true;
                    }
                    // Optional: Update if incoming is newer (be careful with potential loops/stale data)
                    // else if (incomingMetadataCore.ModificationTime > existingMetadata.ModificationTime)
                    // {
                    //     _logger.LogInformation("Incoming metadata for File {FileId} is newer. Updating local record.", request.FileId);
                    //     shouldSaveMetadata = true;
                    // }
                    // Optional: Update if local state is Incomplete
                    else if (existingMetadata.State == FileStateCore.Incomplete && incomingMetadataCore.State != FileStateCore.Incomplete)
                    {
                         _logger.LogInformation("Local metadata for File {FileId} is Incomplete. Updating with full metadata.", request.FileId);
                         shouldSaveMetadata = true;
                    }

                    if (shouldSaveMetadata)
                    {
                        try
                        {
                            await _metadataManager.SaveFileMetadataAsync(incomingMetadataCore, context.CancellationToken);
                             _logger.LogDebug("Saved/Updated FileMetadata for {FileId} based on replica request.", request.FileId);
                        }
                        catch (Exception metaEx)
                        {
                            _logger.LogError(metaEx, "Failed to save parent file metadata received with chunk replica for File {FileId}", request.FileId);
                            // Decide if replication should fail if metadata can't be saved. Maybe not?
                        }
                    }
                } else {
                     _logger.LogWarning("ReplicateChunkRequest for Chunk {ChunkId} did not contain ParentFileMetadata.", request.ChunkId);
                }
                
                
                // 2. Store metadata about the chunk and its location (this node)
                var initialNodes = new List<string> { localNodeId };
                await _metadataManager.SaveChunkMetadataAsync(chunkInfo, initialNodes, context.CancellationToken);
                _logger.LogDebug("Chunk {ChunkId} metadata and local location stored successfully by Node {LocalNodeId}.", request.ChunkId, localNodeId);
                metadataSuccess = true;

                // --- Send ACK back to Original Sender ---
                if (!string.IsNullOrEmpty(request.OriginalNodeId) && request.OriginalNodeId != localNodeId)
                {
                    // Don't ACK yourself, only ACK the original sender
                     _logger.LogDebug("Attempting to send AcknowledgeReplica back to original sender: {OriginalNodeId}", request.OriginalNodeId);

                    // Fire-and-forget the ACK task
                    _ = Task.Run(async () =>
                    {
                        string? senderAddress = null;
                        try
                        {
                            var senderState = (await _metadataManager.GetNodeStatesAsync(new[] { request.OriginalNodeId }, CancellationToken.None)).FirstOrDefault();
                            senderAddress = senderState?.Address;

                            if (!string.IsNullOrEmpty(senderAddress))
                            {
                                var ackRequest = new AcknowledgeReplicaRequest // Use generated proto type
                                {
                                    FileId = request.FileId,
                                    ChunkId = request.ChunkId,
                                    ReplicaNodeId = localNodeId 
                                };

                                 _logger.LogInformation("Sending AcknowledgeReplica for Chunk {ChunkId} to Node {OriginalNodeId} at {SenderAddress}",
                                     request.ChunkId, request.OriginalNodeId, senderAddress);

                               
                                 // // ** REQUIRES INodeClient CHANGE: Add AcknowledgeReplicaAsync method **
                                 // // await _nodeClient.AcknowledgeReplicaAsync(senderAddress, ackRequest); // Make the call
                                 // _logger.LogDebug("Placeholder: Would call _nodeClient.AcknowledgeReplicaAsync here.");
                                 
                                 await _nodeClient.AcknowledgeReplicaAsync(senderAddress, ackRequest, CancellationToken.None); // Make the call using the injected client

                            }
                            else
                            {
                                _logger.LogWarning("Could not find address for original sender Node {OriginalNodeId} to send AcknowledgeReplica for Chunk {ChunkId}.",
                                                   request.OriginalNodeId, request.ChunkId);
                            }
                        }
                        catch (Exception ackEx)
                        {
                            _logger.LogError(ackEx, "Failed to send AcknowledgeReplica for Chunk {ChunkId} to Node {OriginalNodeId} at {SenderAddress}",
                                             request.ChunkId, request.OriginalNodeId, senderAddress ?? "Unknown Address");
                        }
                    }); // End Task.Run
                }
                // --- End Send ACK ---

                return new ReplicateChunkReply { Success = true, Message = "Chunk replicated and ACK sent (or attempted)." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error replicating chunk {ChunkId} for file {FileId} on Node {LocalNodeId}.", request.ChunkId, request.FileId, localNodeId);
                if (storageSuccess && !metadataSuccess)
                {
                    _logger.LogWarning("Attempting cleanup of stored chunk data due to metadata save failure for Chunk {ChunkId}.", chunkInfo.ChunkId);
                    try { await _dataManager.DeleteChunkAsync(chunkInfo, CancellationToken.None); } catch { /* Ignore cleanup error */ }
                }
                return new ReplicateChunkReply { Success = false, Message = $"Failed to replicate chunk: {ex.Message}" };
            }
        }
        
        /// <summary>
        /// Handles requests to delete a specific chunk replica.
        /// </summary>
        public override async Task<DeleteChunkReply> DeleteChunk(DeleteChunkRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Node {NodeId} received DeleteChunk request for Chunk {ChunkId} of File {FileId}.", _nodeOptions.NodeId, request.ChunkId, request.FileId);

            var chunkInfo = new ChunkInfoCore
            { FileId = request.FileId, ChunkId = request.ChunkId, StoredNodeId = _nodeOptions.NodeId };

            bool metadataRemoved = false;
            bool dataRemoved = false;
            string failureReason = "";

            try
            {
                // 1. Delete chunk metadata location
                metadataRemoved = await _metadataManager.RemoveChunkStorageNodeAsync(request.FileId, request.ChunkId, _nodeOptions.NodeId, context.CancellationToken);
                if (!metadataRemoved)
                {
                    // Logged inside RemoveChunkStorageNodeAsync, but we can add context
                    _logger.LogWarning("Failed to remove metadata location for Chunk {ChunkId} on Node {NodeId} (or it was already gone).", request.ChunkId, _nodeOptions.NodeId);
                    // Continue to delete data anyway
                }

                // 2. Delete the actual chunk data file
                dataRemoved = await _dataManager.DeleteChunkAsync(chunkInfo, context.CancellationToken);
                if (!dataRemoved)
                {
                     _logger.LogWarning("Failed to delete chunk data file for Chunk {ChunkId} on Node {NodeId} (or it was already gone).", request.ChunkId, _nodeOptions.NodeId);
                     // If metadata WAS removed but data failed, that's potentially an issue.
                     if (metadataRemoved) failureReason = "Data file could not be deleted after metadata removal.";
                     else failureReason = "Neither metadata nor data file were deleted/found.";
                }

                // Consider success if BOTH steps reported success (incl. "already gone")
                bool overallSuccess = metadataRemoved && dataRemoved;

                return new DeleteChunkReply
                {
                    Success = overallSuccess,
                    Message = overallSuccess ? "Chunk delete processed." : $"Chunk delete partially failed: {failureReason}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chunk {ChunkId} for file {FileId} on Node {NodeId}.", request.ChunkId, request.FileId, _nodeOptions.NodeId);
                return new DeleteChunkReply { Success = false, Message = $"Failed to delete chunk due to exception: {ex.Message}" };
            }
        }

        /// <summary>
        /// Handles Ping requests from other nodes. Simply acknowledges the ping.
        /// </summary>
        /// <summary>
        /// Handles Ping requests from other nodes. Simply acknowledges the ping.
        /// </summary>
        public override Task<PingReply> Ping(PingRequest request, ServerCallContext context)
        {
            _logger.LogDebug("Node {NodeId} received Ping request from Node {SenderNodeId}.", _nodeOptions.NodeId, request.SenderNodeId ?? "Unknown");
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
        public async Task RequestChunk(RequestChunkRequest request,
            IServerStreamWriter<RequestChunkReply> responseStream, ServerCallContext context)
        {
            _logger.LogInformation(
                "Node {NodeId} received RequestChunk for Chunk {ChunkId} of File {FileId} from peer {Peer}",
                _nodeOptions.NodeId, request.ChunkId, request.FileId, context.Peer);
            
            var chunkInfo = new ChunkInfoCore
            {
                FileId = request.FileId,
                ChunkId = request.ChunkId,
                StoredNodeId = _nodeOptions.NodeId // Data should be local on this node
                // Index, Size, Hash are not strictly needed for retrieval by path
            };

            Stream? chunkDataStream = null;
            try
            {
                chunkDataStream = await _dataManager.RetrieveChunkAsync(chunkInfo, context.CancellationToken);

                if (chunkDataStream == null)
                {
                    _logger.LogWarning("Chunk {ChunkId} not found locally on Node {NodeId}.", request.ChunkId,
                        _nodeOptions.NodeId);
                    // Optionally: throw new RpcException(new Status(StatusCode.NotFound, "Chunk not found locally"));
                    // Or just complete the stream without sending anything.
                    return; // Nothing to stream
                }

                _logger.LogDebug("Streaming chunk {ChunkId} data to peer {Peer}...", request.ChunkId, context.Peer);
                
                byte[] buffer = new byte[65536]; 
                int bytesRead;
                while ((bytesRead =
                           await chunkDataStream.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0)
                {
                    await responseStream.WriteAsync(new RequestChunkReply
                    {
                        Data = ByteString.CopyFrom(buffer, 0, bytesRead)
                    });
                }

                _logger.LogInformation("Finished streaming chunk {ChunkId} to peer {Peer}.", request.ChunkId,
                    context.Peer);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Streaming chunk {ChunkId} to {Peer} was cancelled.", request.ChunkId,
                    context.Peer);
            }
            catch (RpcException)
            {
                // If we were throwing specific RpcExceptions, re-throw them
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming chunk {ChunkId} to peer {Peer}.", request.ChunkId, context.Peer);
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

        public override Task<FindSuccessorReply> FindSuccessor(FindSuccessorRequest request, ServerCallContext context) { throw new RpcException(Status.DefaultCancelled); }
        public override Task<GetPredecessorReply> GetPredecessor(GetPredecessorRequest request, ServerCallContext context) { throw new RpcException(Status.DefaultCancelled); }
        public override Task<NotifyReply> Notify(NotifyRequest request, ServerCallContext context) { throw new RpcException(Status.DefaultCancelled); }

        /// <summary>
        /// Handles requests from other nodes to get the list of files known locally.
        /// </summary>
        public override async Task<GetNodeFileListReply> GetNodeFileList(
            GetNodeFileListRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Node {NodeId} received GetNodeFileList request from peer {Peer}", _nodeOptions.NodeId, context.Peer);
            var reply = new GetNodeFileListReply();

            try
            {
                var localFiles = await _metadataManager.ListFilesAsync(context.CancellationToken);

                if (localFiles != null)
                {
                    _logger.LogDebug("Found {Count} local file metadata entries.", localFiles.Count());
                    foreach (var fileCore in localFiles)
                    {
                        // Map the Core model to the Protobuf message
                        // Ensure this mapping matches the FileMetadata definition used by StorageService.ListFiles
                        // and defined in your GetNodeFileListReply proto message.
                        var fileProto = new VKR.Protos.FileMetadata // Use the correct proto message type
                        {
                            FileId = fileCore.FileId,
                            FileName = fileCore.FileName,
                            FileSize = fileCore.FileSize,
                            CreationTime = Timestamp.FromDateTime(fileCore.CreationTime.ToUniversalTime()),
                            ModificationTime = Timestamp.FromDateTime(fileCore.ModificationTime.ToUniversalTime()),
                            ContentType = fileCore.ContentType ?? "",
                            ChunkSize = fileCore.ChunkSize,
                            TotalChunks = fileCore.TotalChunks,
                            State = (VKR.Protos.FileState)fileCore.State // Cast to the proto enum type
                        };
                        reply.Files.Add(fileProto);
                    }
                }
                _logger.LogInformation("Returning {Count} file metadata entries to peer {Peer}.", reply.Files.Count, context.Peer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing GetNodeFileList request for peer {Peer}", context.Peer);
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to retrieve local file list: {ex.Message}"));
            }
            return reply;
        }
        
        /// <summary>
        /// Handles incoming acknowledgements from nodes that have successfully stored a replica.
        /// Updates the local metadata manager with the new chunk location.
        /// </summary>
        public override async Task<Empty> AcknowledgeReplica(AcknowledgeReplicaRequest request, ServerCallContext context)
        {
            string localNodeId = _nodeOptions.NodeId ?? "Unknown";
            _logger.LogInformation("Node {LocalNodeId} received AcknowledgeReplica for Chunk {ChunkId} (File {FileId}) successfully stored on Node {ReplicaNodeId}",
                localNodeId, request.ChunkId, request.FileId, request.ReplicaNodeId);

            // Validate request
            if (string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.ChunkId) || string.IsNullOrEmpty(request.ReplicaNodeId))
            {
                _logger.LogWarning("Received invalid AcknowledgeReplica request with missing IDs from peer {Peer}. FileId={FileId}, ChunkId={ChunkId}, ReplicaNodeId={ReplicaNodeId}",
                    context.Peer, request.FileId, request.ChunkId, request.ReplicaNodeId);
                // Cannot return specific error easily with Empty reply, rely on logs.
                return new Empty(); // Or throw RpcException(Status.InvalidArgument)? Decide error handling.
            }

            // Don't add self based on an ACK from elsewhere (should only be added during initial local save)
            if (request.ReplicaNodeId == localNodeId)
            {
                _logger.LogDebug("Received AcknowledgeReplica for local node {LocalNodeId}. Ignoring as location should already exist.", localNodeId);
                return new Empty();
            }

            try
            {
                // Call the Metadata Manager to add the node storing the replica
                // This method should be idempotent (handle cases where the location already exists)
                await _metadataManager.AddChunkStorageNodeAsync(request.FileId, request.ChunkId, request.ReplicaNodeId, context.CancellationToken);

                _logger.LogInformation("Successfully processed AcknowledgeReplica: Added Node {ReplicaNodeId} as location for Chunk {ChunkId} (File {FileId})",
                    request.ReplicaNodeId, request.ChunkId, request.FileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AcknowledgeReplica for Chunk {ChunkId} from Node {ReplicaNodeId}: Failed to update metadata.",
                    request.ChunkId, request.ReplicaNodeId);
                // Decide if this failure should be propagated back to the caller.
                // With Empty reply, throwing an RpcException is the primary way.
                // throw new RpcException(new Status(StatusCode.Internal, "Failed to update metadata based on acknowledgement."));
            }

            return new Empty(); // Return empty reply on success
        }
        
}