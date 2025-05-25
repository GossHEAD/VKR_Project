using System.Collections.Concurrent;
using System.Diagnostics;
using AutoMapper;
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

public class NodeInternalServiceImpl : NodeInternalService.NodeInternalServiceBase
{
    private readonly ILogger<NodeInternalServiceImpl> _logger;
    private readonly IMetadataManager _metadataManager;
    private readonly IDataManager _dataManager;
    private readonly INodeClient _nodeClient;
    private readonly NodeIdentityOptions _nodeOptions;
    private readonly IMapper _mapper;
    private readonly IReplicationManager _replicationManager;

    private readonly ConcurrentDictionary<string, (long Count, long TotalMs)> _methodMetrics = new();
    public NodeInternalServiceImpl(
        ILogger<NodeInternalServiceImpl> logger,
        IMetadataManager metadataManager,
        IDataManager dataManager,
        INodeClient nodeClient,
        IOptions<NodeIdentityOptions> nodeOptions,
        IMapper mapper,
        IReplicationManager replicationManager)
    {
        _logger = logger;
        _metadataManager = metadataManager;
        _dataManager = dataManager;
        _nodeClient = nodeClient;
        _nodeOptions = nodeOptions.Value ?? 
                       throw new ArgumentNullException(nameof(nodeOptions));
        _mapper = mapper;
        _replicationManager = replicationManager;
    }
   
    private void RecordMetric(string methodName, long elapsedMs)
    {
        _methodMetrics.AddOrUpdate(
            methodName,
            (1, elapsedMs),
            (_, current) => (current.Count + 1, current.TotalMs + elapsedMs)
        );
    }

    private void LogMetrics()
    {
        foreach (var kvp in _methodMetrics.ToArray())
        {
            string method = kvp.Key;
            var (count, totalMs) = kvp.Value;
        
            if (count > 0)
            {
                _logger.LogInformation(
                    "Method: {Method}, Calls: {Count}, AvgTime: {AvgTime:F2}ms", 
                    method, count, (double)totalMs / count);
            }
        }
    }

    public override async Task<ReplicateChunkReply> ReplicateChunk(ReplicateChunkRequest request,
        ServerCallContext context)
    {
        var sw = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "Node {NodeId} received ReplicateChunk request for Chunk {ChunkId} of File {FileId} from Node {OriginalNodeId}",
            _nodeOptions.NodeId, request.ChunkId, request.FileId, request.OriginalNodeId);

