using Grpc.Core;
using VKR.Protos;

namespace VKR_Node.Services.NodeServices.NodeInterfaces;

/// <summary>
/// Service for discovering and communicating with peer nodes.
/// </summary>
public interface INodeConfigService
{
    Task<GetNodeConfigurationReply> GetNodeConfiguration(GetNodeConfigurationRequest request, ServerCallContext context);
}