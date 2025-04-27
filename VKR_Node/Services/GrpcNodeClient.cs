using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR.Protos;
using System.Threading; // Add CancellationToken namespace
using System.Threading.Tasks; // Add Task namespace

namespace VKR_Node.Services
{
    // Ensure it implements IDisposable if the interface requires it
    public class GrpcNodeClient : INodeClient
    {
        private readonly ILogger<GrpcNodeClient> _logger;
        private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new ();
        private readonly string _callingNodeId;

        public GrpcNodeClient(ILogger<GrpcNodeClient> logger, Microsoft.Extensions.Options.IOptions<Configuration.NodeOptions> nodeOptions)
        {
            _logger = logger;
            // Ensure NodeId is standardized in options
            _callingNodeId = nodeOptions.Value?.NodeId ?? throw new InvalidOperationException("NodeId is not configured.");
        }

        /// <summary>
        /// Gets or creates a gRPC channel for the specified target address.
        /// </summary>
        private GrpcChannel GetOrCreateChannel(string targetNodeAddress)
        {
            // Normalize address (ensure scheme)
            string formattedAddress = targetNodeAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || targetNodeAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? targetNodeAddress
                : $"http://{targetNodeAddress}"; // Default to http if no scheme

            return _channels.GetOrAdd(formattedAddress, addr => // Use formattedAddress as key
            {
                _logger.LogDebug("Creating gRPC channel for address: {Address}", addr);
                // *** SECURITY WARNING: Replace this in production! ***
                // var httpHandler = new HttpClientHandler
                // {
                //     ServerCertificateCustomValidationCallback =
                //         HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                // };
                // return GrpcChannel.ForAddress(addr, new GrpcChannelOptions { HttpHandler = httpHandler });
                return GrpcChannel.ForAddress(addr); // Use default handler (requires proper certs or config)
            });
        }

