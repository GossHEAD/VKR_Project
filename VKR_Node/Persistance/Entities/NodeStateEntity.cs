using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VKR_Core.Enums; // For NodeStateCore enum mapping

namespace VKR.Node.Persistance.Entities
{
    /// <summary>
    /// Represents the persisted state or information about a node (self or peer) in the network.
    /// Maps to the 'NodeStates' table. Useful for DHT, node discovery, or caching status.
    /// </summary>
    [Table("NodeStates")]
    public class NodeStateEntity // Ensure class name matches if you adjusted the original
    {
        /// <summary>
        /// Unique identifier for the node. Primary Key.
        /// </summary>
        [Key]
        [Required]
        public required string NodeId { get; set; }

        /// <summary>
        /// Network address (e.g., "host:port" or "ip:port") of the node.
        /// Should be resolvable/connectable.
        /// </summary>
        [Required]
        public required string Address { get; set; }

        /// <summary>
        /// Last known state of the node, stored as an integer.
        /// Maps to the VKR.Core.Enums.NodeStateCore enum.
        /// </summary>
        [Required]
        public int State { get; set; } // Stored as int, mapped from/to NodeStateCore enum

        /// <summary>
        /// Timestamp of the last successful contact or status update (UTC).
        /// </summary>
        public DateTime LastSeen { get; set; }

        // --- NEW PROPERTIES ---

        /// <summary>
        /// Timestamp of the last successful ping response received from this node (UTC).
        /// </summary>
        public DateTime? LastSuccessfulPingTimestamp { get; set; }

        /// <summary>
        /// Estimated available disk space in bytes on the node's storage volume. Null if unknown.
        /// </summary>
        public long? DiskSpaceAvailableBytes { get; set; }

        /// <summary>
        /// Estimated total disk space in bytes on the node's storage volume. Null if unknown.
        /// </summary>
        public long? DiskSpaceTotalBytes { get; set; }

        /// <summary>
        /// A metric representing the current load or activity level (e.g., number of active transfers, stored chunks). Null if unknown.
        /// Definition of "load" needs to be decided. Let's use stored chunk count for now.
        /// </summary>
        public int? StoredChunkCount { get; set; }

        // --- Optional DHT Related Fields (Example - Kept from original) ---
        // public string? PredecessorId { get; set; }
        // public string? PredecessorAddress { get; set; }
        // public string? FingerTableJson { get; set; }
    }
}
