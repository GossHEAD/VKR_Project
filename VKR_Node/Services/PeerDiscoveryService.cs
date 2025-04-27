using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Enums;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos;

namespace VKR_Node.Services;

public class PeerDiscoveryService : BackgroundService
{
    private readonly ILogger<PeerDiscoveryService> _logger;
    private readonly INodeClient _nodeClient;
    private readonly NodeOptions _nodeOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _pingInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _pingTimeout = TimeSpan.FromSeconds(10);
    
    // Cache last known states to reduce DB writes
    private readonly ConcurrentDictionary<string, NodeStateCore> _nodeStateCache = new();

    public PeerDiscoveryService(
        ILogger<PeerDiscoveryService> logger,
        INodeClient nodeClient,
        IOptions<NodeOptions> nodeOptions,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _nodeClient = nodeClient;
        _nodeOptions = nodeOptions.Value;
        _serviceProvider = serviceProvider;

        _logger.LogInformation("PeerDiscoveryService initialized for Node {NodeId}", _nodeOptions.NodeId);
        LogKnownNodesStatus();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PeerDiscoveryService starting execution");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteDiscoveryCycleAsync(stoppingToken);
                await Task.Delay(_pingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("PeerDiscoveryService stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error in discovery cycle");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ExecuteDiscoveryCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting peer discovery cycle");
        var knownNodes = _nodeOptions.KnownNodes?.ToImmutableList() ?? ImmutableList<ChunkStorageNode>.Empty;

        if (knownNodes.IsEmpty)
        {
            _logger.LogWarning("No known nodes configured");
            return;
        }

        var pingTasks = knownNodes
            .Where(node => !IsSelf(node.Address))
            .Select(node => ProcessNodeAsync(node, cancellationToken))
            .ToList();

        await ExecuteWithTimeout(pingTasks, cancellationToken);
        _logger.LogDebug("Peer discovery cycle completed");
    }

    private async Task ProcessNodeAsync(ChunkStorageNode node, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_pingTimeout);
            
            var (success, responderId) = await PingNodeAsync(node, cts.Token);
            await UpdateNodeStatusAsync(node, success, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error processing node {NodeId}", node.NodeId);
        }
    }

    private async Task<(bool Success, string? ResponderId)> PingNodeAsync(ChunkStorageNode node, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(node.Address) || string.IsNullOrEmpty(node.NodeId))
        {
            _logger.LogWarning("Invalid node configuration: {NodeId}", node.NodeId);
            return (false, null);
        }

        try
        {
            var reply = await _nodeClient.PingNodeAsync(
                node.Address,
                new PingRequest { SenderNodeId = _nodeOptions.NodeId }
                );//ct

            _logger.LogInformation("Node {NodeId} ({Address}) - {Status}",
                node.NodeId, node.Address, reply.Success ? "ONLINE" : "OFFLINE");

            return (reply.Success, reply.ResponderNodeId);
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

    private async Task UpdateNodeStatusAsync(ChunkStorageNode node, bool success, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var newState = success ? NodeStateCore.Online : NodeStateCore.Offline;
        if (_nodeStateCache.TryGetValue(node.NodeId, out var previousState) && previousState == newState)
        {
            _logger.LogTrace("Skipping status update for {NodeId} - no state change", node.NodeId);
            return;
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
            _nodeStateCache[node.NodeId] = newState;
            _logger.LogDebug("Updated {NodeId} status to {State}", node.NodeId, newState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for {NodeId}", node.NodeId);
        }
    }
    
    private bool IsSelf(string? targetAddress)
    {
        if (string.IsNullOrEmpty(targetAddress))
            return false;
        if (string.IsNullOrEmpty(_nodeOptions.Address)) return false;

        try
        {
            var selfUri = new Uri(_nodeOptions.Address);
            var targetUri = new Uri(targetAddress);

            if (selfUri.Port != targetUri.Port) return false;

            var selfIps = Dns.GetHostAddresses(selfUri.Host);
            var targetIps = Dns.GetHostAddresses(targetUri.Host);
            return selfIps.Intersect(targetIps).Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error comparing addresses: {Self} vs {Target}", 
                _nodeOptions.Address, targetAddress);
            return false;
        }
    }
    
    private async Task ExecuteWithTimeout(List<Task> tasks, CancellationToken ct)
    {
        try
        {
            await Task.WhenAll(tasks.Select(t => 
                t.ContinueWith(_ => { }, ct)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during parallel ping execution");
        }
    }

    private void LogKnownNodesStatus()
    {
        if (_nodeOptions.KnownNodes == null || !_nodeOptions.KnownNodes.Any())
        {
            _logger.LogWarning("No known nodes configured");
            return;
        }

        var selfNodes = _nodeOptions.KnownNodes.Count(n => IsSelf(n.Address));
        _logger.LogInformation("Configured {TotalNodes} nodes ({SelfNodes} self-references)",
            _nodeOptions.KnownNodes.Count, selfNodes);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PeerDiscoveryService shutdown initiated");
        _nodeStateCache.Clear();
        return base.StopAsync(cancellationToken);
    }
}