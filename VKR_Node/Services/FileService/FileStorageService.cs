using System.Collections.Concurrent;
using AutoMapper;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Enums;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Services.FileService.FileInterface;
using VKR_Node.Services.Utilities;
using VKR.Protos;

namespace VKR_Node.Services.FileService
{
    public class FileStorageService : IFileStorageService
    {
        private readonly ILogger<FileStorageService> _logger;
        private readonly IMetadataManager _metadataManager;
        private readonly IDataManager _dataManager;
        private readonly INodeClient _nodeClient;
        private readonly IReplicationManager _replicationManager;
        private readonly string _localNodeId;
        private readonly NodeIdentityOptions _nodeIdentityOptions;
        private readonly NetworkOptions _networkOptions;
        private readonly StorageOptions _storageOptions;
        private readonly DhtOptions _dhtOptions;
        private readonly ChunkStreamingHelper _streamingHelper;
        private readonly IMapper _mapper;

        public FileStorageService(
            ILogger<FileStorageService> logger,
            IMetadataManager metadataManager,
            IDataManager dataManager,
            INodeClient nodeClient,
            IOptions<NetworkOptions> networkOptions,
            IReplicationManager replicationManager,
            IOptions<NodeIdentityOptions> nodeIdentityOptions,
            IOptions<StorageOptions> storageOptions,
            IOptions<DhtOptions> dhtOptions,
            IMapper mapper)
        {
            _logger = logger;
            _metadataManager = metadataManager;
            _dataManager = dataManager;
            _nodeClient = nodeClient;
            _replicationManager = replicationManager;
            _nodeIdentityOptions = nodeIdentityOptions.Value;
            _storageOptions = storageOptions.Value;
            _dhtOptions = dhtOptions.Value;
            _networkOptions = networkOptions.Value;
            _localNodeId = _nodeIdentityOptions.NodeId ?? throw new InvalidOperationException("NodeId is not configured.");
            _streamingHelper = new ChunkStreamingHelper(logger, dataManager, nodeClient, _localNodeId);
            _mapper = mapper;
        }

