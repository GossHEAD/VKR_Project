using Microsoft.Extensions.Logging;
using VKR_Core.Models;
using VKR_Node.Configuration;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using VKR.Protos;
using Google.Protobuf.WellKnownTypes;
using VKR_Core.Enums;
using VKR_Core.Services;

namespace VKR_Node.Services.Utilities
{
    /// <summary>
    /// Utility class containing common replication functions shared across services
    /// </summary>
    public static class ReplicationUtility
    {
        /// <summary>
        /// Selects appropriate nodes to replicate data to based on chunk ID and available nodes
        /// </summary>
        public static List<KnownNodeOptions> SelectReplicaTargets(
            List<KnownNodeOptions> onlinePeers, 
            string chunkId, 
            int replicasNeeded)
        {
            if (onlinePeers == null || !onlinePeers.Any() || replicasNeeded <= 0)
            {
                return new List<KnownNodeOptions>();
            }
            
            // If we don't have enough peers, return all we have
            if (onlinePeers.Count <= replicasNeeded)
            {
                return onlinePeers;
            }
            
            // Use consistent hashing to distribute chunks evenly
            int hashCode = Math.Abs(chunkId.GetHashCode());
            int startIndex = hashCode % onlinePeers.Count;
            var selectedTargets = new List<KnownNodeOptions>();
            
            for (int i = 0; i < replicasNeeded; i++)
            {
                selectedTargets.Add(onlinePeers[(startIndex + i) % onlinePeers.Count]);
            }
            
            return selectedTargets;
        }

        /// <summary>
        /// Maps a core metadata object to a protocol buffer metadata object
        /// </summary>
        public static FileMetadata? MapCoreToProtoMetadata(FileModel? core)
        {
            if (core == null) return null;

            return new FileMetadata
            {
                FileId = core.FileId,
                FileName = core.FileName ?? string.Empty,
                FileSize = core.FileSize,
                CreationTime = Timestamp.FromDateTime(core.CreationTime.ToUniversalTime()),
                ModificationTime = Timestamp.FromDateTime(core.ModificationTime.ToUniversalTime()),
                ContentType = core.ContentType ?? string.Empty,
                ChunkSize = core.ChunkSize,
                TotalChunks = core.TotalChunks,
                State = (FileState)core.State
            };
        }

        /// <summary>
        /// Checks if a node is online by pinging it
        /// </summary>
        public static async Task<bool> IsNodeOnlineAsync(
            string nodeId, 
            string nodeAddress,
            string senderNodeId,
            INodeClient nodeClient,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(nodeAddress))
            {
                logger.LogWarning("Cannot check online status for Node {NodeId}: No address provided", nodeId);
                return false;
            }

            try
            {
                var pingRequest = new PingRequest { SenderNodeId = senderNodeId };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));

                var reply = await nodeClient.PingNodeAsync(nodeAddress, pingRequest, cts.Token);
                var isOnline = reply?.Success ?? false;
                
                logger.LogTrace("Ping result for Node {NodeId}: Online={Status}", nodeId, isOnline);
                return isOnline;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Ping failed for node {NodeId} ({Address})", nodeId, nodeAddress);
                return false;
            }
        }
    }
}