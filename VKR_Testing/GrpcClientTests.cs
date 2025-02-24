using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Grpc.Core;
using Network;
using VKR_Common.Models;
using VKR_Network;
using Assert = Xunit.Assert;

public class GrpcClientTests
{
    private readonly Mock<NodeService.NodeServiceClient> _mockClient;
    private readonly GrpcClient _client;

    public GrpcClientTests()
    {
        _mockClient = new Mock<NodeService.NodeServiceClient>();
        _client = new GrpcClient("http://localhost:5000");
    }

    private AsyncUnaryCall<T> CreateAsyncUnaryCall<T>(T response)
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response), 
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    [Fact]
    public async Task RegisterNodeAsync_ShouldReturnTrue_WhenSuccess()
    {
        var mockResponse = new RegisterNodeResponse { Success = true };

        _mockClient
            .Setup(c => c.RegisterNodeAsync(It.IsAny<RegisterNodeRequest>(), null, null, default))
            .Returns(CreateAsyncUnaryCall(mockResponse));

        var result = await _client.RegisterNodeAsync("1", "127.0.0.1", 5000);

        Assert.True(result);
    }

    [Fact]
    public async Task GetNodeListAsync_ShouldReturnNodes()
    {
        var mockResponse = new GetNodeListResponse
        {
            Nodes =
            {
                new NodeInfo { Id = "1", Address = "127.0.0.1", Port = 5000 },
                new NodeInfo { Id = "2", Address = "127.0.0.2", Port = 5001 }
            }
        };

        _mockClient
            .Setup(c => c.GetNodeListAsync(It.IsAny<GetNodeListRequest>(), null, null, default))
            .Returns(CreateAsyncUnaryCall(mockResponse));

        var nodes = await _client.GetNodeListAsync();

        Assert.NotNull(nodes);
        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.Id == "1");
        Assert.Contains(nodes, n => n.Id == "2");
    }
}