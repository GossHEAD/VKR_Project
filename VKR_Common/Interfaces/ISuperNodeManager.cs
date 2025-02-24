using VKR_Common.Models;

namespace VKR_Common.Interfaces;

public interface ISuperNodeManager
{
    string? SuperNodeId { get; }
    bool IsSuperNode { get; }
    Task AssignNewSuperNodeAsync();
    Task RegisterSuperNodeAsync(Node node);
    Task<bool> IsSuperNodeAliveAsync();
    Task NotifySuperNodeChangeAsync(Node newSuperNode);
}
