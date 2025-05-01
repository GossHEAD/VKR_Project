using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Enums;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos;

namespace VKR_Node.Services
{
    /// <summary>
    /// Background service that discovers and monitors peer nodes in the network.
    /// Periodically pings known nodes and updates their status in the metadata system.
    /// </summary>
    public class PeerDiscoveryService : BackgroundService
    {
        private readonly ILogger<PeerDiscoveryService> _logger;
        private readonly INodeClient _nodeClient;
        private readonly NodeOptions _nodeOptions;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pingInterval;
        private readonly TimeSpan _pingTimeout;
        private readonly TimeSpan _initialDelay;
        private readonly int _maxConcurrentPings;
        
        // Cache last known states to reduce DB writes
        private readonly ConcurrentDictionary<string, (NodeStateCore State, DateTime LastUpdated)> _nodeStateCache = new();

        /// <summary>
        /// Creates a new instance of the PeerDiscoveryService.
        /// </summary>
        public PeerDiscoveryService(
            ILogger<PeerDiscoveryService> logger,
            INodeClient nodeClient,
            IOptions<NodeOptions> nodeOptions,
            IOptions<DhtOptions> dhtOptions,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _nodeClient = nodeClient;
            _nodeOptions = nodeOptions.Value;
            _serviceProvider = serviceProvider;

            // Get configuration or use defaults
            int pingIntervalSeconds = dhtOptions.Value?.StabilizationIntervalSeconds > 0 
                ? dhtOptions.Value.StabilizationIntervalSeconds 
                : 30;
            _pingInterval = TimeSpan.FromSeconds(pingIntervalSeconds);
            _pingTimeout = TimeSpan.FromSeconds(10);
            _initialDelay = TimeSpan.FromSeconds(5);
            _maxConcurrentPings = dhtOptions.Value?.ReplicationMaxParallelism > 0 
                ? dhtOptions.Value.ReplicationMaxParallelism 
                : 10;

            _logger.LogInformation("PeerDiscoveryService initialized for Node {NodeId}. Ping interval: {Interval}s, Timeout: {Timeout}s",
                _nodeOptions.NodeId, _pingInterval.TotalSeconds, _pingTimeout.TotalSeconds);
            
            LogKnownNodesStatus();
        }

        /// <summary>
        /// Main execution loop of the service.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PeerDiscoveryService starting execution");
            
            // Initial delay to allow other services to initialize
            try
            {
                await Task.Delay(_initialDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("PeerDiscoveryService stopped during initial delay");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExecuteDiscoveryCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("PeerDiscoveryService stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error in discovery cycle");
                }

                try
                {
                    await Task.Delay(_pingInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("PeerDiscoveryService stopping during delay");
                    break;
                }
            }
            
            _logger.LogInformation("PeerDiscoveryService execution stopped");
        }

        /// <summary>
        /// Executes a single discovery cycle, pinging all known nodes.
        /// </summary>
        private async Task ExecuteDiscoveryCycleAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting peer discovery cycle");
            var knownNodes = _nodeOptions.KnownNodes?.ToImmutableList() ?? ImmutableList<ChunkStorageNode>.Empty;

            if (knownNodes.IsEmpty)
            {
                _logger.LogWarning("No known nodes configured");
                return;
            }

            // Filter out self nodes
            var peerNodes = knownNodes
                .Where(node => !IsSelf(node.NodeId, node.Address))
                .ToList();
            
            _logger.LogDebug("Found {Count} peer nodes to ping", peerNodes.Count);

            // Use semaphore to limit concurrency
            using var semaphore = new SemaphoreSlim(_maxConcurrentPings);
            var pingTasks = new List<Task>();

