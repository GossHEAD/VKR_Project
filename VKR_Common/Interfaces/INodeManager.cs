using VKR_Common.Models;

namespace VKR_Common.Interfaces;

public interface INodeManager
{
    Task AddNodeAsync(Node node);
    Task RemoveNodeAsync(string nodeId);
    Task<List<Node>> GetAllNodesAsync();
    Task<bool> NodeExistsAsync(string nodeId);
    Task UpdateNodeStatusAsync(string nodeId, float cpu, float memory, float network);
    Task AssignSuperNodeAsync();
    string? GetSuperNodeId();

}