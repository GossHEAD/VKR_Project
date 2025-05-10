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
        public static List<KnownNodeOptions> SelectReplicaTargets(
            List<KnownNodeOptions> onlinePeers, 
            string chunkId, 
            int replicasNeeded)
        {
            if (onlinePeers == null || !onlinePeers.Any() || replicasNeeded <= 0)
            {
                return new List<KnownNodeOptions>();
            }
            
            if (onlinePeers.Count <= replicasNeeded)
            {
                return onlinePeers;
            }
            
            int hashCode = Math.Abs(chunkId.GetHashCode());
            int startIndex = hashCode % onlinePeers.Count;
            var selectedTargets = new List<KnownNodeOptions>();
            
            for (int i = 0; i < replicasNeeded; i++)
            {
                selectedTargets.Add(onlinePeers[(startIndex + i) % onlinePeers.Count]);
            }
            
            return selectedTargets;
        }
        
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