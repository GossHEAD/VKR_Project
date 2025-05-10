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
        private readonly NodeIdentityOptions _nodeOptions;
        private readonly NetworkOptions _networkOptions;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _pingInterval;
        private readonly TimeSpan _pingTimeout;
        private readonly TimeSpan _initialDelay;
        private readonly int _maxConcurrentPings;
        
        private readonly ConcurrentDictionary<string, (NodeStateCore State, DateTime LastUpdated)> _nodeStateCache = new();
        
        public PeerDiscoveryService(
            ILogger<PeerDiscoveryService> logger,
            INodeClient nodeClient,
            IOptions<NodeIdentityOptions> nodeOptions,
            IOptions<DhtOptions> dhtOptions,
            IServiceProvider serviceProvider,
            IOptions<NetworkOptions> networkOptions)
        {
            _logger = logger;
            _nodeClient = nodeClient;
            _nodeOptions = nodeOptions.Value;
            _serviceProvider = serviceProvider;
            _networkOptions = networkOptions.Value;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("PeerDiscoveryService starting execution");
            
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

        private async Task ExecuteDiscoveryCycleAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting peer discovery cycle");
            var knownNodes = _networkOptions.KnownNodes?.ToImmutableList() ?? ImmutableList<KnownNodeOptions>.Empty;

            if (knownNodes.IsEmpty)
            {
                _logger.LogWarning("No known nodes configured");
                return;
            }

            var peerNodes = knownNodes
                .Where(node => !IsSelf(node.NodeId, node.Address))
                .ToList();
            
            _logger.LogDebug("Found {Count} peer nodes to ping", peerNodes.Count);

            using var semaphore = new SemaphoreSlim(_maxConcurrentPings);
            var pingTasks = new List<Task>();

            foreach (var node in peerNodes)
            {
                if (string.IsNullOrEmpty(node.Address) || string.IsNullOrEmpty(node.NodeId))
                {
                    _logger.LogWarning("Skipping node with missing ID or address: {NodeId}", node.NodeId ?? "Unknown");
                    continue;
                }

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
            }
        }

        private async Task ProcessNodeAsync(KnownNodeOptions node, CancellationToken cancellationToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_pingTimeout);
                
                var (success, responderId) = await PingNodeAsync(node, cts.Token);
                await UpdateNodeStatusAsync(node, success, responderId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing node {NodeId}", node.NodeId);
                await UpdateNodeStatusAsync(node, false, "Error: " + ex.GetType().Name, cancellationToken);
            }
        }

        private async Task<(bool Success, string? ResponderId)> PingNodeAsync(KnownNodeOptions node, CancellationToken ct)
        {
            _logger.LogTrace("Pinging node {NodeId} at {Address}", node.NodeId, node.Address);
            
            try
            {
                var reply = await _nodeClient.PingNodeAsync(
                    node.Address,
                    new PingRequest { SenderNodeId = _nodeOptions.NodeId },
                    ct); 

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

        private async Task UpdateNodeStatusAsync(
            KnownNodeOptions node, 
            bool success, 
            string? responderId,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            var newState = success ? NodeStateCore.Online : NodeStateCore.Offline;
            
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

                var statusInfo = new NodeModel
                {
                    Id = node.NodeId,
                    Address  = node.Address,
                    State = newState,
                    LastSeen = DateTime.UtcNow,
                    LastSuccessfulPingTimestamp = success ? DateTime.UtcNow : null
                };

                await metadataManager.SaveNodeStateAsync(statusInfo, ct);
                
                _nodeStateCache[node.NodeId] = (newState, DateTime.UtcNow);
                
                _logger.LogDebug("Updated {NodeId} status to {State}", node.NodeId, newState);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for {NodeId}", node.NodeId);
            }
        }
        
        private bool IsSelf(string nodeId, string? targetAddress)
        {
            if (nodeId == _nodeOptions.NodeId)
                return true;
            
            if (string.IsNullOrEmpty(targetAddress) || string.IsNullOrEmpty(_networkOptions.ListenAddress))
                return false;

            try
            {
                string selfAddrNorm = NormalizeAddress(_networkOptions.ListenAddress);
                string targetAddrNorm = NormalizeAddress(targetAddress);
                
                return string.Equals(selfAddrNorm, targetAddrNorm, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error comparing addresses: {Self} vs {Target}", 
                    _networkOptions.ListenAddress, targetAddress);
                return false;
            }
        }
        
        private string NormalizeAddress(string address)
        {
            if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                address = address.Substring(7);
            else if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                address = address.Substring(8);
            
            address = address.Replace("localhost:", "127.0.0.1:");
            address = address.Replace("0.0.0.0:", "127.0.0.1:");
            address = address.Replace("[::]:", "127.0.0.1:");
            
            return address.ToLowerInvariant();
        }
        
        private void LogKnownNodesStatus()
        {
            if (_networkOptions.KnownNodes == null || !_networkOptions.KnownNodes.Any())
            {
                _logger.LogWarning("No known nodes configured");
                return;
            }

            int selfCount = _networkOptions.KnownNodes.Count(n => IsSelf(n.NodeId, n.Address));
            
            _logger.LogInformation("Configured with {TotalNodes} known nodes ({SelfNodes} self-references)",
                _networkOptions.KnownNodes.Count, selfCount);
            
            foreach (var node in _networkOptions.KnownNodes)
            {
                bool isSelf = IsSelf(node.NodeId, node.Address);
                _logger.LogDebug("Known node: {NodeId} at {Address} {SelfMarker}", 
                    node.NodeId, node.Address, isSelf ? "(self)" : "");
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("PeerDiscoveryService shutdown initiated");
            _nodeStateCache.Clear();
            return base.StopAsync(cancellationToken);
        }
    }
}