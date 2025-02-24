// GrpcServer.cs
using Grpc.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Network;
using VKR_Common.Interfaces;
using VKR_Common.Models;

public class GrpcServer : NodeService.NodeServiceBase
{
    private readonly INodeManager _nodeManager;
    private readonly IDataStorage _dataStorage;

    public GrpcServer(INodeManager nodeManager, IDataStorage dataStorage)
    {
        _nodeManager = nodeManager;
        _dataStorage = dataStorage;
    }

    public override async Task<RegisterNodeResponse> RegisterNode(RegisterNodeRequest request, ServerCallContext context)
    {
        var node = new Node
        {
            Id = request.Id,
            Address = request.Address,
            Port = request.Port
        };

        await _nodeManager.AddNodeAsync(node);

        return new RegisterNodeResponse { Success = true };
    }

    public override async Task<GetNodeListResponse> GetNodeList(GetNodeListRequest request, ServerCallContext context)
    {
        var nodes = await _nodeManager.GetAllNodesAsync();
        var response = new GetNodeListResponse();
        response.Nodes.AddRange(nodes.Select(n => new NodeInfo
        {
            Id = n.Id,
            Address = n.Address,
            Port = n.Port
        }));
        return response;
    }

    public override async Task<SendDataResponse> SendData(SendDataRequest request, ServerCallContext context)
    {
        var data = request.Data.ToByteArray();
        await _dataStorage.SaveFileAsync(request.TargetNodeId, data);

        return new SendDataResponse { Success = true };
    }
}