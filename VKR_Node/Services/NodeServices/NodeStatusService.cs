using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Services.NodeServices.NodeInterfaces;
using VKR_Node.Services.Utilities;
using VKR.Protos;

namespace VKR_Node.Services.NodeServices;

// NodeStatusService.cs
public class NodeStatusService : INodeStatusService
{
    private readonly ILogger<NodeStatusService> _logger;
    private readonly NodeOptions _nodeOptions;
    private readonly INodeClient _nodeClient;
    
    private readonly string _localNodeId;

    public NodeStatusService(
        ILogger<NodeStatusService> logger,
        IOptions<NodeOptions> nodeOptions,
        INodeClient nodeClient)
    {
        _logger = logger;
        _nodeOptions = nodeOptions.Value;
        _nodeClient = nodeClient;
        _localNodeId = _nodeOptions.NodeId ?? throw new InvalidOperationException("NodeId is not configured.");
    }

    public async Task<GetNodeStatusesReply> GetNodeStatuses(GetNodeStatusesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetNodeStatuses request received from {Peer}", context.Peer);
        var reply = new GetNodeStatusesReply();

        try
        {
            // Add self status
            reply.Nodes.Add(new NodeStatusInfo
            {
                NodeId = _localNodeId,
                Address = _nodeOptions.Address ?? "Unknown",
                Status = NodeState.Online,
                Details = "Online (Self)"
            });

            await GetPeerStatusesAsync(reply, context.CancellationToken);

            // Sort nodes for consistent display
            var sortedNodes = reply.Nodes.ToList();
            sortedNodes.Sort((a, b) => string.Compare(a.NodeId, b.NodeId, StringComparison.OrdinalIgnoreCase));
            reply.Nodes.Clear();
            reply.Nodes.AddRange(sortedNodes);

            _logger.LogInformation("Returning status for {NodeCount} nodes.", reply.Nodes.Count);
            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving node statuses");
            throw new RpcException(new Status(StatusCode.Internal, $"Internal error: {ex.Message}"));
        }
    }

    private async Task GetPeerStatusesAsync(
        GetNodeStatusesReply reply,
        CancellationToken cancellationToken)
    {
        var knownNodes = _nodeOptions.KnownNodes ?? new List<ChunkStorageNode>();
        var pingTasks = new List<Task<NodeStatusInfo>>();

        foreach (var node in knownNodes)
        {
            if (node.NodeId == _localNodeId || NodeSelectionHelper.IsSelfNode(
                node.NodeId, node.Address, _localNodeId, _nodeOptions.Address ?? "", _logger))
                continue;

            pingTasks.Add(PingNodeAsync(node, cancellationToken));
        }

        var results = await Task.WhenAll(pingTasks);
        reply.Nodes.AddRange(results);
    }

    private async Task<NodeStatusInfo> PingNodeAsync(
        ChunkStorageNode node,
        CancellationToken cancellationToken)
    {
        NodeStatusInfo statusInfo;
        try
        {
            _logger.LogDebug("Pinging node {Id} at {Addr}", node.NodeId, node.Address);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            
            var pingReply = await _nodeClient.PingNodeAsync(
                node.Address, 
                new PingRequest { SenderNodeId = _localNodeId }, 
                cts.Token);
                
            statusInfo = new NodeStatusInfo
            {
                NodeId = node.NodeId,
                Address = node.Address,
                Status = pingReply.Success ? NodeState.Online : NodeState.Offline,
                Details = pingReply.Success ? "Online (Responded)" : $"Offline ({pingReply.ResponderNodeId ?? "No Response"})"
            };
            
            _logger.LogDebug("Ping result for {Id}: {Status} - {Details}", 
                node.NodeId, statusInfo.Status, statusInfo.Details);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ping timed out for {Id} at {Addr}", node.NodeId, node.Address);
            statusInfo = new NodeStatusInfo
            {
                NodeId = node.NodeId,
                Address = node.Address,
                Status = NodeState.Offline,
                Details = "Offline (Timeout)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pinging {Id} at {Addr}", node.NodeId, node.Address);
            statusInfo = new NodeStatusInfo
            {
                NodeId = node.NodeId,
                Address = node.Address,
                Status = NodeState.Error,
                Details = $"Error ({ex.GetType().Name})"
            };
        }
        
        return statusInfo;
    }
    
    public Task<PingReply> PingNode(PingRequest request, ServerCallContext context)
    {
        _logger.LogDebug("Node {NodeId} received Ping request from Node {SenderNodeId}.",
            _localNodeId, request.SenderNodeId ?? "Unknown");
            
        var reply = new PingReply
        {
            ResponderNodeId = _localNodeId,
            Success = true
        };
        
        return Task.FromResult(reply);
    }
}