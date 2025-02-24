using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Network;
using Xunit;
using Assert = NUnit.Framework.Assert;

namespace VKR_Testing;

public class GrpcClientIntegrationTests
{
    private readonly TestServer _testServer;
    private readonly GrpcChannel _channel;

    public GrpcClientIntegrationTests()
    {
        _testServer = new TestServer(new WebHostBuilder().UseStartup<Startup>());
        _channel = GrpcChannel.ForAddress(_testServer.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = _testServer.CreateHandler()
        });
    }

    [Fact]
    public async Task RegisterNodeAsync_ShouldReturnTrue_WhenServerIsRunning()
    {
        var client = new NodeService.NodeServiceClient(_channel);
        var response = await client.RegisterNodeAsync(new RegisterNodeRequest
        {
            Id = "1",
            Address = "127.0.0.1",
            Port = 5000
        });

        Assert.True(response.Success);
    }
}