            foreach (var node in peerNodes)
            {
                // Skip nodes with missing info
                if (string.IsNullOrEmpty(node.Address) || string.IsNullOrEmpty(node.NodeId))
                {
                    _logger.LogWarning("Skipping node with missing ID or address: {NodeId}", node.NodeId ?? "Unknown");
                    continue;
                }

                // Wait for a slot to become available
                await semaphore.WaitAsync(cancellationToken);

                pingTasks.Add(Task.Run(async () => {
                    try
                    {
                        await ProcessNodeAsync(node, cancellationToken);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                    {
                        _logger.LogError(ex, "Error processing node {NodeId}", node.NodeId);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            // Wait for all ping tasks to complete or cancellation
            try
            {
                await Task.WhenAll(pingTasks);
                _logger.LogDebug("Peer discovery cycle completed successfully");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Peer discovery cycle was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for ping tasks to complete");
                // Continue execution - don't rethrow as this shouldn't crash the service
            }
        }

        /// <summary>
        /// Processes a single node by pinging it and updating its status.
        /// </summary>
        private async Task ProcessNodeAsync(ChunkStorageNode node, CancellationToken cancellationToken)
        {
            try
            {
                // Create a timeout for this specific ping
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_pingTimeout);
                
                var (success, responderId) = await PingNodeAsync(node, cts.Token);
                await UpdateNodeStatusAsync(node, success, responderId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Propagate cancellation
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing node {NodeId}", node.NodeId);
                // We'll still try to update the node status as offline
                await UpdateNodeStatusAsync(node, false, "Error: " + ex.GetType().Name, cancellationToken);
            }
        }

        /// <summary>
        /// Pings a node to check if it's online.
        /// </summary>
        private async Task<(bool Success, string? ResponderId)> PingNodeAsync(ChunkStorageNode node, CancellationToken ct)
        {
            _logger.LogTrace("Pinging node {NodeId} at {Address}", node.NodeId, node.Address);
            
            try
            {
                var reply = await _nodeClient.PingNodeAsync(
                    node.Address,
                    new PingRequest { SenderNodeId = _nodeOptions.NodeId },
                    ct); // Pass cancellation token

                bool success = reply?.Success == true;
                
                _logger.LogInformation("Node {NodeId} ({Address}) - {Status}",
                    node.NodeId, node.Address, success ? "ONLINE" : "OFFLINE");

                return (success, reply?.ResponderNodeId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Ping to {NodeId} timed out", node.NodeId);
                return (false, "Timeout");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping failed for {NodeId}", node.NodeId);
                return (false, ex.GetType().Name);
            }
        }

        /// <summary>
        /// Updates a node's status in the metadata system.
        /// </summary>
        private async Task UpdateNodeStatusAsync(
            ChunkStorageNode node, 
            bool success, 
            string? responderId,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            var newState = success ? NodeStateCore.Online : NodeStateCore.Offline;
            
            // Check if state changed from cache to reduce DB writes
            bool stateChanged = true;
            if (_nodeStateCache.TryGetValue(node.NodeId, out var cachedState))
            {
                stateChanged = cachedState.State != newState || 
                              (DateTime.UtcNow - cachedState.LastUpdated) > TimeSpan.FromMinutes(5);
                
                if (!stateChanged)
                {
                    _logger.LogTrace("Skipping status update for {NodeId} - no state change", node.NodeId);
                    return;
                }
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();

                var statusInfo = new NodeStateCoreInfo
                {
                    NodeId = node.NodeId,
                    Address = node.Address,
                    State = newState,
                    LastSeen = DateTime.UtcNow,
                    LastSuccessfulPingTimestamp = success ? DateTime.UtcNow : null
                };

                await metadataManager.SaveNodeStateAsync(statusInfo, ct);
                
                // Update cache
                _nodeStateCache[node.NodeId] = (newState, DateTime.UtcNow);
                
                _logger.LogDebug("Updated {NodeId} status to {State}", node.NodeId, newState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for {NodeId}", node.NodeId);
            }
        }
        
        /// <summary>
        /// Checks if a node is this node (self).
        /// </summary>
        private bool IsSelf(string nodeId, string? targetAddress)
        {
            // ID-based check is primary and most reliable
            if (nodeId == _nodeOptions.NodeId)
                return true;
            
            // Address check is secondary
            if (string.IsNullOrEmpty(targetAddress) || string.IsNullOrEmpty(_nodeOptions.Address))
                return false;

            try
            {
                // Normalize addresses for comparison
                string selfAddrNorm = NormalizeAddress(_nodeOptions.Address);
                string targetAddrNorm = NormalizeAddress(targetAddress);
                
                return string.Equals(selfAddrNorm, targetAddrNorm, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error comparing addresses: {Self} vs {Target}", 
                    _nodeOptions.Address, targetAddress);
                return false;
            }
        }
        
        /// <summary>
        /// Normalizes an address for comparison.
        /// </summary>
        private string NormalizeAddress(string address)
        {
            // Remove scheme
            if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                address = address.Substring(7);
            else if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                address = address.Substring(8);
            
            // Handle common local addresses
            address = address.Replace("localhost:", "127.0.0.1:");
            address = address.Replace("0.0.0.0:", "127.0.0.1:");
            address = address.Replace("[::]:", "127.0.0.1:");
            
            return address.ToLowerInvariant();
        }
        
        /// <summary>
        /// Logs information about the configured known nodes.
        /// </summary>
        private void LogKnownNodesStatus()
        {
            if (_nodeOptions.KnownNodes == null || !_nodeOptions.KnownNodes.Any())
            {
                _logger.LogWarning("No known nodes configured");
                return;
            }

            // Count self-references for information
            int selfCount = _nodeOptions.KnownNodes.Count(n => IsSelf(n.NodeId, n.Address));
            
            _logger.LogInformation("Configured with {TotalNodes} known nodes ({SelfNodes} self-references)",
                _nodeOptions.KnownNodes.Count, selfCount);
            
            // Log each node for debugging
            foreach (var node in _nodeOptions.KnownNodes)
            {
                bool isSelf = IsSelf(node.NodeId, node.Address);
                _logger.LogDebug("Known node: {NodeId} at {Address} {SelfMarker}", 
                    node.NodeId, node.Address, isSelf ? "(self)" : "");
            }
        }

        /// <summary>
        /// Called when the service is stopping.
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PeerDiscoveryService shutdown initiated");
            _nodeStateCache.Clear();
            return base.StopAsync(cancellationToken);
        }
    }
}