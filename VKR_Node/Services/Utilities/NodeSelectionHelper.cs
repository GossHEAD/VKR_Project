using Microsoft.Extensions.Logging;
using VKR_Node.Configuration;

namespace VKR_Node.Services.Utilities;

public static class NodeSelectionHelper
{
    public static bool IsSelfNode(
        string nodeId, 
        string? targetAddress, 
        string localNodeId, 
        string? localAddress, 
        ILogger logger)
    {
        if (nodeId == localNodeId)
            return true;
        
        if (string.IsNullOrEmpty(targetAddress) || string.IsNullOrEmpty(localAddress))
            return false;

        try
        {
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

    private static string NormalizeAddress(string address)
    {
        if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            address = address.Substring(7);
        else if (address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            address = address.Substring(8);
        
        address = address.Replace("localhost:", "127.0.0.1:");
        address = address.Replace("0.0.0.0:", "127.0.0.1:");
        address = address.Replace("[::]:", "127.0.0.1:");
        
        return address.ToLowerInvariant();
    }
}