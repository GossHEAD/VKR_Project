using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VKR_Core.Enums; // For NodeStateCore enum mapping

namespace VKR_Core.Models
{
    /// <summary>
    /// Data Transfer Object representing the state and characteristics of a node,
    /// suitable for use in Core interfaces and inter-node communication context.
    /// </summary>
    public class NodeStateCoreInfo
    {
        public required string NodeId { get; set; }
        public required string Address { get; set; }
        public NodeStateCore State { get; set; } // Use the Core Enum
        public DateTime LastSeen { get; set; }

        // --- NEW PROPERTIES (matching NodeStateEntity additions) ---
        public DateTime? LastSuccessfulPingTimestamp { get; set; }
        public long? DiskSpaceAvailableBytes { get; set; }
        public long? DiskSpaceTotalBytes { get; set; }
        public int? StoredChunkCount { get; set; }

        // Add other properties as needed (e.g., CPU/Memory if implemented)
    }
}
