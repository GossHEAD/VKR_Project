using System.Threading.Tasks;
using Xunit;
using VKR_Common.Models;
using VKR_Core;
using VKR_Core.Services;
using Assert = Xunit.Assert;

public class NodeManagerTests
{
    private readonly NodeManager _nodeManager;

    public NodeManagerTests()
    {
        _nodeManager = new NodeManager();
    }

    [Fact]
    public async Task AddNode_ShouldAddNodeSuccessfully()
    {
        var node = new Node { Id = "1", Address = "127.0.0.1", Port = 5000 };
        await _nodeManager.AddNodeAsync(node);

        var nodes = await _nodeManager.GetAllNodesAsync();
        Assert.Contains(nodes, n => n.Id == "1");
    }

    [Fact]
    public async Task RemoveNode_ShouldRemoveNodeSuccessfully()
    {
        var node = new Node { Id = "1", Address = "127.0.0.1", Port = 5000 };
        await _nodeManager.AddNodeAsync(node);
        await _nodeManager.RemoveNodeAsync("1");

        var nodes = await _nodeManager.GetAllNodesAsync();
        Assert.DoesNotContain(nodes, n => n.Id == "1");
    }

    [Fact]
    public async Task NodeExists_ShouldReturnTrueIfExists()
    {
        var node = new Node { Id = "1", Address = "127.0.0.1", Port = 5000 };
        await _nodeManager.AddNodeAsync(node);

        var exists = await _nodeManager.NodeExistsAsync("1");
        Assert.True(exists);
    }
}