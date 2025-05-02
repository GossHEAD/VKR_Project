using VKR_Core.Extensions;
using VKR_Core.Models;
using VKR_Node.Persistance.Entities;

namespace VKR_Node.Services.Utilities.Mappers;

public static class NodeMapper
    {
        public static NodeModel ToCore(NodeEntity entity)
        {
            return new NodeModel
            {
                Id = entity.NodeId,
                Address = entity.Address,
                State = entity.State.ToNodeStateCore(), // Convert int to enum
                LastSeen = entity.LastSeen,
                LastSuccessfulPingTimestamp = entity.LastSuccessfulPingTimestamp,
                DiskSpaceAvailableBytes = entity.DiskSpaceAvailableBytes,
                DiskSpaceTotalBytes = entity.DiskSpaceTotalBytes,
                StoredChunkCount = entity.StoredChunkCount,
                CpuUsagePercent = null, // Not in entity
                MemoryUsedBytes = null, // Not in entity
                MemoryTotalBytes = null // Not in entity
            };
        }
    
        public static NodeEntity ToEntity(NodeModel core)
        {
            return new NodeEntity
            {
                NodeId = core.Id,
                Address = core.Address,
                State = core.State.ToInt(), // Convert enum to int
                LastSeen = core.LastSeen,
                LastSuccessfulPingTimestamp = core.LastSuccessfulPingTimestamp,
                DiskSpaceAvailableBytes = core.DiskSpaceAvailableBytes,
                DiskSpaceTotalBytes = core.DiskSpaceTotalBytes,
                StoredChunkCount = core.StoredChunkCount
                // Missing CPU/Memory fields that are in core but not entity
            };
        }
    }