        public async Task<ListFilesReply> ListFiles(ListFilesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("ListFiles request received from {Peer}. Aggregating from self and peers...",
                context.Peer);
            var aggregatedFiles = new ConcurrentDictionary<string, FileMetadata>();
            var reply = new ListFilesReply();

            try
            {
                
                await GetLocalFilesAsync(aggregatedFiles, context.CancellationToken);

                
                await GetPeerFilesAsync(aggregatedFiles, context.CancellationToken);

                
                reply.Files.AddRange(aggregatedFiles.Values.OrderBy(f => f.FileName).ThenBy(f => f.FileId));
                _logger.LogInformation("Returning ListFiles reply with {Count} files.", reply.Files.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing ListFiles request");
                
                
            }

            return reply;
        }

        private async Task GetLocalFilesAsync(
            ConcurrentDictionary<string, FileMetadata> aggregatedFiles,
            CancellationToken cancellationToken)
        {
            try
            {
                var localFiles = await _metadataManager.ListFilesAsync(cancellationToken);
                if (localFiles != null)
                {
                    foreach (var fileCore in localFiles.Where(f => f.State != FileStateCore.Deleting))
                    {
                        if (string.IsNullOrEmpty(fileCore.FileId))
                        {
                            _logger.LogWarning("Skipping local file with empty FileId.");
                            continue;
                        }
                        
                        var fileMetadata = _mapper.Map<FileMetadata>(fileCore);
                        
                        
                        MergeFile(aggregatedFiles, fileMetadata);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving local file list.");
            }
        }

        private async Task GetPeerFilesAsync(
            ConcurrentDictionary<string, FileMetadata> aggregatedFiles,
            CancellationToken cancellationToken)
        {
            var knownPeers = _networkOptions.KnownNodes;
            var semaphore = new SemaphoreSlim(10); 
            var peerTasks = new List<Task>();

            foreach (var peer in knownPeers)
            {
                if (peer.NodeId == _localNodeId || string.IsNullOrEmpty(peer.Address)) continue;
                await semaphore.WaitAsync(cancellationToken);

                peerTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await GetFilesFromPeerAsync(peer, aggregatedFiles, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(peerTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while waiting for peer ListFiles tasks to complete.");
            }
        }

        private async Task GetFilesFromPeerAsync(
            KnownNodeOptions peer,
            ConcurrentDictionary<string, FileMetadata> aggregatedFiles,
            CancellationToken cancellationToken)
        {
            try
            {
                
                using var ctsPing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ctsPing.CancelAfter(TimeSpan.FromSeconds(5));

                var pingReply = await _nodeClient.PingNodeAsync(
                    peer.Address,
                    new PingRequest { SenderNodeId = _localNodeId },
                    ctsPing.Token);

                if (pingReply.Success != true) return;

                using var ctsList = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ctsList.CancelAfter(TimeSpan.FromSeconds(15));

                var fileListReply = await _nodeClient.GetNodeFileListAsync(
                    peer.Address,
                    new GetNodeFileListRequest(),
                    ctsList.Token);

                if (fileListReply?.Files == null) return;

                foreach (var fileProto in fileListReply.Files
                             .Where(f => f.State != FileState.Deleting && !string.IsNullOrEmpty(f.FileId)))
                {
                    MergeFile(aggregatedFiles, fileProto);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("ListFiles request to peer {PeerId} was canceled.", peer.NodeId);
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning("ListFiles request to peer {PeerId} timed out or was cancelled.", peer.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying peer {PeerId} during ListFiles", peer.NodeId);
            }
        }

        private void MergeFile(
            ConcurrentDictionary<string, FileMetadata> aggregatedFiles,
            FileMetadata file)
        {
            aggregatedFiles.AddOrUpdate(
                file.FileId,
                file,
                (key, existing) =>
                {
                    var fileModTime = file.ModificationTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;
                    var existingModTime = existing.ModificationTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;
                    return fileModTime > existingModTime ? file : existing;
                });
        }

        public async Task<UploadFileReply> UploadFile(IAsyncStreamReader<UploadFileRequest> requestStream,
            ServerCallContext context)
        {
            _logger.LogInformation("UploadFile request initiated by client {Peer}", context.Peer);
            FileMetadata? fileMetadataProto = null;
            FileModel? partialFileModel = null;
            long totalBytesReceived = 0;
            int chunkCount = 0;
            string? fileId = null;
            int actualChunkSize = _storageOptions.ChunkSize > 0 ? _storageOptions.ChunkSize : 1048576;
            long expectedFileSize = 0;
            bool metadataReceived = false;  
            int expectedChunkIndex = 0;

            try
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    if (request.PayloadCase == UploadFileRequest.PayloadOneofCase.Metadata)
                    {
                        if (metadataReceived)
                        {
                            _logger.LogWarning("Metadata sent more than once. Ignoring.");
                            continue;
                        }

                        metadataReceived = true;  
                        fileMetadataProto = request.Metadata;
                        fileId = Guid.NewGuid().ToString();
                        fileMetadataProto.FileId = fileId;
                        expectedFileSize = fileMetadataProto.ExpectedFileSize > 0 ? fileMetadataProto.ExpectedFileSize : 0;
                        actualChunkSize = expectedFileSize > 0 ? DetermineOptimalChunkSize(expectedFileSize) : actualChunkSize;
                        fileMetadataProto.ChunkSize = actualChunkSize;

                        _logger.LogInformation(
                            "Received metadata for file: {FileName}, Gen FileId: {FileId}, ChunkSize: {ChunkSize}, ExpectedSize: {ExpectedSize}",
                            fileMetadataProto.FileName, fileId, actualChunkSize,
                            expectedFileSize > 0 ? expectedFileSize.ToString() : "N/A");

                        partialFileModel = new FileModel
                        {
                            FileId = fileId,
                            FileName = fileMetadataProto.FileName,
                            FileSize = expectedFileSize,
                            CreationTime = fileMetadataProto.CreationTime?.ToDateTime() ?? DateTime.UtcNow,
                            ModificationTime = DateTime.UtcNow,
                            ContentType = fileMetadataProto.ContentType,
                            ChunkSize = actualChunkSize,
                            TotalChunks = -1,
                            State = FileStateCore.Uploading
                        };
                    }
                    else if (request.PayloadCase == UploadFileRequest.PayloadOneofCase.Chunk)
                    {
                        if (!metadataReceived || fileMetadataProto == null || string.IsNullOrEmpty(fileId) ||
                            partialFileModel == null)
                        {
                            throw new RpcException(new Status(StatusCode.InvalidArgument,
                                "Chunk received before valid metadata."));
                        }

                        var chunkProto = request.Chunk;
                        if (chunkProto.ChunkIndex != expectedChunkIndex)
                        {
                            throw new RpcException(new Status(
                                StatusCode.InvalidArgument,
                                $"Unexpected chunk index. Expected {expectedChunkIndex}, received {chunkProto.ChunkIndex}"));
                        }

                        _logger.LogDebug("Received Chunk Index: {Index}, Size: {Size} bytes.", chunkProto.ChunkIndex,
                            chunkProto.Data.Length);

                        var chunkInfo = new ChunkModel
                        {
                            FileId = fileId,
                            ChunkId = chunkProto.ChunkId,
                            ChunkIndex = chunkProto.ChunkIndex,
                            Size = chunkProto.Data.Length,
                            StoredNodeId = _localNodeId,
                            ChunkHash = null
                        };

                        
                        await using (var dataStream = new MemoryStream(chunkProto.Data.Memory.ToArray()))
                        {
                            await _dataManager.StoreChunkAsync(chunkInfo, dataStream, context.CancellationToken);
                        }

                        _logger.LogDebug("Stored Chunk {ChunkId} data locally.", chunkInfo.ChunkId);

                        try
                        {
                            await _metadataManager.SaveChunkMetadataAsync(
                                chunkInfo,
                                new List<string> { _localNodeId },
                                context.CancellationToken);

                            _logger.LogDebug("Saved metadata for Chunk {ChunkId} locally.", chunkInfo.ChunkId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to save metadata for locally stored Chunk {ChunkId}.", chunkInfo.ChunkId);

                            try
                            {
                                await _dataManager.DeleteChunkAsync(chunkInfo, CancellationToken.None);
                            }
                            catch (Exception cleanupEx)
                            {
                                _logger.LogError(cleanupEx,
                                    "Failed to clean up chunk data after metadata error for Chunk {ChunkId}", chunkInfo.ChunkId);
                            }

                            throw new RpcException(new Status(
                                StatusCode.Internal,
                                $"Failed to save metadata for chunk {chunkInfo.ChunkIndex}."));
                        }

                        
                        await TriggerChunkReplicationAsync(chunkInfo, chunkProto, partialFileModel, context.CancellationToken);

                        totalBytesReceived += chunkInfo.Size;
                        chunkCount++;
                        expectedChunkIndex++;
                    }
                    else
                    {
                        _logger.LogWarning("Received unknown payload case: {Case}", request.PayloadCase);
                    }
                }

                if (!metadataReceived || fileMetadataProto == null || string.IsNullOrEmpty(fileId))
                {
                    throw new RpcException(new Status(
                        StatusCode.InvalidArgument,
                        "Upload stream completed without receiving metadata."));
                }

                if (expectedFileSize > 0 && totalBytesReceived != expectedFileSize)
                {
                    _logger.LogWarning(
                        "Uploaded file size mismatch for File {Id}. Expected: {ExpSize}, Received: {RecSize}.",
                        fileId, expectedFileSize, totalBytesReceived);
                }

                var finalMetadataCore = new FileModel
                {
                    FileId = fileId,
                    FileName = fileMetadataProto.FileName,
                    FileSize = totalBytesReceived,
                    CreationTime = fileMetadataProto.CreationTime?.ToDateTime() ?? DateTime.UtcNow,
                    ModificationTime = DateTime.UtcNow,
                    ContentType = fileMetadataProto.ContentType,
                    ChunkSize = actualChunkSize,
                    TotalChunks = chunkCount,
                    State = FileStateCore.Available
                };

                await _metadataManager.SaveFileMetadataAsync(finalMetadataCore, context.CancellationToken);

                _logger.LogInformation("File {Name} (ID: {Id}) uploaded successfully. Final Size: {Size}, Chunks: {Count}",
                    finalMetadataCore.FileName, fileId, totalBytesReceived, chunkCount);

                return new UploadFileReply { Success = true, Message = "File uploaded successfully.", FileId = fileId };
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC error during file upload (FileId: {Id}): {Code} - {Detail}",
                    fileId ?? "N/A", ex.StatusCode, ex.Status.Detail);

                await HandleUploadErrorAsync(fileId, context.CancellationToken);
                return new UploadFileReply
                    { Success = false, Message = $"Upload failed: {ex.Status.Detail}", FileId = fileId };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic error during file upload (FileId: {Id})", fileId ?? "N/A");
                await HandleUploadErrorAsync(fileId, context.CancellationToken);
                return new UploadFileReply
                    { Success = false, Message = $"Upload failed: {ex.Message}", FileId = fileId };
            }
            finally
            {
                _logger.LogDebug("Exiting UploadFile method.");
            }
        }

        private async Task TriggerChunkReplicationAsync(
            ChunkModel chunkInfo,
            FileChunk chunkProto,
            FileModel? partialFileModel,
            CancellationToken cancellationToken)
        {
            int replicationFactor = Math.Max(2, _dhtOptions.ReplicationFactor);
            int replicasNeeded = replicationFactor - 1;

            var potentialPeers = (_networkOptions.KnownNodes)
                .Where(n => n.NodeId != _localNodeId && !string.IsNullOrEmpty(n.Address))
                .ToList();

            if (!potentialPeers.Any())
            {
                _logger.LogWarning("No potential peers available for replication of Chunk {ChunkId}.",
                    chunkInfo.ChunkId);
                return;
            }

            var onlinePeerNodes = await FindOnlinePeersAsync(potentialPeers, cancellationToken);
            var targetReplicaNodes = NodeSelectionHelper.SelectReplicaTargets(
                onlinePeerNodes,
                chunkInfo.ChunkId,
                replicasNeeded,
                _logger);

            _logger.LogInformation(
                "Chunk {Index} ({Id}): Stored locally. Triggering replication to {Count} targets: {Nodes}",
                chunkInfo.ChunkIndex,
                chunkInfo.ChunkId,
                targetReplicaNodes.Count,
                string.Join(", ", targetReplicaNodes.Select(n => n.NodeId)));

            if (partialFileModel == null)
            {
                _logger.LogError("Cannot trigger replication for Chunk {Id}: Parent metadata missing.",
                    chunkInfo.ChunkId);
                return;
            }

            var replicationTasks = new List<Task<bool>>();
            var successfulReplications = new ConcurrentBag<string>();

            foreach (var targetNode in targetReplicaNodes)
            {
                if (string.IsNullOrEmpty(targetNode.Address))
                {
                    continue;
                }

                var replicationTask = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("Replicating Chunk {ChunkId} to Node {TargetId} ({Addr})",
                            chunkInfo.ChunkId, targetNode.NodeId, targetNode.Address);

                        var replicateRequest = new ReplicateChunkRequest
                        {
                            FileId = chunkInfo.FileId,
                            ChunkId = chunkInfo.ChunkId,
                            ChunkIndex = chunkInfo.ChunkIndex,
                            Data = chunkProto.Data,
                            OriginalNodeId = _localNodeId,
                            ParentFileMetadata = _mapper.Map<FileMetadata>(partialFileModel)
                        };

                        var reply = await _nodeClient.ReplicateChunkToNodeAsync(
                            targetNode.Address,
                            replicateRequest,
                            cancellationToken);

                        if (reply.Success)
                        {
                            successfulReplications.Add(targetNode.NodeId);
                            _logger.LogInformation(
                                "Replication call for chunk {ChunkId} to {NodeId} completed successfully",
                                chunkInfo.ChunkId, targetNode.NodeId);
                            return true;
                        }
                        else
                        {
                            _logger.LogWarning("Replication call for chunk {ChunkId} to {NodeId} failed: {Msg}",
                                chunkInfo.ChunkId, targetNode.NodeId, reply.Message);
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in replication for chunk {ChunkId} to {NodeId}",
                            chunkInfo.ChunkId, targetNode.NodeId);
                        return false;
                    }
                }, cancellationToken);

                replicationTasks.Add(replicationTask);
            }

            await Task.WhenAll(replicationTasks);

            if (successfulReplications.Any())
            {
                var allStorageNodes = new List<string> { _localNodeId };
                allStorageNodes.AddRange(successfulReplications);

                await _metadataManager.UpdateChunkStorageNodesAsync(
                    chunkInfo.FileId,
                    chunkInfo.ChunkId,
                    allStorageNodes,
                    cancellationToken);

                _logger.LogInformation("Updated storage nodes for Chunk {ChunkId}. Added {Count} replicas.",
                    chunkInfo.ChunkId, successfulReplications.Count);
            }
            else if (replicasNeeded > 0)
            {
                _logger.LogWarning("Failed to replicate Chunk {ChunkId} to any nodes. Will retry in background.",
                    chunkInfo.ChunkId);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _replicationManager.EnsureChunkReplicationAsync(
                            chunkInfo.FileId,
                            chunkInfo.ChunkId,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background replication retry for Chunk {ChunkId} failed",
                            chunkInfo.ChunkId);
                    }
                });
            }
        }


