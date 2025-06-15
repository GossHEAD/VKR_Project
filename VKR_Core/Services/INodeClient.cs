using Grpc.Core;
using VKR.Protos;

namespace VKR_Core.Services
{
    public interface INodeClient : IDisposable 
    {
        Task<ReplicateChunkReply> ReplicateChunkToNodeStreamingAsync(
            string targetNodeAddress,
            ReplicateChunkMetadata metadata,
            Stream dataStream,
            CancellationToken cancellationToken = default);

        Task<DeleteChunkReply> DeleteChunkOnNodeAsync(string targetNodeAddress, DeleteChunkRequest request, CancellationToken cancellationToken = default); 
        
        Task<PingReply> PingNodeAsync(string targetNodeAddress, PingRequest request, CancellationToken cancellationToken = default); 
        
        Task<AsyncServerStreamingCall<RequestChunkReply>?> RequestChunkFromNodeAsync(string targetNodeAddress, RequestChunkRequest request, CancellationToken cancellationToken = default);
        
        Task<GetNodeFileListReply?> GetNodeFileListAsync(string targetNodeAddress, GetNodeFileListRequest request, CancellationToken cancellationToken = default);
        
        Task AcknowledgeReplicaAsync(string targetNodeAddress, AcknowledgeReplicaRequest request, CancellationToken cancellationToken = default);
    }
}