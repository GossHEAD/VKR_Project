using System.Collections.Concurrent;
using Google.Protobuf;
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
        
        public GrpcNodeClient(
            ILogger<GrpcNodeClient> logger,
            IOptions<Configuration.NodeIdentityOptions> nodeOptions,
            IOptions<Configuration.NetworkOptions> networkOptions)
        {
            _logger = logger;
            _callingNodeId = nodeOptions.Value?.NodeId ?? throw new InvalidOperationException("NodeId is not configured");
    
            var network = networkOptions.Value;
            if (network != null)
            {
                _channelIdleTimeout = TimeSpan.FromSeconds(network.ConnectionTimeoutSeconds * 2);
        
                _cleanupInterval = TimeSpan.FromMinutes(
                    Math.Max(1, Math.Min(30, network.MaxConnections / 10)));
            }

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
        
        private void CleanupIdleChannels()
        {
            try
            {
                var now = DateTime.UtcNow;
                
                foreach (var key in _channelCircuitBreakers.Keys.ToList())
                {
                    if (_channelCircuitBreakers.TryGetValue(key, out var expiry) && now > expiry)
                    {
                        _channelCircuitBreakers.TryRemove(key, out _);
                        _channelFailureCount.TryRemove(key, out _);
                        _logger.LogDebug("Circuit breaker expired for {Address}", key);
                    }
                }
                
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
        
        private GrpcChannel GetOrCreateChannel(string targetNodeAddress)
        {
            string formattedAddress = NormalizeAddress(targetNodeAddress);

            if (_channelCircuitBreakers.TryGetValue(formattedAddress, out var breakUntil) && 
                DateTime.UtcNow < breakUntil)
            {
                _logger.LogWarning("Circuit breaker open for {Address} until {Time}", 
                    formattedAddress, breakUntil.ToString("HH:mm:ss"));
            
                return GrpcChannel.ForAddress(formattedAddress, new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(5),
                        EnableMultipleHttp2Connections = true
                    }
                });
            }

            var channel = _channels.GetOrAdd(formattedAddress, addr => {
                _logger.LogDebug("Creating gRPC channel for address: {Address}", addr);
                /*
                return GrpcChannel.ForAddress(addr, new GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(10),
                        EnableMultipleHttp2Connections = true
                    }
                });
                */
                var options = new GrpcChannelOptions
                {
                    MaxReceiveMessageSize = 100 * 1024 * 1024, 
                    MaxSendMessageSize = 100 * 1024 * 1024,    
                    HttpHandler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(30),
                        EnableMultipleHttp2Connections = true
                    }
                };
        
                return GrpcChannel.ForAddress(addr, options);
            });
            
            _channelLastUsed[formattedAddress] = DateTime.UtcNow;

            return channel;
        }
        
        private string NormalizeAddress(string targetNodeAddress)
        {
            if (string.IsNullOrWhiteSpace(targetNodeAddress))
                throw new ArgumentException("Target node address cannot be null or empty", nameof(targetNodeAddress));

            return targetNodeAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                   targetNodeAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? targetNodeAddress
                : $"http://{targetNodeAddress}";
        }
        
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
                
                int failures = _channelFailureCount.AddOrUpdate(
                    formattedAddress, 
                    1,
                    (_, current) => current + 1 
                );
                
                if (failures >= MaxFailuresBeforeCircuitBreak)
                {
                    var breakUntil = DateTime.UtcNow + _circuitBreakDuration;
                    _channelCircuitBreakers[formattedAddress] = breakUntil;
                    
                    _logger.LogWarning("Circuit breaker triggered for {Target} until {Time} after {Failures} failures", 
                        targetAddress, breakUntil.ToString("HH:mm:ss"), failures);
                    
                    if (_channels.TryRemove(formattedAddress, out var failedChannel))
                    {
                        try
                        {
                            failedChannel.ShutdownAsync().Wait(TimeSpan.FromSeconds(1));
                        }
                        catch
                        {
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
                throw; 
            }
            catch (Exception ex)
            {
                sw.Stop();
                
                int failures = _channelFailureCount.AddOrUpdate(
                    formattedAddress, 
                    1,
                    (_, current) => current + 1 
                );
                
                _logger.LogError(ex, "Error during gRPC {Method} to {Target} (Failure {Count})", 
                    methodName, targetAddress, failures);
                return null;
            }
        }
        
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
        
        public async Task<ReplicateChunkReply> ReplicateChunkToNodeStreamingAsync(
            string targetNodeAddress,
            ReplicateChunkMetadata metadata,
            Stream dataStream,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Sending streaming ReplicateChunk request for Chunk {ChunkId} to Node {TargetAddress}", 
                metadata.ChunkId, targetNodeAddress);

            return await ExecuteWithErrorHandlingAsync(
                "ReplicateChunkStreaming",
                targetNodeAddress,
                async () => {
                    var channel = GetOrCreateChannel(targetNodeAddress);
                    var client = new NodeInternalService.NodeInternalServiceClient(channel);
            
                    using var call = client.ReplicateChunkStreaming(cancellationToken: cancellationToken);
                    
                    await call.RequestStream.WriteAsync(new ReplicateChunkStreamingRequest { 
                        Metadata = metadata 
                    });
                    
                    byte[] buffer = new byte[64 * 1024]; 
                    int bytesRead;
                    while ((bytesRead = await dataStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await call.RequestStream.WriteAsync(new ReplicateChunkStreamingRequest {
                            DataChunk = ByteString.CopyFrom(buffer, 0, bytesRead)
                        });
                    }
            
                    await call.RequestStream.CompleteAsync();
                    return await call.ResponseAsync;
                },
                cancellationToken) ?? new ReplicateChunkReply { 
                Success = false, 
                Message = "Подключение к узлу не удалось" 
            };
        }
        
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
        
        public Task<NodeModel?> FindSuccessorOnNodeAsync(
            NodeModel targetNode, 
            string keyId, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("FindSuccessorOnNodeAsync is not implemented");
            // TODO: Implement when DHT functionality is required
            return Task.FromResult<NodeModel?>(null);
        }
        
        public Task<NodeModel?> GetPredecessorFromNodeAsync(
            NodeModel targetNode, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("GetPredecessorFromNodeAsync is not implemented");
            // TODO: Implement when DHT functionality is required
            return Task.FromResult<NodeModel?>(null);
        }
        
        public Task<bool> NotifyNodeAsync(
            NodeModel targetNode, 
            NodeModel selfInfo, 
            CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("NotifyNodeAsync is not implemented");
            return Task.FromResult(false);
        }

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
                throw; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating RequestChunk request to {TargetAddress}", targetNodeAddress);
                return null;
            }
        }

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
        public void Dispose()
        {
            if (_disposed) return;
            
            _logger.LogInformation("Disposing GrpcNodeClient and closing cached channels");
            _disposed = true;
            
            _cleanupCts.Cancel();
            try { _cleanupTask.Wait(TimeSpan.FromSeconds(1)); } catch { /* Ignore */ }
            _cleanupCts.Dispose();
            
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