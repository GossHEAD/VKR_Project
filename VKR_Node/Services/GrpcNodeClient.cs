using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Services.Utilities;
using VKR.Protos;

namespace VKR_Node.Services
{
    /// <summary>
    /// Client implementation for making gRPC calls to other nodes in the network.
    /// Provides methods to send requests, manage channels, and handle responses.
    /// </summary>
    public class GrpcNodeClient : INodeClient
    {
        private readonly ILogger<GrpcNodeClient> _logger;
        private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();
        private readonly ConcurrentDictionary<string, DateTime> _channelLastUsed = new();
        private readonly string _callingNodeId;
        private readonly TimeSpan _channelIdleTimeout = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(10);
        private bool _disposed;
        private readonly Task _cleanupTask;
        private readonly CancellationTokenSource _cleanupCts = new();
        
        private readonly ConcurrentDictionary<string, int> _channelFailureCount = new();
        private readonly ConcurrentDictionary<string, DateTime> _channelCircuitBreakers = new();
        private const int MaxFailuresBeforeCircuitBreak = 3;
        private readonly TimeSpan _circuitBreakDuration = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Initializes a new instance of the GrpcNodeClient.
        /// </summary>
        public GrpcNodeClient(ILogger<GrpcNodeClient> logger, IOptions<Configuration.NodeIdentityOptions> nodeOptions)
        {
            _logger = logger;
            _callingNodeId = nodeOptions.Value?.NodeId ?? throw new InvalidOperationException("NodeId is not configured");

            _cleanupTask = Task.Run(async () => {
                try
                {
                    _ = Task.Run(LogMetricsAsync);
                    while (!_cleanupCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(_cleanupInterval, _cleanupCts.Token);
                        CleanupIdleChannels();
                    }
                }
                catch (OperationCanceledException) {}
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in channel cleanup background task");
                }
            });

            _logger.LogInformation("GrpcNodeClient initialized for node {NodeId}", _callingNodeId);
        }
        
        // Add to constructor
        public GrpcNodeClient(
            ILogger<GrpcNodeClient> logger,
            IOptions<Configuration.NodeIdentityOptions> nodeOptions,
            IOptions<Configuration.NetworkOptions> networkOptions)
        {
            _logger = logger;
            _callingNodeId = nodeOptions.Value?.NodeId ?? throw new InvalidOperationException("NodeId is not configured");
    
            // Apply network configuration
            var network = networkOptions.Value;
            if (network != null)
            {
                // Set connection timeout based on configuration
                _channelIdleTimeout = TimeSpan.FromSeconds(network.ConnectionTimeoutSeconds * 2);
        
                // Max connections can influence circuit breaker thresholds
                _cleanupInterval = TimeSpan.FromMinutes(
                    Math.Max(1, Math.Min(30, network.MaxConnections / 10)));
            }

            // Start cleanup task
            _cleanupTask = Task.Run(async () => {
                try
                {
                    while (!_cleanupCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(_cleanupInterval, _cleanupCts.Token);
                        CleanupIdleChannels();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in channel cleanup background task");
                }
            });

            _logger.LogInformation("GrpcNodeClient initialized for node {NodeId} with idle timeout {Timeout}s", 
                _callingNodeId, _channelIdleTimeout.TotalSeconds);
        }
        
        /// <summary>
        /// Cleans up idle channels and expired circuit breakers.
        /// </summary>
        private void CleanupIdleChannels()
        {
            try
            {
                var now = DateTime.UtcNow;
        
                // 1. Clean up expired circuit breakers
                foreach (var key in _channelCircuitBreakers.Keys.ToList())
                {
                    if (_channelCircuitBreakers.TryGetValue(key, out var expiry) && now > expiry)
                    {
                        _channelCircuitBreakers.TryRemove(key, out _);
                        _channelFailureCount.TryRemove(key, out _);
                        _logger.LogDebug("Circuit breaker expired for {Address}", key);
                    }
                }
        
                // 2. Clean up idle channels
                var keysToRemove = _channelLastUsed
                    .Where(kvp => (now - kvp.Value) > _channelIdleTimeout)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    if (_channelLastUsed.TryRemove(key, out _) && 
                        _channels.TryRemove(key, out var channel))
                    {
                        try
                        {
                            // Use ShutdownAsync for graceful shutdown
                            channel.ShutdownAsync().Wait(TimeSpan.FromSeconds(2));
                            _logger.LogDebug("Closed idle channel to {Address}", key);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error closing idle channel to {Address}", key);
                        }
                    }
                }
        
                _logger.LogTrace("Channel cleanup complete. Removed {Count} idle channels. Current channel count: {ChannelCount}", 
                    keysToRemove.Count, _channels.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during channel cleanup");
            }
        }

        /// <summary>
        /// Gets or creates a gRPC channel for the specified target address with circuit breaking.
        /// </summary>
        private GrpcChannel GetOrCreateChannel(string targetNodeAddress)
        {
            // Normalize address
            string formattedAddress = NormalizeAddress(targetNodeAddress);

            // Check if this address has an open circuit breaker
            if (_channelCircuitBreakers.TryGetValue(formattedAddress, out var breakUntil) && 
                DateTime.UtcNow < breakUntil)
            {
                _logger.LogWarning("Circuit breaker open for {Address} until {Time}", 
                    formattedAddress, breakUntil.ToString("HH:mm:ss"));
            
                // Create a one-time channel that won't be cached
                return GrpcChannel.ForAddress(formattedAddress, new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(5),
                        EnableMultipleHttp2Connections = true
                    }
                });
            }

