using VKR_Node.Models;
using VKR_Node.Utilities;

namespace VKR_Node.Services;
/// <summary>
/// Manages local state,
/// including keys and chunks stored on the node.
/// </summary>

public class NodeStateManager
{
    private readonly Node _localNode;

    public NodeStateManager(Node localNode)
    {
        _localNode = localNode;
        Logger.Info($"NodeStateManager initialized for Node: {_localNode.Identifier}");
    }

    public void AddKey(Key key)
    {
        Logger.Info($"Adding Key: {key.Identifier} to Node: {_localNode.Identifier}");
        _localNode.StoredKeys.Add(key);
        UpdateLoad();
    }

    public void RemoveKey(Key key)
    {
        Logger.Info($"Removing Key: {key.Identifier} from Node: {_localNode.Identifier}");
        _localNode.StoredKeys.Remove(key);
        UpdateLoad();
    }

    public void UpdateSuccessor(Node successor)
    {
        Logger.Info($"Updating successor for Node: {_localNode.Identifier} to {successor.Identifier}");
        _localNode.Successor = successor;
    }

    public void UpdatePredecessor(Node predecessor)
    {
        Logger.Info($"Updating predecessor for Node: {_localNode.Identifier} to {predecessor.Identifier}");
        _localNode.Predecessor = predecessor;
    }

    private void UpdateLoad()
    {
        _localNode.Load = _localNode.StoredKeys.Count;
        Logger.Info($"Node {_localNode.Identifier} Load updated to {_localNode.Load}");
    }
}

