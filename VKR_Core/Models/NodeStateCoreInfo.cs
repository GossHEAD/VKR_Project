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
    // In VKR_Core/Models/NodeStateCoreInfo.cs
    public class NodeStateCoreInfo
    {
        public required string NodeId { get; set; }
        public required string Address { get; set; }
        public NodeStateCore State { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime? LastSuccessfulPingTimestamp { get; set; }
        public long? DiskSpaceAvailableBytes { get; set; }
        public long? DiskSpaceTotalBytes { get; set; }
        public int? StoredChunkCount { get; set; }
    
        // Add new properties for CPU and memory usage
        public double? CpuUsagePercent { get; set; }
        public long? MemoryUsedBytes { get; set; }
        public long? MemoryTotalBytes { get; set; }
    }
}
