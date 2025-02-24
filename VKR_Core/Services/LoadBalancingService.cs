using VKR_Common.Interfaces;
using VKR_Common.Models;

namespace VKR_Core.Services;

public class LoadBalancingService
{
    private readonly INodeManager _nodeManager;

    public LoadBalancingService(INodeManager nodeManager)
    {
        _nodeManager = nodeManager;
    }

    public async Task<Node> GetBestNodeAsync()
    {
        var nodes = await _nodeManager.GetAllNodesAsync();
        return nodes.OrderBy(n => n.CpuLoad + n.MemoryUsage + n.NetworkLoad).FirstOrDefault();
    }
}