using VKR_Node.Models;
using VKR_Node.Utilities;

namespace VKR_Node.Services;
/// <summary>
///  Implements operations like
/// Join, Lookup, Stabilize, and Fix Fingers. 
/// </summary>

public class ChordService
{
    private readonly Node _localNode;

    public ChordService(Node localNode)
    {
        _localNode = localNode;
        Logger.Info($"ChordService initialized for Node: {_localNode.Identifier}");
    }

    public void Join(Node newNode)
    {
        Logger.Info($"Processing join request for Node: {newNode.Identifier}");
        if (_localNode.Successor == null || IsBetween(newNode.Identifier, _localNode.Identifier, _localNode.Successor.Identifier))
        {
            // Update successor and predecessor links
            newNode.Successor = _localNode.Successor;
            newNode.Predecessor = _localNode;
            _localNode.Successor = newNode;

            if (newNode.Successor != null)
                newNode.Successor.Predecessor = newNode;
            Logger.Info($"Node {newNode.Identifier} joined the network successfully.");
        }
        else
        {
            ForwardJoinRequest(newNode);
        }
    }

    public Node Lookup(Guid key)
    {
        Logger.Info($"Lookup request received for Key: {key}");
        if (IsKeyInRange(key))
        {
            Logger.Info($"Key {key} is managed by Node: {_localNode.Identifier}");
            return _localNode;
        }

        Logger.Info($"Key {key} not in range. Forwarding lookup to successor.");
        return ForwardLookupRequest(key);
    }

    public void Stabilize()
    {
        Logger.Info("Running stabilization process.");
        if (_localNode.Successor != null && _localNode.Successor.Predecessor != _localNode)
        {
            _localNode.Successor.Predecessor = _localNode;
            Logger.Info($"Updated successor's predecessor to Node: {_localNode.Identifier}");
        }
    }

    private bool IsKeyInRange(Guid key)
    {
        return (_localNode.Predecessor == null || key.CompareTo(_localNode.Predecessor.Identifier) > 0) &&
               key.CompareTo(_localNode.Identifier) <= 0;
    }

    private bool IsBetween(Guid key, Guid start, Guid end)
    {
        if (start.CompareTo(end) < 0)
        {
            return key.CompareTo(start) > 0 && key.CompareTo(end) <= 0;
        }
        else
        {
            return key.CompareTo(start) > 0 || key.CompareTo(end) <= 0;
        }
    }

    private void ForwardJoinRequest(Node newNode)
    {
        // Use GRpcNodeClient to send a join request to the successor
        Logger.Info($"Forwarding join request for Node: {newNode.Identifier} to Successor.");
    }

    private Node ForwardLookupRequest(Guid key)
    {
        Logger.Info($"Forwarding lookup request for Key: {key} to Successor.");
        // GRpcNodeClient logic to forward the request
        return _localNode.Successor;
    }
}

