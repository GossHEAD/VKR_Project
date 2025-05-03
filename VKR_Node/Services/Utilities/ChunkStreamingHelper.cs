using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos;

namespace VKR_Node.Services.Utilities;

/// <summary>
/// Helper class for streaming chunk data from local or remote sources
/// </summary>
public class ChunkStreamingHelper
{
    private readonly ILogger _logger;
    private readonly IDataManager _dataManager;
    private readonly INodeClient _nodeClient;
    private readonly string _localNodeId;

    public ChunkStreamingHelper(
        ILogger logger,
        IDataManager dataManager,
        INodeClient nodeClient,
        string localNodeId)
    {
        _logger = logger;
        _dataManager = dataManager;
        _nodeClient = nodeClient;
        _localNodeId = localNodeId;
    }

    /// <summary>
    /// Attempts to stream a chunk from the local storage to the response stream
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> TryStreamLocalChunkAsync(
        ChunkModel chunkInfo,
        IServerStreamWriter<DownloadFileReply> responseStream,
        ServerCallContext context)
    {
        Stream? chunkStream = null;
        
        try
        {
            // Create a copy of the chunk info with the local node ID
            var localChunkInfo = chunkInfo with { StoredNodeId = _localNodeId };
            
            // Retrieve the chunk data from local storage
            chunkStream = await _dataManager.RetrieveChunkAsync(localChunkInfo, context.CancellationToken);
            
            if (chunkStream == null)
            {
                _logger.LogWarning("DataManager returned null stream for local Chunk {ChunkId}.", chunkInfo.ChunkId);
                return false;
            }
            
            // Stream the chunk data in buffer-sized pieces
            const int bufferSize = 65536; // 64KB buffer
            byte[] buffer = new byte[bufferSize];
            int bytesRead;
            
            while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0)
            {
                var fileChunkProto = new FileChunk
                {
                    FileId = chunkInfo.FileId,
                    ChunkId = chunkInfo.ChunkId,
                    ChunkIndex = chunkInfo.ChunkIndex,
                    Data = ByteString.CopyFrom(buffer, 0, bytesRead),
                    Size = bytesRead
                };
                
                await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto }, context.CancellationToken);
            }
            
            _logger.LogInformation("Successfully streamed local Chunk {ChunkId} (Index {Index})", 
                chunkInfo.ChunkId, chunkInfo.ChunkIndex);
                
            return true;
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation
            _logger.LogInformation("Streaming of local Chunk {ChunkId} was cancelled", chunkInfo.ChunkId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming local Chunk {ChunkId}", chunkInfo.ChunkId);
            return false;
        }
        finally
        {
            // Ensure the stream is properly disposed
            if (chunkStream != null)
            {
                await chunkStream.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Attempts to stream a chunk from a remote node to the response stream
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    public async Task<bool> TryStreamRemoteChunkAsync(
        ChunkModel chunkInfo,
        KnownNodeOptions targetNodeInfo,
        IServerStreamWriter<DownloadFileReply> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Attempting to fetch Chunk {ChunkId} from remote Node {NodeId} ({Address})", 
            chunkInfo.ChunkId, targetNodeInfo.NodeId, targetNodeInfo.Address);
            
        AsyncServerStreamingCall<RequestChunkReply>? remoteCall = null;
        
        try
        {
            // Create request to fetch chunk from remote node
            var remoteRequest = new RequestChunkRequest
            {
                FileId = chunkInfo.FileId,
                ChunkId = chunkInfo.ChunkId
            };
            
            // Create a timeout for the remote request
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60)); // Timeout after 60 seconds
            
            // Initiate the request to the remote node
            remoteCall = await _nodeClient.RequestChunkFromNodeAsync(
                targetNodeInfo.Address, 
                remoteRequest, 
                cts.Token);
                
            if (remoteCall == null)
            {
                _logger.LogWarning("Failed to initiate RequestChunk call to node {NodeId}", targetNodeInfo.NodeId);
                return false;
            }
            
            // Stream the response data from the remote node to the client
            await foreach (var replyChunk in remoteCall.ResponseStream.ReadAllAsync(cts.Token))
            {
                if (replyChunk.Data != null && !replyChunk.Data.IsEmpty)
                {
                    var fileChunkProto = new FileChunk
                    {
                        FileId = chunkInfo.FileId,
                        ChunkId = chunkInfo.ChunkId,
                        ChunkIndex = chunkInfo.ChunkIndex,
                        Data = replyChunk.Data,
                        Size = replyChunk.Data.Length
                    };
                    
                    await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto }, cts.Token);
                }
                else
                {
                    _logger.LogWarning("Received empty data chunk from remote Node {NodeId} for Chunk {ChunkId}", 
                        targetNodeInfo.NodeId, chunkInfo.ChunkId);
                }
            }
            
            _logger.LogInformation("Successfully streamed Chunk {ChunkId} (Index {Index}) from remote Node {NodeId}", 
                chunkInfo.ChunkId, chunkInfo.ChunkIndex, targetNodeInfo.NodeId);
                
            return true;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Remote Node {NodeId} reported NotFound for Chunk {ChunkId}", 
                targetNodeInfo.NodeId, chunkInfo.ChunkId);
            return false;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
        {
            _logger.LogWarning("gRPC error ({Status}) fetching Chunk {ChunkId} from Node {NodeId}", 
                ex.StatusCode, chunkInfo.ChunkId, targetNodeInfo.NodeId);
            return false;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Download cancelled while fetching Chunk {ChunkId} from Node {NodeId}", 
                chunkInfo.ChunkId, targetNodeInfo.NodeId);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Request for Chunk {ChunkId} from Node {NodeId} timed out", 
                chunkInfo.ChunkId, targetNodeInfo.NodeId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching/streaming Chunk {ChunkId} from Node {NodeId}", 
                chunkInfo.ChunkId, targetNodeInfo.NodeId);
            return false;
        }
        finally
        {
            // Ensure proper disposal of the gRPC call
            remoteCall?.Dispose();
        }
    }
}