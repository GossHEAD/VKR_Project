using VKR_Core.Enums;

namespace VKR_Core.Models.Interfaces;

/// <summary>
/// Defines the core properties of a node in the network.
/// </summary>
public interface INode
{
    string Id { get; }
    string Address { get; }
    NodeStateCore State { get; }
    DateTime LastSeen { get; }
}