        private async Task HandleUploadErrorAsync(string? fileId, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(fileId))
            {
                try
                {
                    await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Error, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update file state to Error for {FileId}", fileId);
                }
            }
        }

        private int DetermineOptimalChunkSize(long expectedFileSize)
        {
            const long KB = 1024, MB = 1024 * KB, GB = 1024 * MB;
            int defaultChunkSize = _storageOptions.ChunkSize > 0 ? _storageOptions.ChunkSize : (int)(1 * MB);

            if (expectedFileSize <= 0)
            {
                return defaultChunkSize;
            }
            else if (expectedFileSize <= 100 * MB)
            {
                return (int)(1 * MB);
            }
            else if (expectedFileSize <= 1 * GB)
            {
                return (int)(4 * MB);
            }
            else if (expectedFileSize <= 10 * GB)
            {
                return (int)(16 * MB);
            }
            else
            {
                return (int)(64 * MB);
            }
        }

        private async Task<List<KnownNodeOptions>> FindOnlinePeersAsync(
            List<KnownNodeOptions> peers,
            CancellationToken cancellationToken)
        {
            var onlinePeers = new ConcurrentBag<KnownNodeOptions>();
            var pingTasks = new List<Task>();
            var semaphore = new SemaphoreSlim(10);

            foreach (var peer in peers)
            {
                await semaphore.WaitAsync(cancellationToken);
                pingTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogTrace("Pinging peer {Id} ({Addr}) for replication suitability.",
                            peer.NodeId, peer.Address);

                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(TimeSpan.FromSeconds(3));

                        var reply = await _nodeClient.PingNodeAsync(
                            peer.Address,
                            new PingRequest { SenderNodeId = _localNodeId },
                            cts.Token);

                        if (reply.Success)
                        {
                            onlinePeers.Add(peer);
                            _logger.LogTrace("Peer {Id} is online.", peer.NodeId);
                        }
                        else
                        {
                            _logger.LogTrace("Peer {Id} is offline/ping failed: {Reason}",
                                peer.NodeId, reply.ResponderNodeId);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogTrace("Ping timed out for peer {Id}.", peer.NodeId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error pinging peer {Id} ({Addr}).",
                            peer.NodeId, peer.Address);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(pingTasks);
            return onlinePeers.ToList();
        }

        public async Task DownloadFile(
            DownloadFileRequest request,
            IServerStreamWriter<DownloadFileReply> responseStream,
            ServerCallContext context)
        {
            _logger.LogInformation("DownloadFile request for File ID: {FileId} from peer: {Peer}",
                request.FileId, context.Peer);

            if (string.IsNullOrEmpty(request.FileId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "FileId cannot be empty."));
            }

            try
            {
                var FileModel = await GetAndValidateFileMetadataAsync(request.FileId, context.CancellationToken);

                await SendFileMetadataAsync(FileModel, responseStream, context.CancellationToken);

                await SendFileChunksAsync(FileModel, responseStream, context.CancellationToken);

                _logger.LogInformation("Finished streaming all chunks for File ID: {Id}", request.FileId);
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "gRPC error during download for File ID {Id}: {Code} - {Detail}",
                    request.FileId, ex.StatusCode, ex.Status.Detail);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("DownloadFile for File ID {Id} was cancelled.", request.FileId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during download for File ID: {Id}", request.FileId);
                throw new RpcException(new Status(
                    StatusCode.Internal,
                    $"Internal error during download: {ex.Message}"));
            }
        }

        private async Task<FileModel> GetAndValidateFileMetadataAsync(
            string fileId,
            CancellationToken cancellationToken)
        {
            var FileModel = await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);

            if (FileModel == null)
            {
                throw new RpcException(new Status(
                    StatusCode.NotFound,
                    $"File metadata not found for ID '{fileId}'."));
            }

            if (FileModel.State is FileStateCore.Deleting or FileStateCore.Deleted)
            {
                throw new RpcException(new Status(
                    StatusCode.NotFound,
                    $"File '{FileModel.FileName}' (ID: {fileId}) is deleted or pending deletion."));
            }

            if (FileModel.State != FileStateCore.Available)
            {
                _logger.LogWarning("File {Id} requested for download is not Available (State: {State}). Proceeding...",
                    fileId, FileModel.State);
            }

            _logger.LogInformation("Found metadata for file: {Name}, Size: {Size}, Chunks: {Chunks}, State: {State}",
                FileModel.FileName, FileModel.FileSize, FileModel.TotalChunks,
                FileModel.State);

            return FileModel;
        }

        private async Task SendFileMetadataAsync(
            FileModel FileModel,
            IServerStreamWriter<DownloadFileReply> responseStream,
            CancellationToken cancellationToken)
        {
            await responseStream.WriteAsync(new DownloadFileReply
            {
                
                Metadata = _mapper.Map<FileMetadata>(FileModel),
            });

            _logger.LogDebug("Sent metadata reply for File ID: {FileId}", FileModel.FileId);
        }

        private async Task SendFileChunksAsync(
            FileModel fileModel,
            IServerStreamWriter<DownloadFileReply> responseStream,
            CancellationToken cancellationToken)
        {
            var chunkInfos = (await _metadataManager.GetChunksMetadataForFileAsync(
                fileModel.FileId, cancellationToken))?.OrderBy(c => c.ChunkIndex).ToList();

            if (chunkInfos == null || !chunkInfos.Any())
            {
                if (fileModel.FileSize > 0)
                {
                    _logger.LogError(
                        "Inconsistency: File metadata for {Id} (Size: {Size}) exists, but no chunk metadata.",
                        fileModel.FileId, fileModel.FileSize);

                    throw new RpcException(new Status(
                        StatusCode.Internal,
                        $"Inconsistency: Chunk metadata missing for file ID '{fileModel.FileId}'."));
                }
                else
                {
                    _logger.LogInformation("File {Id} is empty (Size 0). Download complete.", fileModel.FileId);
                    return;
                }
            }

            _logger.LogDebug("Found {Count} chunk metadata entries for File ID: {Id}. Starting streaming...",
                chunkInfos.Count, fileModel.FileId);

            foreach (var chunkInfo in chunkInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Processing Chunk Index: {Index}, Chunk ID: {ChunkId}",
                    chunkInfo.ChunkIndex, chunkInfo.ChunkId);

                bool chunkSent = await TryStreamChunkFromAnySourceAsync(chunkInfo, responseStream, cancellationToken);

                if (!chunkSent)
                {
                    _logger.LogError(
                        "Failed to retrieve/stream Chunk {Id} (Index {Index}) from any source for File {FileId}. Aborting.",
                        chunkInfo.ChunkId, chunkInfo.ChunkIndex, fileModel.FileId);

                    throw new RpcException(new Status(
                        StatusCode.Internal,
                        $"Chunk {chunkInfo.ChunkIndex} (ID: {chunkInfo.ChunkId}) could not be retrieved."));
                }
            }
        }

        private async Task<bool> TryStreamChunkFromAnySourceAsync(
            ChunkModel chunkInfo,
            IServerStreamWriter<DownloadFileReply> responseStream,
            CancellationToken cancellationToken)
        {
            var storageNodes = (await _metadataManager.GetChunkStorageNodesAsync(
                chunkInfo.FileId, chunkInfo.ChunkId, cancellationToken)).ToList();

            if (!storageNodes.Any())
            {
                _logger.LogError("Chunk {Id} (Index {Index}) has no known storage locations!",
                    chunkInfo.ChunkId, chunkInfo.ChunkIndex);
                return false;
            }

            _logger.LogDebug("Chunk {Id}: Found storage nodes: {Nodes}",
                chunkInfo.ChunkId, string.Join(",", storageNodes));

            
            if (storageNodes.Contains(_localNodeId))
            {
                _logger.LogTrace("Attempting local stream for Chunk {Id}.", chunkInfo.ChunkId);

                if (await TryStreamLocalChunkAsync(chunkInfo, responseStream, cancellationToken))
                {
                    return true;
                }

                _logger.LogWarning("Failed local stream for Chunk {Id}. Trying remote.", chunkInfo.ChunkId);
                storageNodes.Remove(_localNodeId);
            }

            
            //var remoteNodeIds = storageNodes.Where(id => id != _localNodeId).ToList();
            var remoteNodeIds = storageNodes.ToList();
            if (!remoteNodeIds.Any())
            {
                _logger.LogError("No valid storage nodes found for Chunk {ChunkId}", chunkInfo.ChunkId);
                return false;
            }
            remoteNodeIds = remoteNodeIds.OrderBy(_ => Guid.NewGuid()).ToList();

            _logger.LogTrace("Attempting remote stream for Chunk {Id} from nodes: {Nodes}",
                chunkInfo.ChunkId, string.Join(",", remoteNodeIds));

            foreach (var remoteNodeId in remoteNodeIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetNodeInfo = _networkOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == remoteNodeId);

                if (targetNodeInfo == null || string.IsNullOrEmpty(targetNodeInfo.Address))
                {
                    _logger.LogWarning("No address for remote node {Id}. Skipping.", remoteNodeId);
                    continue;
                }

                if (await TryStreamRemoteChunkAsync(chunkInfo, targetNodeInfo, responseStream, cancellationToken))
                {
                    return true;
                }

                _logger.LogWarning("Failed remote stream for Chunk {Id} from {RemoteId}. Trying next.",
                    chunkInfo.ChunkId, remoteNodeId);
            }

            _logger.LogError("Failed to stream Chunk {Id} from all locations: {Locations}",
                chunkInfo.ChunkId, string.Join(",", storageNodes));

            return false;
        }

        private async Task<bool> TryStreamLocalChunkAsync(
            ChunkModel chunkInfo,
            IServerStreamWriter<DownloadFileReply> responseStream,
            CancellationToken cancellationToken)
        {
            Stream? chunkStream = null;

            try
            {
                
                var localChunkInfo = chunkInfo with { StoredNodeId = _localNodeId };

                chunkStream = await _dataManager.RetrieveChunkAsync(localChunkInfo, cancellationToken);

                if (chunkStream == null)
                {
                    _logger.LogWarning("DataManager returned null stream for local Chunk {Id}.", chunkInfo.ChunkId);
                    return false;
                }

                byte[] buffer = new byte[65536];
                int bytesRead;

                while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    var fileChunkProto = new FileChunk
                    {
                        FileId = chunkInfo.FileId,
                        ChunkId = chunkInfo.ChunkId,
                        ChunkIndex = chunkInfo.ChunkIndex,
                        Data = ByteString.CopyFrom(buffer, 0, bytesRead),
                        Size = bytesRead
                    };

                    await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto },
                        cancellationToken);
                }

                _logger.LogInformation("Finished streaming local Chunk {Id} (Index {Index})",
                    chunkInfo.ChunkId, chunkInfo.ChunkIndex);

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming local Chunk {Id}.", chunkInfo.ChunkId);
                return false;
            }
            finally
            {
                if (chunkStream != null)
                {
                    await chunkStream.DisposeAsync();
                }
            }
        }
        
        private async Task<bool> TryStreamRemoteChunkAsync(
            ChunkModel chunkInfo,
            KnownNodeOptions targetNodeInfo,
            IServerStreamWriter<DownloadFileReply> responseStream,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Attempting remote fetch for Chunk {Id} from Node {RemoteId} ({Addr})", 
                chunkInfo.ChunkId, targetNodeInfo.NodeId, targetNodeInfo.Address);
                
            AsyncServerStreamingCall<RequestChunkReply>? remoteCall = null;
            
            try
            {
                var remoteRequest = new RequestChunkRequest
                {
                    FileId = chunkInfo.FileId,
                    ChunkId = chunkInfo.ChunkId
                };
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(60));
                
                remoteCall = await _nodeClient.RequestChunkFromNodeAsync(
                    targetNodeInfo.Address, remoteRequest, cts.Token);
                    
                if (remoteCall == null)
                {
                    _logger.LogWarning("Failed to initiate RequestChunk call to node {Id}.", targetNodeInfo.NodeId);
                    return false;
                }
                
                await foreach (var replyChunk in remoteCall.ResponseStream.ReadAllAsync(cts.Token))
                {
                    if (replyChunk.Data != null && !replyChunk.Data.IsEmpty)
                    {
                        var fileChunkProto = new FileChunk
                        {
                            FileId = chunkInfo.FileId,
                            ChunkId = chunkInfo.ChunkId,
                            ChunkIndex = chunkInfo.ChunkIndex,
                            Data = replyChunk.Data,
                            Size = replyChunk.Data.Length
                        };
                        
                        await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto }, cts.Token);
                    }
                    else
                    {
                        _logger.LogWarning("Received empty data chunk from remote {Id} for Chunk {ChunkId}", 
                            targetNodeInfo.NodeId, chunkInfo.ChunkId);
                    }
                }
                
                _logger.LogInformation("Finished streaming Chunk {Id} (Index {Index}) from remote {RemoteId}", 
                    chunkInfo.ChunkId, chunkInfo.ChunkIndex, targetNodeInfo.NodeId);
                    
                return true;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                _logger.LogWarning("Remote node {Id} reported NotFound for Chunk {ChunkId}.", 
                    targetNodeInfo.NodeId, chunkInfo.ChunkId);
                return false;
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded or StatusCode.Unavailable)
            {
                _logger.LogWarning("gRPC error ({Code}) fetching chunk {Id} from {RemoteId}", 
                    ex.StatusCode, chunkInfo.ChunkId, targetNodeInfo.NodeId);
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Download cancelled while fetching {Id} from {RemoteId}.", 
                    chunkInfo.ChunkId, targetNodeInfo.NodeId);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Fetching chunk {Id} from {RemoteId} timed out/cancelled.", 
                    chunkInfo.ChunkId, targetNodeInfo.NodeId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching/streaming chunk {Id} from {RemoteId}", 
                    chunkInfo.ChunkId, targetNodeInfo.NodeId);
                return false;
            }
            finally
            {
                remoteCall?.Dispose();
            }
        }

        public async Task<DeleteFileReply> DeleteFile(DeleteFileRequest request, ServerCallContext context)
        {
            string fileId = request.FileId;
            
            if (string.IsNullOrEmpty(fileId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "FileId cannot be empty."));
            }
            
            _logger.LogInformation("DeleteFile request for File ID: {FileId} from {Peer}.", fileId, context.Peer);
            
            FileModel? fileMeta = null;
            IEnumerable<ChunkModel>? chunkInfos = null;
            var chunkNodeMap = new Dictionary<string, List<string>>();
            
            try
            {
                fileMeta = await GetFileMetadataForDeletionAsync(fileId, context.CancellationToken);
                if (fileMeta == null)
                {
                    _logger.LogWarning("File {Id} not found during delete. Assuming deleted.", fileId);
                    return new DeleteFileReply { Success = true, Message = "File not found, assumed deleted." };
                }
                
                if (fileMeta.State is FileStateCore.Deleted or FileStateCore.Deleting)
                {
                    _logger.LogInformation("File {Id} already in {State} state.", fileId, fileMeta.State);
                    return new DeleteFileReply { Success = true, Message = $"File already in {fileMeta.State} state." };
                }

                
                await UpdateFileStateForDeletionAsync(fileId, context.CancellationToken);
                
                
                chunkInfos = await _metadataManager.GetChunksMetadataForFileAsync(fileId, context.CancellationToken);
                if (chunkInfos == null || !chunkInfos.Any())
                {
                    _logger.LogInformation("File {Id} has no chunk metadata. Skipping peer notification.", fileId);
                    await DeleteFileMetadataAsync(fileId, context.CancellationToken);
                    return new DeleteFileReply { Success = true, Message = $"File {fileId} deletion processed (no chunks)." };
                }
                
                var chunkLocationMap = await BuildChunkLocationMapAsync(fileId, chunkInfos, context.CancellationToken);
                
                await NotifyRemoteNodesForDeletionAsync(fileId, chunkLocationMap, context.CancellationToken);
                
                await DeleteLocalChunksAsync(fileId, chunkInfos, chunkLocationMap, context.CancellationToken);
                
                await DeleteFileMetadataAsync(fileId, context.CancellationToken);
                
                _logger.LogInformation("DeleteFile process completed successfully for File ID: {Id}", fileId);
                return new DeleteFileReply { Success = true, Message = $"File {fileId} deletion processed." };
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during delete process for file {Id}.", fileId);
                
                if (fileMeta != null)
                {
                    try
                    {
                        await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Error, CancellationToken.None);
                    }
                    catch (Exception updateEx)
                    {
                        _logger.LogError(updateEx, "Failed to update file state to Error for {FileId}", fileId);
                    }
                }
                
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to process file deletion: {ex.Message}"));
            }
        }

        private async Task<FileModel?> GetFileMetadataForDeletionAsync(
            string fileId, 
            CancellationToken cancellationToken)
        {
            try
            {
                return await _metadataManager.GetFileMetadataAsync(fileId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching metadata for file {FileId} during deletion", fileId);
                throw;
            }
        }

        private async Task UpdateFileStateForDeletionAsync(
            string fileId, 
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Marking File {Id} state as Deleting.", fileId);
            
            try
            {
                await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Deleting, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update file state to Deleting for {FileId}", fileId);
                throw;
            }
        }

        private async Task<Dictionary<string, List<string>>> BuildChunkLocationMapAsync(
            string fileId,
            IEnumerable<ChunkModel> chunkInfos,
            CancellationToken cancellationToken)
        {
            _logger.LogDebug("Fetching chunk locations for File ID {Id}...", fileId);
            var chunkNodeMap = new Dictionary<string, List<string>>();
            var nodesToDeleteFrom = new HashSet<string>();
            
            try
            {
                var locationTasks = chunkInfos.Select(async ci => new
                {
                    ci.ChunkId,
                    Nodes = await _metadataManager.GetChunkStorageNodesAsync(fileId, ci.ChunkId, cancellationToken)
                }).ToList();
                
                var chunkLocations = await Task.WhenAll(locationTasks);
                
                foreach (var locInfo in chunkLocations)
                {
                    if (locInfo.Nodes != null && locInfo.Nodes.Any())
                    {
                        chunkNodeMap[locInfo.ChunkId] = locInfo.Nodes.ToList();
                        foreach (var nodeId in locInfo.Nodes)
                        {
                            nodesToDeleteFrom.Add(nodeId);
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "No storage nodes found for Chunk {Id} of File {FileId} during deletion.",
                            locInfo.ChunkId, fileId);
                    }
                }
                
                _logger.LogInformation(
                    "Notifying {Count} unique nodes for File {Id} deletion: {Nodes}",
                    nodesToDeleteFrom.Count, fileId, string.Join(", ", nodesToDeleteFrom));
                    
                return chunkNodeMap;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building chunk location map for file {FileId}", fileId);
                throw;
            }
        }

        private async Task NotifyRemoteNodesForDeletionAsync(
            string fileId,
            Dictionary<string, List<string>> chunkNodeMap,
            CancellationToken cancellationToken)
        {
            
            var allNodeIds = chunkNodeMap.Values
                .SelectMany(nodes => nodes)
                .Distinct()
                .Where(nodeId => nodeId != _localNodeId)
                .ToList();
                
            if (!allNodeIds.Any())
            {
                _logger.LogInformation("No remote nodes to notify for file {FileId} deletion", fileId);
                return;
            }
            
            var nodeDeleteTasks = new List<Task>();
            
            foreach (var nodeId in allNodeIds)
            {
                var nodeInfo = _networkOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
                
                if (nodeInfo == null || string.IsNullOrEmpty(nodeInfo.Address))
                {
                    _logger.LogWarning("No address for Node {Id}. Cannot trigger remote delete.", nodeId);
                    continue;
                }
                
                
                var chunksOnThisNode = chunkNodeMap
                    .Where(kvp => kvp.Value.Contains(nodeId))
                    .Select(kvp => kvp.Key)
                    .ToList();
                    
                if (!chunksOnThisNode.Any())
                {
                    continue;
                }
                
                _logger.LogDebug(
                    "Notifying remote {Id} ({Addr}) to delete {Count} chunks.",
                    nodeId, nodeInfo.Address, chunksOnThisNode.Count);
                    
                
                nodeDeleteTasks.Add(Task.Run(async () =>
                {
                    foreach (var chunkId in chunksOnThisNode)
                    {
                        try
                        {
                            
                            await _nodeClient.DeleteChunkOnNodeAsync(
                                nodeInfo.Address,
                                new DeleteChunkRequest { FileId = fileId, ChunkId = chunkId },
                                CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Exception in remote DeleteChunk task for Chunk {ChunkId} on {Addr}",
                                chunkId, nodeInfo.Address);
                            
                        }
                    }
                }));
            }
            
            
            await Task.WhenAll(nodeDeleteTasks);
        }

        private async Task DeleteLocalChunksAsync(
            string fileId,
            IEnumerable<ChunkModel> chunkInfos,
            Dictionary<string, List<string>> chunkNodeMap,
            CancellationToken cancellationToken)
        {
            
            var localChunks = chunkInfos
                .Where(ci => chunkNodeMap.TryGetValue(ci.ChunkId, out var nodes) && nodes.Contains(_localNodeId))
                .ToList();
                
            if (!localChunks.Any())
            {
                _logger.LogInformation("No local chunks to delete for File ID: {Id}", fileId);
                return;
            }
            
            _logger.LogInformation("Deleting {Count} local chunk data for File ID: {Id}", localChunks.Count, fileId);
            
            var localDeleteTasks = localChunks.Select(ci => Task.Run(async () =>
            {
                try
                {
                    var localChunkInfo = ci with { StoredNodeId = _localNodeId };
                    
                    await _dataManager.DeleteChunkAsync(localChunkInfo, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error deleting local chunk data for Chunk {Id}.", ci.ChunkId);
                    
                }
            })).ToList();
            
            await Task.WhenAll(localDeleteTasks);
            
            _logger.LogInformation("Finished local chunk data deletion for File ID: {Id}", fileId);
        }

        private async Task DeleteFileMetadataAsync(string fileId, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Deleting local file metadata for File ID: {Id}", fileId);
            
            try
            {
                await _metadataManager.DeleteFileMetadataAsync(fileId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting metadata for File ID: {Id}", fileId);
                throw;
            }
        }

        public Task<GetFileStatusReply> GetFileStatus(GetFileStatusRequest request, ServerCallContext context)
        {
            _logger.LogWarning("GetFileStatus method is not implemented.");
            throw new RpcException(new Status(StatusCode.Unimplemented, "GetFileStatus method is not implemented."));
        }
    }
}

    