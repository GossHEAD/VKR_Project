using VKR_Node.Models;
using VKR_Node.Utilities;

namespace VKR_Node.Services;
using Grpc.Core;
using System.Threading.Tasks;


public class NodeServiceImpl : NodeService.NodeServiceBase
{
    /*
     * Implement the ring stabilization logic to ensure correct successor and predecessor updates.
     * Test the Join and Lookup functionality with real gRPC client calls.
     * Add error handling for edge cases (e.g., missing successor/predecessor).
     */
    private readonly Node _localNode;
    private readonly JoinQueue _joinQueue = new JoinQueue();
    private readonly ChordService _chordService;

    public NodeServiceImpl(Node localNode, ChordService chordService)
    {
        _localNode = localNode;
        _chordService = chordService;
    }

    public override async Task<JoinResponse> Join(JoinRequest request, ServerCallContext context)
    {
        _joinQueue.Enqueue(request);

        // Process join request from the queue
        var joinRequest = _joinQueue.Dequeue();
        if (joinRequest == null)
            return new JoinResponse { Message = "No pending join requests." };

        var newNode = new Node(joinRequest.Ip, joinRequest.Port);
        newNode.Predecessor = _localNode; // Set the local node as predecessor
        newNode.Successor = _localNode.Successor; // Assign the current successor as its successor

        // Update the local node's successor to the new node
        _localNode.Successor = newNode;

        return new JoinResponse
        {
            Message = "Node joined successfully.",
            SuccessorId = newNode.Successor?.Identifier.ToString(),
            PredecessorId = newNode.Predecessor?.Identifier.ToString()
        };
    }

    public override Task<LookupResponse> Lookup(LookupRequest request, ServerCallContext context)
    {
        Guid key = Guid.Parse(request.Key);

        if (IsKeyInRange(key))
        {
            return Task.FromResult(new LookupResponse
            {
                NodeId = _localNode.Identifier.ToString(),
                Ip = _localNode.Ip,
                Port = _localNode.Port,
                ChunkAvailability = string.Join(",", _localNode.StoredChunks),
                Load = _localNode.Load
            });
        }

        return ForwardLookupToSuccessor(request);
    }

    private Task<LookupResponse> ForwardLookupToSuccessor(LookupRequest request)
    {
        // Forward the request to the successor node
        // In a real implementation, gRPC client calls would be used
        return Task.FromResult(new LookupResponse
        {
            NodeId = _localNode.Successor.Identifier.ToString(),
            Ip = _localNode.Successor.Ip,
            Port = _localNode.Successor.Port
        });
    }
    
    public override Task<NodeInfo> GetSuccessor(EmptyRequest request, ServerCallContext context)
    {
        var successor = _localNode.Successor;
        return Task.FromResult(new NodeInfo
        {
            NodeId = successor?.Identifier.ToString(),
            Ip = successor?.Ip,
            Port = successor?.Port ?? 0
        });
    }

    public override Task<NodeInfo> GetPredecessor(EmptyRequest request, ServerCallContext context)
    {
        var predecessor = _localNode.Predecessor;
        return Task.FromResult(new NodeInfo
        {
            NodeId = predecessor?.Identifier.ToString(),
            Ip = predecessor?.Ip,
            Port = predecessor?.Port ?? 0
        });
    }

    public override Task<PingResponse> Ping(EmptyRequest request, ServerCallContext context)
    {
        return Task.FromResult(new PingResponse { Alive = true });
    }

    public override Task<MetricsResponse> GetNodeMetrics(EmptyRequest request, ServerCallContext context)
    {
        return Task.FromResult(new MetricsResponse
        {
            Load = _localNode.Load,
            ChunkAvailability = string.Join(",", _localNode.StoredChunks),
            Uptime = "N/A" // Placeholder for uptime
        });
    }
    private bool IsKeyInRange(Guid key)
    {
        // Check if key belongs to this node
        return (_localNode.Predecessor == null || key.CompareTo(_localNode.Predecessor.Identifier) > 0) &&
               key.CompareTo(_localNode.Identifier) <= 0;
    }
}
