using Microsoft.Extensions.Logging;
using VKR_Node.Configuration;

namespace VKR_Node.Services.Utilities;

public static class NodeSelectionHelper
{
    public static List<KnownNodeOptions> SelectReplicaTargets(
        List<KnownNodeOptions> onlinePeers,
        string chunkId,
        int replicasNeeded,
        ILogger logger)
    {
        if (onlinePeers == null || !onlinePeers.Any() || replicasNeeded <= 0) return new List<KnownNodeOptions>();
        var orderedPeers = onlinePeers.OrderBy(p => p.NodeId).ToList();
        if (orderedPeers.Count <= replicasNeeded) return orderedPeers;
        int hashCode = Math.Abs(chunkId.GetHashCode()); int startIndex = hashCode % orderedPeers.Count;
        var selectedTargets = new List<KnownNodeOptions>();
        for (int i = 0; i < replicasNeeded; i++) { selectedTargets.Add(orderedPeers[(startIndex + i) % orderedPeers.Count]); }
        logger.LogDebug("Selected {Count} replica targets for Chunk {Id}: {Targets}", selectedTargets.Count, chunkId, string.Join(", ", selectedTargets.Select(n => n.NodeId)));
        return selectedTargets;
    }

    public static bool IsSelfNode(
        string nodeId, 
        string? targetAddress, 
        string localNodeId, 
        string? localAddress, 
        ILogger logger)
    {
        // ID-based check is primary and most reliable
        if (nodeId == localNodeId)
            return true;
        
        // Address check is secondary
        if (string.IsNullOrEmpty(targetAddress) || string.IsNullOrEmpty(localAddress))
            return false;

        try
        {
            // Normalize addresses for comparison
            string selfAddrNorm = NormalizeAddress(localAddress);
            string targetAddrNorm = NormalizeAddress(targetAddress);
            
            return string.Equals(selfAddrNorm, targetAddrNorm, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error comparing addresses: {Self} vs {Target}", 
                localAddress, targetAddress);
            return false;
        }
    }

    /// <summary>
    /// Normalizes an address to a standard format for comparison
    /// </summary>
    private static string NormalizeAddress(string address)
    {
        // Remove scheme
        if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            address = address.Substring(7);
        else if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            address = address.Substring(8);
        
        // Handle common local addresses
        address = address.Replace("localhost:", "127.0.0.1:");
        address = address.Replace("0.0.0.0:", "127.0.0.1:");
        address = address.Replace("[::]:", "127.0.0.1:");
        
        return address.ToLowerInvariant();
    }
}