            // Use the existing channel if available
            var channel = _channels.GetOrAdd(formattedAddress, addr => {
                _logger.LogDebug("Creating gRPC channel for address: {Address}", addr);
                return GrpcChannel.ForAddress(addr, new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(10),
                        EnableMultipleHttp2Connections = true
                    }
                });
            });

            // Record channel usage time
            _channelLastUsed[formattedAddress] = DateTime.UtcNow;

            return channel;
        }

        /// <summary>
        /// Normalizes an address to ensure consistent format.
        /// </summary>
        private string NormalizeAddress(string targetNodeAddress)
        {
            if (string.IsNullOrWhiteSpace(targetNodeAddress))
                throw new ArgumentException("Target node address cannot be null or empty", nameof(targetNodeAddress));

            return targetNodeAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                   targetNodeAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? targetNodeAddress
                : $"http://{targetNodeAddress}";
        }

        /// <summary>
        /// Helper method to execute gRPC operations with consistent error handling.
        /// </summary>
        private async Task<T?> ExecuteWithErrorHandlingAsync<T>(
            string methodName,
            string targetAddress,
            Func<Task<T>> operation,
            CancellationToken cancellationToken) where T : class
        {
            string formattedAddress = NormalizeAddress(targetAddress);
            string metricKey = methodName;
            _metrics.AddOrUpdate(
                metricKey,
                (1, 0, 0),
                (_, current) => (current.Calls + 1, current.SuccessfulCalls, current.TotalMs)
            );
    
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                var result = await operation();
                sw.Stop();
                
                _channelFailureCount.TryRemove(formattedAddress, out _);
                _metrics.AddOrUpdate(
                    metricKey,
                    (1, 1, sw.ElapsedMilliseconds),  
                    (_, current) => (current.Calls, current.SuccessfulCalls + 1, current.TotalMs + sw.ElapsedMilliseconds)
                );
                
                _logger.LogDebug("gRPC {Method} to {Target} completed in {ElapsedMs}ms", 
                    methodName, targetAddress, sw.ElapsedMilliseconds);
                    
                return result;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable || 
                                        ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                sw.Stop();
                
                // Increment failure count
                int failures = _channelFailureCount.AddOrUpdate(
                    formattedAddress, 
                    1,  // Initial value if key doesn't exist
                    (_, current) => current + 1  // Increment existing value
                );
                
                // If too many failures, implement circuit breaker
                if (failures >= MaxFailuresBeforeCircuitBreak)
                {
                    var breakUntil = DateTime.UtcNow + _circuitBreakDuration;
                    _channelCircuitBreakers[formattedAddress] = breakUntil;
                    
                    _logger.LogWarning("Circuit breaker triggered for {Target} until {Time} after {Failures} failures", 
                        targetAddress, breakUntil.ToString("HH:mm:ss"), failures);
                        
                    // Remove the failed channel to force recreation
                    if (_channels.TryRemove(formattedAddress, out var failedChannel))
                    {
                        try
                        {
                            // Try to close gracefully
                            failedChannel.ShutdownAsync().Wait(TimeSpan.FromSeconds(1));
                        }
                        catch
                        {
                            // Ignore shutdown errors
                        }
                    }
                }
                
                _logger.LogWarning("gRPC {Method} to {Target} failed with status {StatusCode} (Failure {Count})", 
                    methodName, targetAddress, ex.StatusCode, failures);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("gRPC {Method} to {Target} was cancelled", methodName, targetAddress);
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                sw.Stop();
                
                // Increment failure count for any exception
                int failures = _channelFailureCount.AddOrUpdate(
                    formattedAddress, 
                    1,  // Initial value if key doesn't exist
                    (_, current) => current + 1  // Increment existing value
                );
                
                _logger.LogError(ex, "Error during gRPC {Method} to {Target} (Failure {Count})", 
                    methodName, targetAddress, failures);
                return null;
            }
        }

        /// <summary>
        /// Sends a request to replicate a chunk to a specified target node.
        /// </summary>
        public async Task<ReplicateChunkReply> ReplicateChunkToNodeAsync(
            string targetNodeAddress, 
            ReplicateChunkRequest request, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sending ReplicateChunk request for Chunk {ChunkId} to Node {TargetAddress}", 
                request.ChunkId, targetNodeAddress);

            return await ExecuteWithErrorHandlingAsync(
                "ReplicateChunk",
                targetNodeAddress,
                async () => {
                    var channel = GetOrCreateChannel(targetNodeAddress);
                    var client = new NodeInternalService.NodeInternalServiceClient(channel);
                    var options = new CallOptions(
                        deadline: DateTime.UtcNow.AddSeconds(30), 
                        cancellationToken: cancellationToken);
                    
                    return await client.ReplicateChunkAsync(request, options);
                },
                cancellationToken) ?? new ReplicateChunkReply { 
                    Success = false, 
                    Message = "Connection to target node failed" 
                };
        }

        /// <summary>
        /// Sends a request to delete a chunk replica on a specified target node.
        /// </summary>
        public async Task<DeleteChunkReply> DeleteChunkOnNodeAsync(
            string targetNodeAddress, 
            DeleteChunkRequest request, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sending DeleteChunk request for Chunk {ChunkId} (File {FileId}) to Node {TargetAddress}", 
                request.ChunkId, request.FileId, targetNodeAddress);

            return await ExecuteWithErrorHandlingAsync(
                "DeleteChunk",
                targetNodeAddress,
                async () => {
                    var channel = GetOrCreateChannel(targetNodeAddress);
                    var client = new NodeInternalService.NodeInternalServiceClient(channel);
                    var options = new CallOptions(
                        deadline: DateTime.UtcNow.AddSeconds(10), 
                        cancellationToken: cancellationToken);
                    
                    return await client.DeleteChunkAsync(request, options);
                },
                cancellationToken) ?? new DeleteChunkReply { 
                    Success = false, 
                    Message = "Connection to target node failed" 
                };
        }

        /// <summary>
        /// Sends a Ping request to a specified target node to check its availability.
        /// </summary>
        public async Task<PingReply> PingNodeAsync(
            string targetNodeAddress, 
            PingRequest request, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Sending Ping request from Node {CallingNodeId} to Node {TargetAddress}", 
                _callingNodeId, targetNodeAddress);
            
            request.SenderNodeId ??= _callingNodeId;

            return await ExecuteWithErrorHandlingAsync(
                "Ping",
                targetNodeAddress,
                async () => {
                    var channel = GetOrCreateChannel(targetNodeAddress);
                    var client = new NodeInternalService.NodeInternalServiceClient(channel);
                    var options = new CallOptions(
                        deadline: DateTime.UtcNow.AddSeconds(5), 
                        cancellationToken: cancellationToken);
                    
                    return await client.PingAsync(request, options);
                },
                cancellationToken) ?? new PingReply { 
                    Success = false, 
                    ResponderNodeId = "Error: Connection failed" 
                };
        }

        /// <summary>
        /// Finds the successor node for a given key.
        /// </summary>
        public Task<NodeModel?> FindSuccessorOnNodeAsync(
            NodeModel targetNode, 
            string keyId, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("FindSuccessorOnNodeAsync is not implemented");
            // TODO: Implement when DHT functionality is required
            return Task.FromResult<NodeModel?>(null);
        }

        /// <summary>
        /// Gets the predecessor node from a target node.
        /// </summary>
        public Task<NodeModel?> GetPredecessorFromNodeAsync(
            NodeModel targetNode, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("GetPredecessorFromNodeAsync is not implemented");
            // TODO: Implement when DHT functionality is required
            return Task.FromResult<NodeModel?>(null);
        }

        /// <summary>
        /// Notifies a target node about a potential predecessor.
        /// </summary>
        public Task<bool> NotifyNodeAsync(
            NodeModel targetNode, 
            NodeModel selfInfo, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("NotifyNodeAsync is not implemented");
            // TODO: Implement when DHT functionality is required
            return Task.FromResult(false);
        }

        /// <summary>
        /// Requests chunk data from a target node via streaming.
        /// </summary>
        public async Task<AsyncServerStreamingCall<RequestChunkReply>?> RequestChunkFromNodeAsync(
            string targetNodeAddress, 
            RequestChunkRequest request, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Sending RequestChunk for Chunk {ChunkId} (File {FileId}) to Node {TargetAddress}", 
                request.ChunkId, request.FileId, targetNodeAddress);

            try
            {
                var channel = GetOrCreateChannel(targetNodeAddress);
                var client = new NodeInternalService.NodeInternalServiceClient(channel);
                var options = new CallOptions(
                    deadline: DateTime.UtcNow.AddSeconds(60), 
                    cancellationToken: cancellationToken);
                
                // Creating the streaming call
                return client.RequestChunk(request, options);
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("gRPC Error ({StatusCode}) initiating RequestChunk to {TargetAddress}", 
                    ex.StatusCode, targetNodeAddress);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("RequestChunk initiation to {TargetAddress} cancelled", targetNodeAddress);
                throw; // Re-throw cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating RequestChunk request to {TargetAddress}", targetNodeAddress);
                return null;
            }
        }

        /// <summary>
        /// Gets the list of known files from a target node.
        /// </summary>
        public async Task<GetNodeFileListReply?> GetNodeFileListAsync(
            string targetNodeAddress, 
            GetNodeFileListRequest request, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Sending GetNodeFileList request to Node {TargetAddress}", targetNodeAddress);

            return await ExecuteWithErrorHandlingAsync(
                "GetNodeFileList",
                targetNodeAddress,
                async () => {
                    var channel = GetOrCreateChannel(targetNodeAddress);
                    var client = new NodeInternalService.NodeInternalServiceClient(channel);
                    var options = new CallOptions(
                        deadline: DateTime.UtcNow.AddSeconds(15), 
                        cancellationToken: cancellationToken);
                    
                    return await client.GetNodeFileListAsync(request, options);
                },
                cancellationToken);
        }

        /// <summary>
        /// Sends an acknowledgement that a replica has been stored.
        /// </summary>
        public async Task AcknowledgeReplicaAsync(
            string targetNodeAddress, 
            AcknowledgeReplicaRequest request, 
            CancellationToken cancellationToken = default)
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
                
                await client.AcknowledgeReplicaAsync(request, options);
                _logger.LogDebug("Successfully sent AcknowledgeReplica request for Chunk {ChunkId} to Node {TargetAddress}", 
                    request.ChunkId, targetNodeAddress);
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("gRPC Error ({StatusCode}) sending AcknowledgeReplica to {TargetAddress}. Acknowledgement likely not received.", 
                    ex.StatusCode, targetNodeAddress);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("AcknowledgeReplica request to {TargetAddress} cancelled", targetNodeAddress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending AcknowledgeReplica request to {TargetAddress}", targetNodeAddress);
            }
        }
        
        private readonly ConcurrentDictionary<string, (long Calls, long SuccessfulCalls, long TotalMs)> _metrics = new();
        private async Task LogMetricsAsync()
        {
            try
            {
                while (!_cleanupCts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), _cleanupCts.Token);
            
                    foreach (var kvp in _metrics.ToArray())
                    {
                        var (calls, successful, totalMs) = kvp.Value;
                
                        if (calls > 0)
                        {
                            double successRate = (double)successful / calls * 100;
                            double avgLatency = successful > 0 ? (double)totalMs / successful : 0;
                    
                            _logger.LogInformation(
                                "gRPC Method: {Method}, Calls: {Total}, Success Rate: {SuccessRate:F1}%, Avg Latency: {AvgLatency:F1}ms",
                                kvp.Key, calls, successRate, avgLatency);
                        }
                    }
            
                    // Optionally reset metrics after logging
                    _metrics.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in metrics logging task");
            }
        }

        /// <summary>
        /// Disposes of resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            
            _logger.LogInformation("Disposing GrpcNodeClient and closing cached channels");
            _disposed = true;
            
            // Stop the cleanup task
            _cleanupCts.Cancel();
            try { _cleanupTask.Wait(TimeSpan.FromSeconds(1)); } catch { /* Ignore */ }
            _cleanupCts.Dispose();
            
            // Dispose channels
            var channelsToDispose = _channels.Values.ToList();
            _channels.Clear();
            _channelLastUsed.Clear();

            foreach (var channel in channelsToDispose)
            {
                try
                {
                    channel.ShutdownAsync().Wait(TimeSpan.FromSeconds(2));
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