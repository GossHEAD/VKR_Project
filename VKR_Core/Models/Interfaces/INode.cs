using VKR_Core.Enums;

namespace VKR_Core.Models.Interfaces;

public interface INode
{
    string Id { get; }
    string Address { get; }
    NodeStateCore State { get; }
    DateTime LastSeen { get; }
}