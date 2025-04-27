using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using VKR_Core.Enums;


namespace VKR_Node.Services
{
    public class StorageServiceImpl : StorageService.StorageServiceBase
    {
        private readonly ILogger<StorageServiceImpl> _logger;
        private readonly IMetadataManager _metadataManager;
        private readonly IDataManager _dataManager;
        private readonly INodeClient _nodeClient;
        private readonly NodeOptions _nodeOptions;
        private readonly IReplicationManager _replicationManager;
        private readonly StorageOptions _storageOptions;
        private readonly DhtOptions _dhtOptions;
        private readonly string _localNodeId;

        private int ChunkSize => _storageOptions.ChunkSize;

        public StorageServiceImpl(
            ILogger<StorageServiceImpl> logger,
            IMetadataManager metadataManager,
            IDataManager dataManager,
            INodeClient nodeClient,
            IOptions<NodeOptions> nodeOptions,
            IOptions<StorageOptions> storageOptions,
            IReplicationManager replicationManager,
            IOptions<DhtOptions> dhtOptions)
        {
            _logger = logger;
            _metadataManager = metadataManager;
            _dataManager = dataManager;
            _nodeClient = nodeClient;
            _nodeOptions = nodeOptions.Value;
            _storageOptions = storageOptions.Value;
            _replicationManager = replicationManager;
            _dhtOptions = dhtOptions.Value;
            _localNodeId = _nodeOptions.NodeId ?? throw new InvalidOperationException("NodeId is not configured.");
        }

        #region ListFiles Methods
        public override async Task<ListFilesReply> ListFiles(ListFilesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("ListFiles request received from {Peer}. Aggregating from self and peers (excluding 'Deleting' state)...", context.Peer);
            var aggregatedFiles = new ConcurrentDictionary<string, FileMetadata>();
            var semaphore = new SemaphoreSlim(10);

            try
            {
                var localFiles = await _metadataManager.ListFilesAsync(context.CancellationToken);
                if (localFiles != null)
                {
                    foreach (var fileCore in localFiles.Where(f => f.State != FileStateCore.Deleting))
                    {
                        if (string.IsNullOrEmpty(fileCore.FileId)) { _logger.LogWarning("Skipping local file with empty FileId."); continue; }
                        MergeFile(aggregatedFiles, MapFileMetadataCoreToProto(fileCore));
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Error retrieving local file list."); }

            var knownPeers = _nodeOptions.KnownNodes ?? new List<ChunkStorageNode>();
            var peerTasks = new List<Task>();

            foreach (var peer in knownPeers)
            {
                if (peer.NodeId == _localNodeId || string.IsNullOrEmpty(peer.Address)) continue;
                await semaphore.WaitAsync(context.CancellationToken);

                peerTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var ctsPing = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                        ctsPing.CancelAfter(TimeSpan.FromSeconds(5));
                        // Corrected: Pass CancellationToken to PingNodeAsync
                        var pingReply = await _nodeClient.PingNodeAsync(peer.Address, new PingRequest { SenderNodeId = _localNodeId }, ctsPing.Token);
                        if (pingReply?.Success != true) return;

                        using var ctsList = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                        ctsList.CancelAfter(TimeSpan.FromSeconds(15));
                        var sw = Stopwatch.StartNew();
                        var fileListReply = await _nodeClient.GetNodeFileListAsync(peer.Address, new GetNodeFileListRequest(), ctsList.Token);
                        sw.Stop();
                        _logger.LogDebug("Peer {PeerId} GetNodeFileList response time: {Elapsed} ms", peer.NodeId, sw.ElapsedMilliseconds);
                        if (fileListReply?.Files == null) return;
                        foreach (var fileProto in fileListReply.Files.Where(f => f.State != FileState.Deleting && !string.IsNullOrEmpty(f.FileId)))
                        {
                            MergeFile(aggregatedFiles, fileProto);
                        }
                    }
                    catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { _logger.LogInformation("ListFiles request to peer {PeerId} was canceled.", peer.NodeId); }
                    catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded) { _logger.LogWarning("ListFiles request to peer {PeerId} timed out or was cancelled.", peer.NodeId); }
                    catch (Exception ex) { _logger.LogError(ex, "Error querying peer {PeerId} during ListFiles", peer.NodeId); }
                    finally { semaphore.Release(); }
                }, context.CancellationToken));
            }
            try { await Task.WhenAll(peerTasks); }
            catch (Exception ex) { _logger.LogError(ex, "Error occurred while waiting for peer ListFiles tasks to complete."); }

