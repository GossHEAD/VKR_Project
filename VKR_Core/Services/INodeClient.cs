using Grpc.Core;
using VKR_Core.Models;
using VKR.Protos;

namespace VKR_Core.Services
{
    /// <summary>
    /// Абстрагирует вызовы RPC к другим узлам сети.
    /// Реализация будет использовать сгенерированный gRPC клиент.
    /// </summary>
    public interface INodeClient : IDisposable // Add IDisposable if GrpcNodeClient implements it
    {
        /// <summary>
        /// Sends a request to replicate a chunk to a specified target node.
        /// </summary>
        /// <param name="targetNodeAddress">The address (e.g., "host:port") of the target node.</param>
        /// <param name="request">The replication request details.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param> // Add cancellation token param doc
        /// <returns>The reply from the target node.</returns>
        Task<ReplicateChunkReply> ReplicateChunkToNodeAsync(string targetNodeAddress, ReplicateChunkRequest request, CancellationToken cancellationToken = default); // Add token

        Task<ReplicateChunkReply> ReplicateChunkToNodeStreamingAsync(
            string targetNodeAddress,
            ReplicateChunkMetadata metadata,
            Stream dataStream,
            CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a request to delete a chunk replica on a specified target node.
        /// </summary>
        /// <param name="targetNodeAddress">The address (e.g., "host:port") of the target node.</param>
        /// <param name="request">The delete request details.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param> // Add cancellation token param doc
        /// <returns>The reply from the target node.</returns>
        Task<DeleteChunkReply> DeleteChunkOnNodeAsync(string targetNodeAddress, DeleteChunkRequest request, CancellationToken cancellationToken = default); // Add token

        /// <summary>
        /// Sends a Ping request to a specified target node to check its availability.
        /// </summary>
        /// <param name="targetNodeAddress">The address (e.g., "host:port") of the target node.</param>
        /// <param name="request">The ping request details.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param> // MODIFIED: Added cancellation token parameter
        /// <returns>The reply from the target node, or null/exception if unreachable.</returns>
        Task<PingReply> PingNodeAsync(string targetNodeAddress, PingRequest request, CancellationToken cancellationToken = default); // MODIFIED: Added token parameter

        /// <summary>
        /// Requests chunk data from a target node via streaming.
        /// </summary>
        /// <param name="targetNodeAddress">The address (e.g., "host:port") of the target node.</param>
        /// <param name="request">The request details (fileId, chunkId).</param>
        /// <param name="cancellationToken">Cancellation token for the streaming operation.</param>
        /// <returns>An AsyncServerStreamingCall object to read the stream, or null if the initial call fails.</returns>
        Task<AsyncServerStreamingCall<RequestChunkReply>?> RequestChunkFromNodeAsync(string targetNodeAddress, RequestChunkRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the list of known files from a target node.
        /// </summary>
        /// <param name="targetNodeAddress">The address (e.g., "host:port") of the target node.</param>
        /// <param name="request">The request details.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The reply containing the list of files, or null if the call fails.</returns>
        Task<GetNodeFileListReply?> GetNodeFileListAsync(string targetNodeAddress, GetNodeFileListRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an AcknowledgeReplica request to the target node to confirm successful replica storage.
        /// </summary>
        /// <param name="targetNodeAddress">The address of the node to send the acknowledgement to.</param>
        /// <param name="request">The acknowledgement request details.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        Task AcknowledgeReplicaAsync(string targetNodeAddress, AcknowledgeReplicaRequest request, CancellationToken cancellationToken = default);
    }
}