        if (string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.ChunkId) || request.Data == null ||
            request.Data.IsEmpty)
        {
            _logger.LogWarning("Received invalid ReplicateChunk request: Missing required fields");
            return new ReplicateChunkReply { Success = false, Message = "Missing required fields" };
        }

        string localNodeId = _nodeOptions.NodeId ?? "Unknown";

        var chunkInfo = new ChunkModel
        {
            FileId = request.FileId,
            ChunkId = request.ChunkId,
            ChunkIndex = request.ChunkIndex,
            Size = request.Data.Length,
            StoredNodeId = localNodeId
        };

        try
        {
            long availableSpace = await _dataManager.GetFreeDiskSpaceAsync(context.CancellationToken);
            long requiredSpace = (long)(request.Data.Length * 1.1); 
            
            if (availableSpace < requiredSpace)
            {
                string errorMsg = $"Insufficient disk space. Required: {FormatBytes(requiredSpace)}, Available: {FormatBytes(availableSpace)}";
                _logger.LogError("Cannot store chunk {ChunkId}: {ErrorMessage}", request.ChunkId, errorMsg);
                return new ReplicateChunkReply { Success = false, Message = errorMsg };
            }

            bool success = await StoreChunkAndMetadataAsync(chunkInfo, request.Data, context.CancellationToken);
            if (!success)
            {
                return new ReplicateChunkReply { Success = false, Message = "Failed to store chunk data or metadata" };
            }
            
            if (success)
            {
                _ = Task.Run(async () => {
                    try
                    {
                        await Task.Delay(1000);
                    
                        await _replicationManager.EnsureChunkReplicationAsync(
                            request.FileId, request.ChunkId, CancellationToken.None);
                        
                        _logger.LogInformation("Secondary replication triggered for Chunk {ChunkId}",
                            request.ChunkId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during secondary replication for Chunk {ChunkId}",
                            request.ChunkId);
                    }
                });
            }

            if (request.ParentFileMetadata != null)
            {
                await ProcessParentFileMetadataAsync(request.FileId, request.ParentFileMetadata,
                    context.CancellationToken);
            }

            if (!string.IsNullOrEmpty(request.OriginalNodeId) && request.OriginalNodeId != localNodeId)
            {
                _ = SendReplicaAcknowledgementAsync(request.OriginalNodeId, request.FileId, request.ChunkId, localNodeId);
            }

            sw.Stop();
            _logger.LogInformation("Successfully replicated Chunk {ChunkId} in {ElapsedMs}ms", 
                request.ChunkId, sw.ElapsedMilliseconds);
                
            return new ReplicateChunkReply { Success = true, Message = "Chunk replicated successfully" };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error processing ReplicateChunk request for Chunk {ChunkId}", request.ChunkId);
            return new ReplicateChunkReply { Success = false, Message = $"Error: {ex.Message}" };
        }
    }
    
    public override async Task<ReplicateChunkReply> ReplicateChunkStreaming(
        IAsyncStreamReader<ReplicateChunkStreamingRequest> requestStream,
        ServerCallContext context)
    {
        var sw = Stopwatch.StartNew();
        ReplicateChunkMetadata? metadata = null;
        ChunkModel? chunkInfo = null;
        string tempFilePath = Path.GetTempFileName();
        long totalBytesReceived = 0;
        string localNodeId = _nodeOptions.NodeId;
        
        try
        {
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    if (request.PayloadCase == ReplicateChunkStreamingRequest.PayloadOneofCase.Metadata)
                    {
                        metadata = request.Metadata;
                        
                        _logger.LogInformation(
                            "Node {NodeId} received streaming ReplicateChunk request for Chunk {ChunkId} of File {FileId} from Node {OriginalNodeId}",
                            _nodeOptions.NodeId, metadata.ChunkId, metadata.FileId, metadata.OriginalNodeId);
                            
                        chunkInfo = new ChunkModel
                        {
                            FileId = metadata.FileId,
                            ChunkId = metadata.ChunkId,
                            ChunkIndex = metadata.ChunkIndex,
                            Size = metadata.Size,
                            StoredNodeId = localNodeId 
                        };
                    }
                    else if (request.PayloadCase == ReplicateChunkStreamingRequest.PayloadOneofCase.DataChunk)
                    {
                        if (metadata == null || chunkInfo == null)
                        {
                            throw new RpcException(new Status(StatusCode.InvalidArgument, "Received data before metadata"));
                        }
                        
                        var data = request.DataChunk;
                        await fileStream.WriteAsync(data.Memory, context.CancellationToken);
                        totalBytesReceived += data.Length;
                    }
                }
            }
            
            if (metadata == null || chunkInfo == null)
            {
                File.Delete(tempFilePath);
                return new ReplicateChunkReply { Success = false, Message = "No metadata received" };
            }
            
            if (metadata.Size > 0 && totalBytesReceived != metadata.Size)
            {
                _logger.LogWarning("Size mismatch for Chunk {ChunkId}. Expected: {Expected}, Received: {Received}",
                    metadata.ChunkId, metadata.Size, totalBytesReceived);
            }
            
            chunkInfo.Size = totalBytesReceived;
            
            long availableSpace = await _dataManager.GetFreeDiskSpaceAsync(context.CancellationToken);
            long requiredSpace = (long)(totalBytesReceived * 1.1);
            
            if (availableSpace < requiredSpace)
            {
                File.Delete(tempFilePath);
                return new ReplicateChunkReply { 
                    Success = false, 
                    Message = $"Insufficient disk space. Required: {FormatBytes(requiredSpace)}, Available: {FormatBytes(availableSpace)}" 
                };
            }
            
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
            {
                await _dataManager.StoreChunkAsync(chunkInfo, fileStream, context.CancellationToken);
            }
            
            File.Delete(tempFilePath);
            
            await _metadataManager.SaveChunkMetadataAsync(
                chunkInfo,
                new[] { localNodeId  },
                context.CancellationToken);
            
            if (metadata.ParentFileMetadata != null)
            {
                await ProcessParentFileMetadataAsync(metadata.FileId, metadata.ParentFileMetadata, context.CancellationToken);
            }
            
            if (!string.IsNullOrEmpty(metadata.OriginalNodeId) && metadata.OriginalNodeId != localNodeId )
            {
                _ = SendReplicaAcknowledgementAsync(metadata.OriginalNodeId, metadata.FileId, metadata.ChunkId, localNodeId );
            }
            
            sw.Stop();
            _logger.LogInformation("Successfully processed streaming replica for Chunk {ChunkId} in {ElapsedMs}ms", 
                metadata.ChunkId, sw.ElapsedMilliseconds);
                
            return new ReplicateChunkReply { Success = true, Message = "Chunk replicated successfully" };
        }
        catch (Exception ex)
        {
            sw.Stop();
            
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
            
            _logger.LogError(ex, "Error processing streaming ReplicateChunk request for Chunk {ChunkId}", 
                metadata?.ChunkId ?? "unknown");
                
            return new ReplicateChunkReply { Success = false, Message = $"Error: {ex.Message}" };
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

    private async Task<bool> StoreChunkAndMetadataAsync(ChunkModel chunkInfo, ByteString data,
        CancellationToken cancellationToken)
    {
        bool storageSuccess = false;
        bool metadataSuccess = false;

        try
        {
            await using (var dataStream = new MemoryStream(data.ToArray()))
            {
                await _dataManager.StoreChunkAsync(chunkInfo, dataStream, cancellationToken);
            }

            _logger.LogDebug("Chunk {ChunkId} data stored successfully", chunkInfo.ChunkId);
            storageSuccess = true;

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
        if (string.IsNullOrEmpty(fileId) || protoMetadata == null)
        {
            _logger.LogWarning("Invalid file metadata provided: FileId or metadata is null");
            return;
        }
    
        try
        {
            if (string.IsNullOrEmpty(protoMetadata.FileName) || protoMetadata.FileSize <= 0)
            {
                _logger.LogWarning("Incomplete file metadata for {FileId}: Missing filename or size", fileId);
            }
        
            var existingMetadata = await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);
            bool shouldSaveMetadata = ShouldUpdateFileMetadata(existingMetadata, protoMetadata);

            if (shouldSaveMetadata)
            {
                var coreMetadata = _mapper.Map<FileModel>(protoMetadata);
                coreMetadata.FileId = fileId;
                await _metadataManager.SaveFileMetadataAsync(coreMetadata, cancellationToken);
                _logger.LogDebug("Saved/updated FileMetadata for {FileId} based on replica request", fileId);
            }
            else
            {
                _logger.LogDebug("No need to update FileMetadata for {FileId} (no changes or local version is newer)", fileId);
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

    private bool ShouldUpdateFileMetadata(FileModel? existingMetadata, FileMetadata protoMetadata)
    {
        if (existingMetadata == null)
        {
            _logger.LogInformation("No local metadata found for File. Will save received metadata.");
            return true;
        }

        if (existingMetadata.State == FileStateCore.Incomplete &&
            (FileStateCore)protoMetadata.State != FileStateCore.Incomplete)
        {
            _logger.LogInformation("Local metadata is Incomplete. Will update with received metadata.");
            return true;
        }

        if (protoMetadata.ModificationTime?.ToDateTime() > existingMetadata.ModificationTime)
        {
            _logger.LogInformation("Incoming metadata is newer. Will update local record.");
            return true;
        }

        return false;
    }

    public override async Task<DeleteChunkReply> DeleteChunk(DeleteChunkRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.FileId) || string.IsNullOrEmpty(request.ChunkId))
        {
            _logger.LogWarning("Received invalid DeleteChunk request: Missing file or chunk ID");
            return new DeleteChunkReply { Success = false, Message = "File ID and Chunk ID are required" };
        }

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Node {NodeId} received DeleteChunk request for Chunk {ChunkId} of File {FileId}",
            _nodeOptions.NodeId, request.ChunkId, request.FileId);

        try
        {
            var chunkInfo = new ChunkModel
            {
                FileId = request.FileId,
                ChunkId = request.ChunkId,
                StoredNodeId = _nodeOptions.NodeId
            };
            
            bool exists = await _dataManager.ChunkExistsAsync(chunkInfo, context.CancellationToken);
            
            if (!exists)
            {
                _logger.LogInformation("Chunk {ChunkId} not found locally, nothing to delete", request.ChunkId);
                return new DeleteChunkReply { Success = true, Message = "Chunk not found locally (no action required)" };
            }
            
            bool metadataRemoved = false;
            bool dataRemoved = false;

            try
            {
                metadataRemoved = await _metadataManager.RemoveChunkStorageNodeAsync(
                    request.FileId, request.ChunkId, _nodeOptions.NodeId, context.CancellationToken);

                if (!metadataRemoved)
                {
                    _logger.LogWarning("Failed to remove metadata location for Chunk {ChunkId} (or it was already gone)",
                        request.ChunkId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing chunk metadata for Chunk {ChunkId}", request.ChunkId);
                
            }

            
            try
            {
                dataRemoved = await _dataManager.DeleteChunkAsync(chunkInfo, context.CancellationToken);

                if (!dataRemoved)
                {
                    _logger.LogWarning("Failed to delete chunk data file for Chunk {ChunkId} (or it was already gone)",
                        request.ChunkId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chunk data for Chunk {ChunkId}", request.ChunkId);
            }

            
            bool overallSuccess = metadataRemoved || dataRemoved;
            string message;
            
            if (metadataRemoved && dataRemoved)
                message = "Chunk metadata and data successfully deleted";
            else if (metadataRemoved)
                message = "Chunk metadata removed but data deletion failed";
            else if (dataRemoved)
                message = "Chunk data deleted but metadata removal failed";
            else
                message = "Failed to delete chunk metadata or data (may not exist)";

            sw.Stop();
            _logger.LogInformation("Delete operation for Chunk {ChunkId} completed in {ElapsedMs}ms: {Message}", 
                request.ChunkId, sw.ElapsedMilliseconds, message);
                
            return new DeleteChunkReply { Success = overallSuccess, Message = message };
        }
        catch (Exception ex)
        {
            sw.Stop();
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
        var chunkInfo = new ChunkModel
        {
            FileId = fileId,
            ChunkId = chunkId,
            StoredNodeId = _nodeOptions.NodeId
        };

        bool metadataRemoved;
        bool dataRemoved;

        
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
            return (false, $"Metadata deletion failed: {ex.Message}");
        }

        
        if (metadataRemoved || true) 
        {
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
                
                if (metadataRemoved)
                {
                    return (true, "Chunk metadata removed but data deletion failed");
                }
                return (false, $"Data deletion failed: {ex.Message}");
            }
        }

        
        if (metadataRemoved && dataRemoved)
            return (true, "Chunk metadata and data successfully deleted");
        if (metadataRemoved)
            return (true, "Chunk metadata removed but data deletion failed");
        if (dataRemoved)
            return (true, "Chunk data deleted but metadata removal failed");
        
        return (false, "Failed to delete chunk metadata or data (may not exist)");
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

    public override Task<PingReply> Ping(PingRequest request, ServerCallContext context)
    {
        var sw = Stopwatch.StartNew();
    
        _logger.LogDebug("Node {NodeId} received Ping request from Node {SenderNodeId}.",
            _nodeOptions.NodeId, request.SenderNodeId ?? "Unknown");
        
        var reply = new PingReply
        {
            ResponderNodeId = _nodeOptions.NodeId,
            Success = true
        };
    
        sw.Stop();
    
        
        if (sw.ElapsedMilliseconds > 10)
        {
            _logger.LogDebug("Processed ping from {SenderNodeId} in {ElapsedMs}ms", 
                request.SenderNodeId ?? "Unknown", sw.ElapsedMilliseconds);
        }
    
        return Task.FromResult(reply);
    }
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

        var sw = Stopwatch.StartNew();
        _logger.LogInformation(
            "Node {NodeId} received RequestChunk for Chunk {ChunkId} of File {FileId} from peer {Peer}",
            _nodeOptions.NodeId, request.ChunkId, request.FileId, context.Peer);

        Stream? chunkDataStream = null;
        long totalBytesSent = 0;
        
        try
        {
            chunkDataStream = await GetLocalChunkDataStreamAsync(request.FileId, request.ChunkId, context.CancellationToken);

            if (chunkDataStream == null)
            {
                _logger.LogWarning("Chunk {ChunkId} not found locally on Node {NodeId}", request.ChunkId,
                    _nodeOptions.NodeId);
                throw new RpcException(new Status(StatusCode.NotFound, "Chunk not found locally"));
            }

            
            long totalSize = chunkDataStream.Length;
            
            
            const int bufferSize = 256 * 1024; 
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            int chunksSent = 0;

            while ((bytesRead = await chunkDataStream.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0)
            {
                
                if (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Client cancelled streaming of Chunk {ChunkId}", request.ChunkId);
                    break;
                }
                
                totalBytesSent += bytesRead;
                chunksSent++;
                
                
                if (totalSize > 10 * 1024 * 1024 && chunksSent % 10 == 0) 
                {
                    double progressPercent = (double)totalBytesSent / totalSize * 100;
                    _logger.LogDebug("Streaming Chunk {ChunkId} progress: {Progress:F1}% ({BytesSent}/{TotalSize})", 
                        request.ChunkId, progressPercent, FormatBytes(totalBytesSent), FormatBytes(totalSize));
                }

                await responseStream.WriteAsync(new RequestChunkReply
                {
                    Data = ByteString.CopyFrom(buffer, 0, bytesRead)
                }, context.CancellationToken);
            }

            sw.Stop();
            double transferSpeed = sw.ElapsedMilliseconds > 0 
                ? totalBytesSent / (1024.0 * 1024) / (sw.ElapsedMilliseconds / 1000.0) 
                : 0;
                
            _logger.LogInformation(
                "Finished streaming Chunk {ChunkId} ({TotalBytes}) to {Peer} in {ElapsedMs}ms at {Speed:F2}MB/s", 
                request.ChunkId, 
                FormatBytes(totalBytesSent),
                context.Peer, 
                sw.ElapsedMilliseconds,
                transferSpeed);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming Chunk {ChunkId} to {Peer} was cancelled", request.ChunkId, context.Peer);
            throw new RpcException(new Status(StatusCode.Cancelled, "Operation was cancelled"));
        }
        catch (RpcException)
        {
            
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error streaming Chunk {ChunkId} to peer {Peer} after sending {BytesSent} bytes", 
                request.ChunkId, context.Peer, FormatBytes(totalBytesSent));
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to stream chunk: {ex.Message}"));
        }
        finally
        {
            if (chunkDataStream != null)
            {
                await chunkDataStream.DisposeAsync();
                _logger.LogDebug("Released resources for Chunk {ChunkId} stream", request.ChunkId);
            }
        }
    }

    private async Task<Stream?> GetLocalChunkDataStreamAsync(string fileId, string chunkId,
        CancellationToken cancellationToken)
    {
        try
        {
            var chunkInfo = new ChunkModel
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

        const int bufferSize = 65536; 
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

    public override async Task<GetNodeFileListReply> GetNodeFileList(
        GetNodeFileListRequest request, ServerCallContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Node {NodeId} received GetNodeFileList request from peer {Peer}",
            _nodeOptions.NodeId, context.Peer);
        
        var reply = new GetNodeFileListReply();

        try
        {
            
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            
            var localFiles = await _metadataManager.ListFilesAsync(cts.Token);

            if (localFiles != null)
            {
                int fileCount = 0;
                int skippedCount = 0;
                
                foreach (var fileCore in localFiles)
                {
                    if (fileCore.State == FileStateCore.Deleting || fileCore.State == FileStateCore.Deleted)
                    {
                        skippedCount++;
                        continue;
                    }
                    
                    
                    var fileProto = _mapper.Map<FileMetadata>(fileCore);
                    reply.Files.Add(fileProto);
                    fileCount++;
                    
                    if (fileCount >= 1000) 
                    {
                        _logger.LogWarning("GetNodeFileList truncated to 1000 files for performance");
                        break;
                    }
                }
            }
            
            sw.Stop();
            _logger.LogInformation("Returning {Count} file metadata entries to peer {Peer} in {ElapsedMs}ms",
                reply.Files.Count, context.Peer, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("GetNodeFileList request timed out after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw new RpcException(new Status(StatusCode.DeadlineExceeded, "Operation timed out"));
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error processing GetNodeFileList request for peer {Peer}", context.Peer);
            throw new RpcException(new Status(StatusCode.Internal, 
                $"Failed to retrieve local file list: {ex.Message}"));
        }
        
        return reply;
    }

    public override async Task<Empty> AcknowledgeReplica(AcknowledgeReplicaRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.FileId) ||
            string.IsNullOrEmpty(request.ChunkId) ||
            string.IsNullOrEmpty(request.ReplicaNodeId))
        {
            _logger.LogWarning("Received invalid AcknowledgeReplica request with missing IDs from peer {Peer}",
                context.Peer);
            return new Empty();
        }

        string localNodeId = _nodeOptions.NodeId ?? "Unknown";

        _logger.LogInformation("Node {LocalNodeId} received AcknowledgeReplica for Chunk {ChunkId} (File {FileId}) " +
                               "successfully stored on Node {ReplicaNodeId}",
            localNodeId, request.ChunkId, request.FileId, request.ReplicaNodeId);
        
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
        }

        return new Empty();
    }
    
    private T HandleGrpcException<T>(string operation, Exception ex, T errorResult) where T : class
    {
        if (ex is OperationCanceledException)
        {
            _logger.LogInformation("{Operation} was cancelled", operation);
            throw new RpcException(new Status(StatusCode.Cancelled, "Operation was cancelled"));
        }
    
        if (ex is RpcException rpcEx)
        {
            throw rpcEx;
        }
    
        _logger.LogError(ex, "Error during {Operation}", operation);
        return errorResult;
    }
}