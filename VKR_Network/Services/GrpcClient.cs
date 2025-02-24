// GrpcClient.cs
using Grpc.Net.Client;
using System.Threading.Tasks;
using VKR_Common.Models;
using System.Linq;
using Network;

public class GrpcClient
{
    private readonly NodeService.NodeServiceClient _client;

    public GrpcClient(string serverAddress)
    {
        var channel = GrpcChannel.ForAddress(serverAddress);
        _client = new NodeService.NodeServiceClient(channel);
    }

    public async Task<bool> RegisterNodeAsync(string Ids, string address, int port)
    {
        var request = new RegisterNodeRequest
        {
            Id = Ids,
            Address = address,
            Port = port
        };

        var response = await _client.RegisterNodeAsync(request);
        return response.Success;
    }

    public async Task<List<Node>> GetNodeListAsync()
    {
        var response = await _client.GetNodeListAsync(new GetNodeListRequest());
        return response.Nodes.Select(n => new Node
        {
            Id = n.Id,
            Address = n.Address,
            Port = n.Port
        }).ToList();
    }

    public async Task<bool> SendDataAsync(string targetNodeId, byte[] data)
    {
        var request = new SendDataRequest
        {
            TargetNodeId = targetNodeId,
            Data = Google.Protobuf.ByteString.CopyFrom(data)
        };

        var response = await _client.SendDataAsync(request);
        return response.Success;
    }
}