            var reply = new ListFilesReply();
            reply.Files.AddRange(aggregatedFiles.Values.OrderBy(f => f.FileName).ThenBy(f => f.FileId));
            _logger.LogInformation("Returning ListFiles reply with {Count} files.", reply.Files.Count);
            return reply;
        }

        private void MergeFile(ConcurrentDictionary<string, FileMetadata> aggregatedFiles, FileMetadata file)
        {
            aggregatedFiles.AddOrUpdate(file.FileId, file, (key, existing) => {
                var fileModTime = file.ModificationTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;
                var existingModTime = existing.ModificationTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue;
                return fileModTime > existingModTime ? file : existing;
            });
        }

        private FileMetadata MapFileMetadataCoreToProto(FileMetadataCore fileCore)
        {
            var creationTime = fileCore.CreationTime > DateTime.MinValue ? Timestamp.FromDateTime(fileCore.CreationTime.ToUniversalTime()) : null;
            var modTime = fileCore.ModificationTime > DateTime.MinValue ? Timestamp.FromDateTime(fileCore.ModificationTime.ToUniversalTime()) : null;
            return new FileMetadata {
                FileId = fileCore.FileId ?? string.Empty, FileName = fileCore.FileName ?? string.Empty, FileSize = fileCore.FileSize,
                CreationTime = creationTime, ModificationTime = modTime, ContentType = fileCore.ContentType ?? string.Empty,
                ChunkSize = fileCore.ChunkSize, TotalChunks = fileCore.TotalChunks, State = (FileState)fileCore.State
            };
        }
        #endregion

        #region UploadFile Methods
        private int DetermineOptimalChunkSize(long expectedFileSize)
        {
            const long KB = 1024, MB = 1024 * KB, GB = 1024 * MB;
            int defaultChunkSize = _storageOptions.ChunkSize > 0 ? _storageOptions.ChunkSize : (int)(1 * MB);
            if (expectedFileSize <= 0) { return defaultChunkSize; }
            else if (expectedFileSize <= 100 * MB) { return (int)(1 * MB); }
            else if (expectedFileSize <= 1 * GB) { return (int)(4 * MB); }
            else if (expectedFileSize <= 10 * GB) { return (int)(16 * MB); }
            else { return (int)(64 * MB); }
        }

        public override async Task<UploadFileReply> UploadFile(IAsyncStreamReader<UploadFileRequest> requestStream, ServerCallContext context)
        {
            _logger.LogInformation("UploadFile request initiated by client {Peer}", context.Peer);
            FileMetadata? fileMetadataProto = null; FileMetadataCore? partialFileMetadataCore = null;
            long totalBytesReceived = 0; int chunkCount = 0; string? fileId = null;
            int actualChunkSize = _storageOptions.ChunkSize > 0 ? _storageOptions.ChunkSize : 1048576;
            long expectedFileSize = 0; bool metadataReceived = false; int expectedChunkIndex = 0;

            try
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    if (request.PayloadCase == UploadFileRequest.PayloadOneofCase.Metadata)
                    {
                        if (metadataReceived) { _logger.LogWarning("Metadata sent more than once by {Peer}. Ignoring.", context.Peer); continue; }
                        metadataReceived = true; fileMetadataProto = request.Metadata;
                        fileId = Guid.NewGuid().ToString(); fileMetadataProto.FileId = fileId;
                        expectedFileSize = fileMetadataProto.ExpectedFileSize > 0 ? fileMetadataProto.ExpectedFileSize : 0;
                        actualChunkSize = expectedFileSize > 0 ? DetermineOptimalChunkSize(expectedFileSize) : actualChunkSize;
                        fileMetadataProto.ChunkSize = actualChunkSize;
                        _logger.LogInformation("Received metadata for file: {FileName}, Gen FileId: {FileId}, ChunkSize: {ChunkSize}, ExpectedSize: {ExpectedSize}",
                            fileMetadataProto.FileName, fileId, actualChunkSize, expectedFileSize > 0 ? expectedFileSize.ToString() : "N/A");
                        partialFileMetadataCore = new FileMetadataCore {
                            FileId = fileId, FileName = fileMetadataProto.FileName, FileSize = expectedFileSize,
                            CreationTime = fileMetadataProto.CreationTime?.ToDateTime() ?? DateTime.UtcNow, ModificationTime = DateTime.UtcNow,
                            ContentType = fileMetadataProto.ContentType, ChunkSize = actualChunkSize, TotalChunks = -1, State = FileStateCore.Uploading
                        };
                    }
                    else if (request.PayloadCase == UploadFileRequest.PayloadOneofCase.Chunk)
                    {
                        if (!metadataReceived || fileMetadataProto == null || fileId == null || partialFileMetadataCore == null)
                            throw new RpcException(new Status(StatusCode.InvalidArgument, "Chunk received before valid metadata."));
                        var chunkProto = request.Chunk;
                        if (chunkProto.ChunkIndex != expectedChunkIndex)
                            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unexpected chunk index. Expected {expectedChunkIndex}, received {chunkProto.ChunkIndex}"));
                        _logger.LogDebug("Received Chunk Index: {Index}, Size: {Size} bytes.", chunkProto.ChunkIndex, chunkProto.Data.Length);

                        var chunkInfo = new ChunkInfoCore {
                            FileId = fileId, ChunkId = chunkProto.ChunkId, ChunkIndex = chunkProto.ChunkIndex,
                            Size = chunkProto.Data.Length, StoredNodeId = _localNodeId, ChunkHash = null
                        };

                        await using (var dataStream = new MemoryStream(chunkProto.Data.Memory.ToArray()))
                            { await _dataManager.StoreChunkAsync(chunkInfo, dataStream, context.CancellationToken); }
                        _logger.LogDebug("Stored Chunk {ChunkId} data locally.", chunkInfo.ChunkId);

                        try
                        {
                            await _metadataManager.SaveChunkMetadataAsync(chunkInfo, new List<string> { _localNodeId }, context.CancellationToken);
                             _logger.LogDebug("Saved metadata for Chunk {ChunkId} locally.", chunkInfo.ChunkId);
                        }
                        catch (Exception metaEx)
                        {
                            _logger.LogError(metaEx, "Failed to save metadata for locally stored Chunk {ChunkId}.", chunkInfo.ChunkId);
                            try { await _dataManager.DeleteChunkAsync(chunkInfo, CancellationToken.None); } catch { /* Ignore */ }
                            throw new RpcException(new Status(StatusCode.Internal, $"Failed to save metadata for chunk {chunkInfo.ChunkIndex}."));
                        }

                        totalBytesReceived += chunkInfo.Size; chunkCount++; expectedChunkIndex++;

                        int replicationFactor = Math.Max(2, _dhtOptions.ReplicationFactor); int replicasNeeded = replicationFactor - 1;
                        if (replicasNeeded > 0)
                        {
                            var potentialPeers = (_nodeOptions.KnownNodes ?? new List<ChunkStorageNode>())
                                .Where(n => n.NodeId != _localNodeId && !string.IsNullOrEmpty(n.Address)).ToList();
                            var onlinePeerNodes = await FindOnlinePeersAsync(potentialPeers, context.CancellationToken);
                            var targetReplicaNodes = SelectReplicaTargetsFromInfo(onlinePeerNodes, chunkInfo.ChunkId, replicasNeeded);
                            _logger.LogInformation("Chunk {Index} ({Id}): Stored locally. Triggering replication to {Count} targets: {Nodes}",
                                   chunkInfo.ChunkIndex, chunkInfo.ChunkId, targetReplicaNodes.Count, string.Join(", ", targetReplicaNodes.Select(n => n.NodeId)));
                            if (partialFileMetadataCore != null) {
                                foreach (var targetNode in targetReplicaNodes) {
                                    if (string.IsNullOrEmpty(targetNode.Address)) continue;
                                    var replicateRequest = new ReplicateChunkRequest {
                                        FileId = fileId, ChunkId = chunkInfo.ChunkId, ChunkIndex = chunkInfo.ChunkIndex, Data = chunkProto.Data,
                                        OriginalNodeId = _localNodeId, ParentFileMetadata = MapCoreToProtoMetadataPartial(partialFileMetadataCore)
                                    };
                                    _ = Task.Run(async () => { // Fire-and-forget task
                                        try {
                                            _logger.LogDebug("Replicating Chunk {Id} to Node {TargetId} ({Addr})", chunkInfo.ChunkId, targetNode.NodeId, targetNode.Address);
                                            // Pass CancellationToken.None or a new linked token with timeout for background task
                                            var reply = await _nodeClient.ReplicateChunkToNodeAsync(targetNode.Address, replicateRequest, CancellationToken.None);
                                            if (!reply.Success) _logger.LogWarning("Replication call for chunk {Id} to {Addr} failed: {Msg}", chunkInfo.ChunkId, targetNode.Address, reply.Message);
                                            else _logger.LogInformation("Replication call for chunk {Id} to {Addr} completed.", chunkInfo.ChunkId, targetNode.Address);
                                        } catch (Exception ex) { _logger.LogError(ex, "Exception in background replication task for chunk {Id} to {Addr}.", chunkInfo.ChunkId, targetNode.Address); }
                                    });
                                }
                            } else { _logger.LogError("Cannot trigger replication for Chunk {Id}: Parent metadata missing.", chunkInfo.ChunkId); }
                        }
                    } else { _logger.LogWarning("Received unknown payload case: {Case}", request.PayloadCase); }
                } // End await foreach

                _logger.LogDebug("Finished reading upload request stream.");
                if (!metadataReceived || fileMetadataProto == null || fileId == null)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Upload stream completed without receiving metadata."));
                if (expectedFileSize > 0 && totalBytesReceived != expectedFileSize)
                    _logger.LogWarning("Uploaded file size mismatch for File {Id}. Expected: {ExpSize}, Received: {RecSize}.", fileId, expectedFileSize, totalBytesReceived);

                var finalMetadataCore = new FileMetadataCore {
                    FileId = fileId, FileName = fileMetadataProto.FileName, FileSize = totalBytesReceived,
                    CreationTime = fileMetadataProto.CreationTime?.ToDateTime() ?? DateTime.UtcNow, ModificationTime = DateTime.UtcNow,
                    ContentType = fileMetadataProto.ContentType, ChunkSize = actualChunkSize, TotalChunks = chunkCount, State = FileStateCore.Available
                };
                await _metadataManager.SaveFileMetadataAsync(finalMetadataCore, context.CancellationToken);
                _logger.LogInformation("File {Name} (ID: {Id}) uploaded successfully. Final Size: {Size}, Chunks: {Count}",
                    finalMetadataCore.FileName, fileId, totalBytesReceived, chunkCount);
                return new UploadFileReply { Success = true, Message = "File uploaded successfully.", FileId = fileId };
            }
            catch (RpcException ex) {
                 _logger.LogError(ex, "gRPC error during file upload (FileId: {Id}): {Code} - {Detail}", fileId ?? "N/A", ex.StatusCode, ex.Status.Detail);
                 if (!string.IsNullOrEmpty(fileId)) { try { await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Error, CancellationToken.None); } catch { /* Ignore */ } }
                 return new UploadFileReply { Success = false, Message = $"Upload failed: {ex.Status.Detail}", FileId = fileId };
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Generic error during file upload (FileId: {Id})", fileId ?? "N/A");
                if (!string.IsNullOrEmpty(fileId)) { try { await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Error, CancellationToken.None); } catch { /* Ignore */ } }
                return new UploadFileReply { Success = false, Message = $"Upload failed: {ex.Message}", FileId = fileId };
            }
            finally { _logger.LogDebug("Exiting UploadFile method."); }
        }

        private async Task<List<ChunkStorageNode>> FindOnlinePeersAsync(List<ChunkStorageNode> peers, CancellationToken cancellationToken)
        {
            var onlinePeers = new ConcurrentBag<ChunkStorageNode>();
            var pingTasks = new List<Task>(); var semaphore = new SemaphoreSlim(10);
            foreach (var peer in peers) {
                await semaphore.WaitAsync(cancellationToken);
                pingTasks.Add(Task.Run(async () => {
                    try {
                        _logger.LogTrace("Pinging peer {Id} ({Addr}) for replication suitability.", peer.NodeId, peer.Address);
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); cts.CancelAfter(TimeSpan.FromSeconds(3));
                        // Corrected: Pass CancellationToken
                        var reply = await _nodeClient.PingNodeAsync(peer.Address, new PingRequest { SenderNodeId = _localNodeId }, cts.Token);
                        if (reply.Success) { onlinePeers.Add(peer); _logger.LogTrace("Peer {Id} is online.", peer.NodeId); }
                        else { _logger.LogTrace("Peer {Id} is offline/ping failed: {Reason}", peer.NodeId, reply.ResponderNodeId); }
                    } catch (OperationCanceledException) { _logger.LogTrace("Ping timed out for peer {Id}.", peer.NodeId); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Error pinging peer {Id} ({Addr}).", peer.NodeId, peer.Address); }
                    finally { semaphore.Release(); }
               }, cancellationToken));
            }
            await Task.WhenAll(pingTasks); return onlinePeers.ToList();
        }

        private static FileMetadata? MapCoreToProtoMetadataPartial(FileMetadataCore? core) {
            if (core == null) return null;
            return new FileMetadata {
                FileName = core.FileName ?? "", FileSize = core.FileSize,
                CreationTime = core.CreationTime > DateTime.MinValue ? Timestamp.FromDateTime(core.CreationTime.ToUniversalTime()) : null,
                ModificationTime = core.ModificationTime > DateTime.MinValue ? Timestamp.FromDateTime(core.ModificationTime.ToUniversalTime()) : null,
                ContentType = core.ContentType ?? "", ChunkSize = core.ChunkSize, TotalChunks = core.TotalChunks, State = (FileState)core.State
            };
        }

        private List<ChunkStorageNode> SelectReplicaTargetsFromInfo(List<ChunkStorageNode> onlinePeers, string chunkId, int replicasNeeded) {
            if (onlinePeers == null || !onlinePeers.Any() || replicasNeeded <= 0) return new List<ChunkStorageNode>();
            var orderedPeers = onlinePeers.OrderBy(p => p.NodeId).ToList();
            if (orderedPeers.Count <= replicasNeeded) return orderedPeers;
            int hashCode = Math.Abs(chunkId.GetHashCode()); int startIndex = hashCode % orderedPeers.Count;
            var selectedTargets = new List<ChunkStorageNode>();
            for (int i = 0; i < replicasNeeded; i++) { selectedTargets.Add(orderedPeers[(startIndex + i) % orderedPeers.Count]); }
            _logger.LogDebug("Selected {Count} replica targets for Chunk {Id}: {Targets}", selectedTargets.Count, chunkId, string.Join(", ", selectedTargets.Select(n => n.NodeId)));
            return selectedTargets;
        }
        #endregion

        #region DownloadFile Methods
        public override async Task DownloadFile(DownloadFileRequest request, IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
        {
            _logger.LogInformation("DownloadFile request for File ID: {FileId} from peer: {Peer}", request.FileId, context.Peer);
            if (string.IsNullOrEmpty(request.FileId)) throw new RpcException(new Status(StatusCode.InvalidArgument, "FileId cannot be empty."));
            try {
                var fileMetadataCore = await _metadataManager.GetFileMetadataAsync(request.FileId, context.CancellationToken);
                if (fileMetadataCore == null) throw new RpcException(new Status(StatusCode.NotFound, $"File metadata not found for ID '{request.FileId}'."));
                if (fileMetadataCore.State is FileStateCore.Deleting or FileStateCore.Deleted) throw new RpcException(new Status(StatusCode.NotFound, $"File '{fileMetadataCore.FileName}' (ID: {request.FileId}) is deleted or pending deletion."));
                if (fileMetadataCore.State != FileStateCore.Available) _logger.LogWarning("File {Id} requested for download is not Available (State: {State}). Proceeding...", request.FileId, fileMetadataCore.State);
                _logger.LogInformation("Found metadata for file: {Name}, Size: {Size}, Chunks: {Chunks}, State: {State}", fileMetadataCore.FileName, fileMetadataCore.FileSize, fileMetadataCore.TotalChunks, fileMetadataCore.State);

                await responseStream.WriteAsync(new DownloadFileReply { Metadata = MapFileMetadataCoreToProto(fileMetadataCore) }); // Use helper
                _logger.LogDebug("Sent metadata reply for File ID: {FileId}", request.FileId);

                var chunkInfos = (await _metadataManager.GetChunksMetadataForFileAsync(request.FileId, context.CancellationToken))?.OrderBy(c => c.ChunkIndex).ToList();
                if (chunkInfos == null || !chunkInfos.Any()) {
                    if (fileMetadataCore.FileSize > 0) {
                         _logger.LogError("Inconsistency: File metadata for {Id} (Size: {Size}) exists, but no chunk metadata.", request.FileId, fileMetadataCore.FileSize);
                         throw new RpcException(new Status(StatusCode.Internal, $"Inconsistency: Chunk metadata missing for file ID '{request.FileId}'."));
                    } else { _logger.LogInformation("File {Id} is empty (Size 0). Download complete.", request.FileId); return; }
                }
                _logger.LogDebug("Found {Count} chunk metadata entries for File ID: {Id}. Starting streaming...", chunkInfos.Count, request.FileId);

                foreach (var chunkInfo in chunkInfos) {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    _logger.LogDebug("Processing Chunk Index: {Index}, Chunk ID: {ChunkId}", chunkInfo.ChunkIndex, chunkInfo.ChunkId);
                    bool chunkSent = await TryStreamChunkFromAnySourceAsync(chunkInfo, responseStream, context);
                    if (!chunkSent) {
                         _logger.LogError("Failed to retrieve/stream Chunk {Id} (Index {Index}) from any source for File {FileId}. Aborting.", chunkInfo.ChunkId, chunkInfo.ChunkIndex, request.FileId);
                         throw new RpcException(new Status(StatusCode.Internal, $"Chunk {chunkInfo.ChunkIndex} (ID: {chunkInfo.ChunkId}) could not be retrieved."));
                    }
                }
                _logger.LogInformation("Finished streaming all {Count} chunks for File ID: {Id}", chunkInfos.Count, request.FileId);
            }
            catch (RpcException ex) { _logger.LogError(ex, "gRPC error during download for File ID {Id}: {Code} - {Detail}", request.FileId, ex.StatusCode, ex.Status.Detail); throw; }
            catch (OperationCanceledException) { _logger.LogInformation("DownloadFile for File ID {Id} was cancelled.", request.FileId); }
            catch (Exception ex) { _logger.LogError(ex, "Unexpected error during download for File ID: {Id}", request.FileId); throw new RpcException(new Status(StatusCode.Internal, $"Internal error during download: {ex.Message}")); }
        }

        private async Task<bool> TryStreamChunkFromAnySourceAsync(ChunkInfoCore chunkInfo, IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context) {
            var storageNodes = (await _metadataManager.GetChunkStorageNodesAsync(chunkInfo.FileId, chunkInfo.ChunkId, context.CancellationToken)).ToList();
            if (!storageNodes.Any()) { _logger.LogError("Chunk {Id} (Index {Index}) has no known storage locations!", chunkInfo.ChunkId, chunkInfo.ChunkIndex); return false; }
             _logger.LogDebug("Chunk {Id}: Found storage nodes: {Nodes}", chunkInfo.ChunkId, string.Join(",", storageNodes));
            if (storageNodes.Contains(_localNodeId)) {
                 _logger.LogTrace("Attempting local stream for Chunk {Id}.", chunkInfo.ChunkId);
                 if (await TryStreamLocalChunkAsync(chunkInfo, responseStream, context)) return true;
                 _logger.LogWarning("Failed local stream for Chunk {Id}. Trying remote.", chunkInfo.ChunkId);
            }
            var remoteNodeIds = storageNodes.Where(id => id != _localNodeId).ToList();
             _logger.LogTrace("Attempting remote stream for Chunk {Id} from nodes: {Nodes}", chunkInfo.ChunkId, string.Join(",", remoteNodeIds));
            foreach (var remoteNodeId in remoteNodeIds) {
                 context.CancellationToken.ThrowIfCancellationRequested();
                 var targetNodeInfo = _nodeOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == remoteNodeId);
                 if (targetNodeInfo == null || string.IsNullOrEmpty(targetNodeInfo.Address)) { _logger.LogWarning("No address for remote node {Id}. Skipping.", remoteNodeId); continue; }
                 if (await TryStreamRemoteChunkAsync(chunkInfo, targetNodeInfo, responseStream, context)) return true;
                  _logger.LogWarning("Failed remote stream for Chunk {Id} from {RemoteId}. Trying next.", chunkInfo.ChunkId, remoteNodeId);
            }
            _logger.LogError("Failed to stream Chunk {Id} from all locations: {Locations}", chunkInfo.ChunkId, string.Join(",", storageNodes)); return false;
        }

        private async Task<bool> TryStreamLocalChunkAsync(ChunkInfoCore chunkInfo, IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context) {
            Stream? chunkStream = null;
            try {
                // Corrected: Use 'with' expression now that ChunkInfoCore is a record
                var localChunkInfo = chunkInfo with { StoredNodeId = _localNodeId };
                chunkStream = await _dataManager.RetrieveChunkAsync(localChunkInfo, context.CancellationToken);
                if (chunkStream == null) { _logger.LogWarning("DataManager returned null stream for local Chunk {Id}.", chunkInfo.ChunkId); return false; }
                byte[] buffer = new byte[65536]; int bytesRead;
                while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0) {
                    var fileChunkProto = new VKR.Protos.FileChunk { FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, ChunkIndex = chunkInfo.ChunkIndex, Data = ByteString.CopyFrom(buffer, 0, bytesRead), Size = bytesRead };
                    await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto });
                }
                _logger.LogInformation("Finished streaming local Chunk {Id} (Index {Index})", chunkInfo.ChunkId, chunkInfo.ChunkIndex); return true;
            } catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogError(ex, "Error streaming local Chunk {Id}.", chunkInfo.ChunkId); return false; }
            finally { if (chunkStream != null) { await chunkStream.DisposeAsync(); } }
        }

        private async Task<bool> TryStreamRemoteChunkAsync(ChunkInfoCore chunkInfo, ChunkStorageNode targetNodeInfo, IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context) {
            _logger.LogInformation("Attempting remote fetch for Chunk {Id} from Node {RemoteId} ({Addr})", chunkInfo.ChunkId, targetNodeInfo.NodeId, targetNodeInfo.Address);
            AsyncServerStreamingCall<RequestChunkReply>? remoteCall = null;
            try {
                var remoteRequest = new RequestChunkRequest { FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken); cts.CancelAfter(TimeSpan.FromSeconds(60));
                remoteCall = await _nodeClient.RequestChunkFromNodeAsync(targetNodeInfo.Address, remoteRequest, cts.Token);
                if (remoteCall == null) { _logger.LogWarning("Failed to initiate RequestChunk call to node {Id}.", targetNodeInfo.NodeId); return false; }
                await foreach (var replyChunk in remoteCall.ResponseStream.ReadAllAsync(cts.Token)) {
                    if (replyChunk.Data != null && !replyChunk.Data.IsEmpty) {
                        var fileChunkProto = new VKR.Protos.FileChunk { FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, ChunkIndex = chunkInfo.ChunkIndex, Data = replyChunk.Data, Size = replyChunk.Data.Length };
                        await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto });
                    } else { _logger.LogWarning("Received empty data chunk from remote {Id} for Chunk {ChunkId}", targetNodeInfo.NodeId, chunkInfo.ChunkId); }
                }
                _logger.LogInformation("Finished streaming Chunk {Id} (Index {Index}) from remote {RemoteId}", chunkInfo.ChunkId, chunkInfo.ChunkIndex, targetNodeInfo.NodeId); return true;
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) { _logger.LogWarning("Remote node {Id} reported NotFound for Chunk {ChunkId}.", targetNodeInfo.NodeId, chunkInfo.ChunkId); return false; }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded or StatusCode.Unavailable) { _logger.LogWarning("gRPC error ({Code}) fetching chunk {Id} from {RemoteId}", ex.StatusCode, chunkInfo.ChunkId, targetNodeInfo.NodeId); return false; }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { _logger.LogInformation("Download cancelled while fetching {Id} from {RemoteId}.", chunkInfo.ChunkId, targetNodeInfo.NodeId); throw; }
             catch (OperationCanceledException) { _logger.LogWarning("Fetching chunk {Id} from {RemoteId} timed out/cancelled.", chunkInfo.ChunkId, targetNodeInfo.NodeId); return false; }
            catch (Exception ex) { _logger.LogError(ex, "Error fetching/streaming chunk {Id} from {RemoteId}", chunkInfo.ChunkId, targetNodeInfo.NodeId); return false; }
            finally { remoteCall?.Dispose(); }
        }
        #endregion

        #region DeleteFile Methods
        public override async Task<DeleteFileReply> DeleteFile(DeleteFileRequest request, ServerCallContext context)
        {
            string fileId = request.FileId;
             if (string.IsNullOrEmpty(fileId)) throw new RpcException(new Status(StatusCode.InvalidArgument, "FileId cannot be empty."));
            _logger.LogInformation("DeleteFile request for File ID: {FileId} from {Peer}.", fileId, context.Peer);
            FileMetadataCore? fileMeta = null; IEnumerable<ChunkInfoCore>? chunkInfos = null; var chunkNodeMap = new Dictionary<string, List<string>>();
            try {
                fileMeta = await _metadataManager.GetFileMetadataAsync(fileId, context.CancellationToken);
                if (fileMeta == null) { _logger.LogWarning("File {Id} not found during delete. Assuming deleted.", fileId); return new DeleteFileReply { Success = true, Message = "File not found, assumed deleted." }; }
                if (fileMeta.State is FileStateCore.Deleted or FileStateCore.Deleting) { _logger.LogInformation("File {Id} already in {State} state.", fileId, fileMeta.State); return new DeleteFileReply { Success = true, Message = $"File already in {fileMeta.State} state." }; }

                _logger.LogDebug("Marking File {Id} state as Deleting.", fileId);
                await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Deleting, context.CancellationToken);

                chunkInfos = await _metadataManager.GetChunksMetadataForFileAsync(fileId, context.CancellationToken);
                var nodesToDeleteFrom = new HashSet<string>();
                if (chunkInfos != null && chunkInfos.Any()) {
                     _logger.LogDebug("Fetching chunk locations for File ID {Id}...", fileId);
                     var locationTasks = chunkInfos.Select(async ci => new { ci.ChunkId, Nodes = await _metadataManager.GetChunkStorageNodesAsync(fileId, ci.ChunkId, context.CancellationToken) }).ToList();
                     var chunkLocations = await Task.WhenAll(locationTasks);
                    foreach (var locInfo in chunkLocations) {
                        if (locInfo.Nodes != null && locInfo.Nodes.Any()) { chunkNodeMap[locInfo.ChunkId] = locInfo.Nodes.ToList(); foreach (var nodeId in locInfo.Nodes) nodesToDeleteFrom.Add(nodeId); }
                        else { _logger.LogWarning("No storage nodes found for Chunk {Id} of File {FileId} during deletion.", locInfo.ChunkId, fileId); }
                    }
                    _logger.LogInformation("Notifying {Count} unique nodes for File {Id} deletion: {Nodes}", nodesToDeleteFrom.Count, fileId, string.Join(", ", nodesToDeleteFrom));
                    foreach (var nodeId in nodesToDeleteFrom) {
                        if (nodeId == _localNodeId) continue;
                        var nodeInfo = _nodeOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
                        if (nodeInfo == null || string.IsNullOrEmpty(nodeInfo.Address)) { _logger.LogWarning("No address for Node {Id}. Cannot trigger remote delete.", nodeId); continue; }
                        var chunksOnThisNode = chunkInfos.Where(ci => chunkNodeMap.TryGetValue(ci.ChunkId, out var nodes) && nodes.Contains(nodeId)).Select(ci => ci.ChunkId).ToList();
                        if (chunksOnThisNode.Any()) {
                            _logger.LogDebug("Notifying remote {Id} ({Addr}) to delete {Count} chunks.", nodeId, nodeInfo.Address, chunksOnThisNode.Count);
                            _ = Task.Run(async () => { // Fire-and-forget
                                foreach (var chunkId in chunksOnThisNode) {
                                    try { await _nodeClient.DeleteChunkOnNodeAsync(nodeInfo.Address, new DeleteChunkRequest { FileId = fileId, ChunkId = chunkId }, CancellationToken.None); }
                                    catch (Exception ex) { _logger.LogError(ex, "Exception in remote DeleteChunk task for Chunk {ChunkId} on {Addr}", chunkId, nodeInfo.Address); }
                                }
                            });
                        }
                    }
                } else { _logger.LogInformation("File {Id} has no chunk metadata. Skipping peer notification.", fileId); }

                if (chunkInfos != null && nodesToDeleteFrom.Contains(_localNodeId)) {
                     _logger.LogInformation("Deleting local chunk data for File ID: {Id}", fileId);
                     var localDeleteTasks = chunkInfos
                         .Where(ci => chunkNodeMap.TryGetValue(ci.ChunkId, out var nodes) && nodes.Contains(_localNodeId))
                         .Select(ci => Task.Run(async () => {
                             try { await _dataManager.DeleteChunkAsync(ci with { StoredNodeId = _localNodeId }, CancellationToken.None); } // Corrected use of 'with'
                             catch (Exception ex) { _logger.LogError(ex, "Error deleting local chunk data for Chunk {Id}.", ci.ChunkId); }
                         })).ToList();
                     await Task.WhenAll(localDeleteTasks);
                     _logger.LogInformation("Finished local chunk data deletion for File ID: {Id}", fileId);
                }

                _logger.LogInformation("Deleting local file metadata for File ID: {Id}", fileId);
                await _metadataManager.DeleteFileMetadataAsync(fileId, context.CancellationToken);

                _logger.LogInformation("DeleteFile process completed successfully for File ID: {Id}", fileId);
                return new DeleteFileReply { Success = true, Message = $"File {fileId} deletion processed." };
            }
            catch (RpcException) { throw; }
            catch (Exception ex) {
                _logger.LogError(ex, "Error during delete process for file {Id}.", fileId);
                 if (fileMeta != null) { try { await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Error, CancellationToken.None); } catch { /* Ignore */ } }
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to process file deletion: {ex.Message}"));
            }
        }
        #endregion

        #region Node Status and Config Methods
        public override async Task<GetNodeStatusesReply> GetNodeStatuses(GetNodeStatusesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetNodeStatuses request received from {Peer}", context.Peer);
            var reply = new GetNodeStatusesReply();
            // Use KnownNodes from options (assuming standardized names NodeId, Address)
            var knownNodes = _nodeOptions.KnownNodes ?? new List<ChunkStorageNode>();

            _logger.LogDebug("Checking status of {KnownNodeCount} known nodes.", knownNodes.Count);

            var pingTasks = new List<Task<NodeStatusInfo>>();

            // Add self status
            reply.Nodes.Add(new NodeStatusInfo {
                NodeId = _localNodeId,
                Address = _nodeOptions.Address ?? "Unknown", // Use standardized Address
                Status = NodeState.Online, // Assume self is online if service is running
                Details = "Online (Self)"
            });

            // Ping known peers concurrently
            foreach (var node in knownNodes) {
                // Skip self using standardized NodeId and Address
                if (node.NodeId == _localNodeId || IsSelfAddress(node.Address)) continue;

                pingTasks.Add(Task.Run(async () => {
                    NodeStatusInfo statusInfo;
                    try {
                        _logger.LogDebug("Pinging node {Id} at {Addr}", node.NodeId, node.Address);
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken); cts.CancelAfter(TimeSpan.FromSeconds(5));
                        // Corrected: Pass CancellationToken
                        var pingReply = await _nodeClient.PingNodeAsync(node.Address, new PingRequest { SenderNodeId = _localNodeId }, cts.Token);
                        statusInfo = new NodeStatusInfo { NodeId = node.NodeId, Address = node.Address, Status = pingReply.Success ? NodeState.Online : NodeState.Offline, Details = pingReply.Success ? "Online (Responded)" : $"Offline ({pingReply.ResponderNodeId ?? "No Response"})" };
                        _logger.LogDebug("Ping result for {Id}: {Status} - {Details}", node.NodeId, statusInfo.Status, statusInfo.Details);
                    } catch (OperationCanceledException) { _logger.LogWarning("Ping timed out for {Id} at {Addr}", node.NodeId, node.Address); statusInfo = new NodeStatusInfo { NodeId = node.NodeId, Address = node.Address, Status = NodeState.Offline, Details = "Offline (Timeout)" }; }
                    catch(Exception ex) { _logger.LogError(ex, "Error pinging {Id} at {Addr}", node.NodeId, node.Address); statusInfo = new NodeStatusInfo { NodeId = node.NodeId, Address = node.Address, Status = NodeState.Error, Details = $"Error ({ex.GetType().Name})" }; }
                    return statusInfo;
                }));
            }
            var results = await Task.WhenAll(pingTasks);
            reply.Nodes.AddRange(results);

            _logger.LogInformation("Returning status for {NodeCount} nodes.", reply.Nodes.Count);
            
            var sortedNodes = reply.Nodes.ToList();
            sortedNodes.Sort((a, b) => string.Compare(a.NodeId, b.NodeId, StringComparison.OrdinalIgnoreCase));
            reply.Nodes.Clear();
            reply.Nodes.AddRange(sortedNodes);

            return reply;
        }

        public override Task<GetNodeConfigurationReply> GetNodeConfiguration(GetNodeConfigurationRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetNodeConfiguration request received from {Peer}", context.Peer);
            try {
                var nodeOpts = _nodeOptions; var storageOpts = _storageOptions; var dhtOpts = _dhtOptions;
                var reply = new GetNodeConfigurationReply {
                    NodeId = nodeOpts.NodeId ?? "N/A", ListenAddress = nodeOpts.Address ?? "N/A", StorageBasePath = storageOpts.BasePath ?? "N/A",
                    ReplicationFactor = dhtOpts.ReplicationFactor, DefaultChunkSize = storageOpts.ChunkSize, Success = true
                };
                _logger.LogInformation("Returning Node Config: Id={Id}, Addr={Addr}, Storage={Storage}, RepFactor={RepFactor}, ChunkSize={ChunkSize}",
                    reply.NodeId, reply.ListenAddress, reply.StorageBasePath, reply.ReplicationFactor, reply.DefaultChunkSize);
                return Task.FromResult(reply);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error retrieving node configuration.");
                return Task.FromResult(new GetNodeConfigurationReply { Success = false, ErrorMessage = $"Internal error: {ex.Message}" });
            }
        }
        #endregion

        #region Helper Methods
        private bool IsSelfAddress(string? targetAddress) {
            var selfAddress = _nodeOptions?.Address;
            if (string.IsNullOrEmpty(targetAddress) || string.IsNullOrEmpty(selfAddress)) return false;
            try {
                 static string NormalizeAddr(string addr) {
                     addr = addr.ToLowerInvariant();
                     if (addr.StartsWith("http://")) addr = addr.Substring(7); if (addr.StartsWith("https://")) addr = addr.Substring(8);
                     addr = addr.Replace("*:", "0.0.0.0:"); addr = addr.Replace("[::]:", "::1:"); addr = addr.Replace("localhost:", "127.0.0.1:"); return addr;
                 }
                return NormalizeAddr(targetAddress) == NormalizeAddr(selfAddress);
            } catch (Exception ex) { _logger.LogError(ex, "Error comparing self address '{Self}' with target '{Target}'", selfAddress, targetAddress); return false; }
        }
        #endregion

        #region Placeholder / Unimplemented Methods
        public override Task<GetFileStatusReply> GetFileStatus(GetFileStatusRequest request, ServerCallContext context)
        {
            _logger.LogWarning("GetFileStatus method is not implemented.");
            throw new RpcException(new Status(StatusCode.Unimplemented, "GetFileStatus method is not implemented."));
        }
        #endregion
    }
}