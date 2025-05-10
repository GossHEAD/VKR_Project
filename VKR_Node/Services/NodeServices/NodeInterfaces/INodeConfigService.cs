using Grpc.Core;
using VKR.Protos;

namespace VKR_Node.Services.NodeServices.NodeInterfaces;

public interface INodeConfigService
{
    Task<GetNodeConfigurationReply> GetNodeConfiguration(GetNodeConfigurationRequest request, ServerCallContext context);
}