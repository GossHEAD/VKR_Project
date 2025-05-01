using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace VKR_Node.Configuration;

public class ChunkStorageNode
{
    /// <summary>
    /// The unique identifier of the node.
    /// </summary>
    [Required(ErrorMessage = "NodeId is required for a ChunkStorageNode.")]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// The network address (IP/hostname and port) used to connect to this node's gRPC services.
    /// Example: "node1.example.com:5001", "192.168.1.101:5001"
    /// </summary>
    [Required(ErrorMessage = "NodeAddress is required for a ChunkStorageNode.")]
    public string Address { get; set; } = string.Empty;
}