using Grpc.Core;
using VKR.Protos;

namespace VKR_Node.Services.NodeServices.NodeInterfaces;

/// <summary>
/// Service for retrieving node status and configuration information.
/// </summary>
public interface INodeStatusService
{
    Task<GetNodeStatusesReply> GetNodeStatuses(GetNodeStatusesRequest request, ServerCallContext context);
    Task<PingReply> PingNode(PingRequest request, ServerCallContext context);
}