using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Grpc.Core;
using Network;
using VKR_Common.Interfaces;
using VKR_Core;
using VKR_Network;
using VKR_Common.Models;
using Assert = Xunit.Assert;

public class GrpcServerTests
{
    private readonly GrpcServer _server;
    private readonly Mock<INodeManager> _mockNodeManager;
    private readonly Mock<IDataStorage> _mockDataStorage;

    public GrpcServerTests()
    {
        _mockNodeManager = new Mock<INodeManager>();
        _mockDataStorage = new Mock<IDataStorage>();
        _server = new GrpcServer(_mockNodeManager.Object, _mockDataStorage.Object);
    }

    [Fact]
    public async Task RegisterNode_ShouldAddNodeSuccessfully()
    {
        var request = new RegisterNodeRequest { Id = "1", Address = "127.0.0.1", Port = 5000 };
        var context = new Mock<ServerCallContext>();

        _mockNodeManager.Setup(m => m.AddNodeAsync(It.IsAny<Node>()))
            .Returns(Task.CompletedTask);

        var response = await _server.RegisterNode(request, context.Object);

        Assert.True(response.Success);
        _mockNodeManager.Verify(m => m.AddNodeAsync(It.Is<Node>(n => n.Id == "1")), Times.Once);
    }

    [Fact]
    public async Task GetNodeList_ShouldReturnNodeList()
    {
        var nodes = new List<Node>
        {
            new Node { Id = "1", Address = "127.0.0.1", Port = 5000 },
            new Node { Id = "2", Address = "127.0.0.2", Port = 5001 }
        };

        _mockNodeManager.Setup(m => m.GetAllNodesAsync())
            .ReturnsAsync(nodes);

        var context = new Mock<ServerCallContext>();
        var response = await _server.GetNodeList(new GetNodeListRequest(), context.Object);

        Assert.Equal(2, response.Nodes.Count);
        Assert.Contains(response.Nodes, n => n.Id == "1");
    }
}