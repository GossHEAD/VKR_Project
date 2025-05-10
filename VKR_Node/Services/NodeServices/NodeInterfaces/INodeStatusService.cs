using Grpc.Core;
using VKR.Protos;

namespace VKR_Node.Services.NodeServices.NodeInterfaces;

public interface INodeStatusService
{
    Task<GetNodeStatusesReply> GetNodeStatuses(GetNodeStatusesRequest request, ServerCallContext context);
    Task<PingReply> PingNode(PingRequest request, ServerCallContext context);
}