        /// <summary>
        /// Sends a ReplicateChunk request to the target node.
        /// </summary>
        public async Task<ReplicateChunkReply> ReplicateChunkToNodeAsync(string targetNodeAddress, ReplicateChunkRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sending ReplicateChunk request for Chunk {ChunkId} to Node {TargetAddress}", request.ChunkId, targetNodeAddress);
            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                // Pass cancellation token to CallOptions
                var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: cancellationToken); // Increased deadline?
                return await client.ReplicateChunkAsync(request, options);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC Error sending ReplicateChunk to {TargetAddress}: {StatusCode}", targetNodeAddress, ex.StatusCode);
                return new ReplicateChunkReply { Success = false, Message = $"gRPC Error: {ex.Status.Detail} (Code: {ex.StatusCode})" };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 _logger.LogWarning("ReplicateChunk request to {TargetAddress} cancelled.", targetNodeAddress);
                 throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ReplicateChunk request to {TargetAddress}", targetNodeAddress);
                return new ReplicateChunkReply { Success = false, Message = $"Failed to send replicate request: {ex.Message}" };
            }
        }

        /// <summary>
        /// Sends a DeleteChunk request to the target node.
        /// </summary>
        public async Task<DeleteChunkReply> DeleteChunkOnNodeAsync(string targetNodeAddress, DeleteChunkRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sending DeleteChunk request for Chunk {ChunkId} (File {FileId}) to Node {TargetAddress}", request.ChunkId, request.FileId, targetNodeAddress);
            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: cancellationToken);
                return await client.DeleteChunkAsync(request, options);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC Error sending DeleteChunk to {TargetAddress}: {StatusCode}", targetNodeAddress, ex.StatusCode);
                return new DeleteChunkReply { Success = false, Message = $"gRPC Error: {ex.Status.Detail} (Code: {ex.StatusCode})" };
            }
             catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 _logger.LogWarning("DeleteChunk request to {TargetAddress} cancelled.", targetNodeAddress);
                 throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending DeleteChunk request to {TargetAddress}", targetNodeAddress);
                return new DeleteChunkReply { Success = false, Message = $"Failed to send delete request: {ex.Message}" };
            }
        }

        /// <summary>
        /// Sends a Ping request to the target node.
        /// </summary>
        public async Task<PingReply> PingNodeAsync(string targetNodeAddress, PingRequest request, CancellationToken cancellationToken = default) // MODIFIED: Added token parameter
        {
            _logger.LogDebug("Sending Ping request from Node {CallingNodeId} to Node {TargetAddress}", _callingNodeId, targetNodeAddress);
            request.SenderNodeId ??= _callingNodeId;

            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                // MODIFIED: Pass CancellationToken to CallOptions
                var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: cancellationToken); // Shorter timeout for ping
                var reply = await client.PingAsync(request, options);
                _logger.LogDebug("Received Ping reply from {ResponderNodeId} at {TargetAddress}. Success: {Success}", reply?.ResponderNodeId ?? "N/A", targetNodeAddress, reply?.Success ?? false);
                return reply ?? new PingReply { Success = false, ResponderNodeId = "Error: Null reply" };
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable || ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("Ping to {TargetAddress} failed with status {StatusCode}. Marking as likely offline.", targetNodeAddress, ex.StatusCode);
                return new PingReply { Success = false, ResponderNodeId = $"Error: {ex.StatusCode}" };
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC Error sending Ping to {TargetAddress}: {StatusCode}", targetNodeAddress, ex.StatusCode);
                 return new PingReply { Success = false, ResponderNodeId = $"Error: {ex.StatusCode} - {ex.Status.Detail}" };
            }
             catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 // If the cancellation was external, log and re-throw
                 _logger.LogWarning("Ping request to {TargetAddress} cancelled.", targetNodeAddress);
                 // Don't return normally, let caller know it was cancelled
                 throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Ping request to {TargetAddress}", targetNodeAddress);
                return new PingReply { Success = false, ResponderNodeId = $"Error: {ex.Message}" };
            }
        }

        // --- DHT Methods (Still not implemented) ---
        public Task<NodeInfoCore?> FindSuccessorOnNodeAsync(NodeInfoCore targetNode, string keyId, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("FindSuccessorOnNodeAsync is not implemented.");
            throw new NotImplementedException();
        }

        public Task<NodeInfoCore?> GetPredecessorFromNodeAsync(NodeInfoCore targetNode, CancellationToken cancellationToken = default)
        {
             _logger.LogWarning("GetPredecessorFromNodeAsync is not implemented.");
            throw new NotImplementedException();
        }

        public Task<bool> NotifyNodeAsync(NodeInfoCore targetNode, NodeInfoCore selfInfo, CancellationToken cancellationToken = default)
        {
             _logger.LogWarning("NotifyNodeAsync is not implemented.");
            throw new NotImplementedException();
        }
        // --- End DHT Methods ---


        public async Task<AsyncServerStreamingCall<RequestChunkReply>?> RequestChunkFromNodeAsync(string targetNodeAddress, RequestChunkRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Sending RequestChunk for Chunk {ChunkId} (File {FileId}) to Node {TargetAddress}", request.ChunkId, request.FileId, targetNodeAddress);
            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                var options = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(60), // Example: 60 second timeout
                    cancellationToken: cancellationToken);
                var call = client.RequestChunk(request, options);
                return call;
            }
            // Catch specific exceptions that indicate failure to initiate the call
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("gRPC Error ({StatusCode}) initiating RequestChunk to {TargetAddress}", ex.StatusCode, targetNodeAddress);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 _logger.LogWarning("RequestChunk initiation to {TargetAddress} cancelled.", targetNodeAddress);
                 return null; // Can't return the call object if cancelled before it starts
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating RequestChunk request to {TargetAddress}", targetNodeAddress);
                return null;
            }
        }

        public async Task<GetNodeFileListReply?> GetNodeFileListAsync(string targetNodeAddress, GetNodeFileListRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Sending GetNodeFileList request to Node {TargetAddress}", targetNodeAddress);
            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                var options = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(15),
                    cancellationToken: cancellationToken);
                var reply = await client.GetNodeFileListAsync(request, options);
                return reply;
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("gRPC Error ({StatusCode}) sending GetNodeFileList to {TargetAddress}", ex.StatusCode, targetNodeAddress);
                return null;
            }
             catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 _logger.LogWarning("GetNodeFileList request to {TargetAddress} cancelled.", targetNodeAddress);
                 throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending GetNodeFileList request to {TargetAddress}", targetNodeAddress);
                return null;
            }
        }

        public async Task AcknowledgeReplicaAsync(string targetNodeAddress, AcknowledgeReplicaRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sending AcknowledgeReplica request for Chunk {ChunkId} (File {FileId}) stored by {ReplicaNodeId} to Node {TargetAddress}",
                request.ChunkId, request.FileId, request.ReplicaNodeId, targetNodeAddress);
            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                var options = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(10),
                    cancellationToken: cancellationToken);
                // google.protobuf.Empty reply doesn't need to be awaited if not checking success explicitly here
                await client.AcknowledgeReplicaAsync(request, options);
                _logger.LogDebug("Successfully sent AcknowledgeReplica request for Chunk {ChunkId} to Node {TargetAddress}", request.ChunkId, targetNodeAddress);
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("gRPC Error ({StatusCode}) sending AcknowledgeReplica to {TargetAddress}. Acknowledgement likely not received.", ex.StatusCode, targetNodeAddress);
                // Don't re-throw, allow caller to continue (fire-and-forget nature)
            }
             catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                 _logger.LogWarning("AcknowledgeReplica request to {TargetAddress} cancelled.", targetNodeAddress);
                 // Don't re-throw
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending AcknowledgeReplica request to {TargetAddress}", targetNodeAddress);
                 // Don't re-throw
            }
        }

        public void Dispose()
        {
            _logger.LogInformation("Disposing GrpcNodeClient and closing cached channels.");
            // Dispose channels correctly
            var channelsToDispose = _channels.Values.ToList(); // Copy values to avoid modification during iteration issues
            _channels.Clear(); // Clear the dictionary

            foreach (var channel in channelsToDispose)
            {
                try
                {
                    // Allow ongoing calls to complete gracefully? ShutdownAsync might be better.
                    // channel.ShutdownAsync().Wait(); // Or use await if method is async
                    channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing gRPC channel to {Target}", channel.Target);
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}