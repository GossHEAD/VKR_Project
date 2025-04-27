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
using FileChunk = VKR.Protos.FileChunk;

namespace VKR_Node.Services;

public class StorageServiceImplTest(
    ILogger<StorageServiceImpl> logger,
    IMetadataManager metadataManager,
    IDataManager dataManager,
    INodeClient nodeClient,
    IOptions<NodeOptions> nodeOptions,
    IOptions<StorageOptions> storageOptions,
    IReplicationManager replicationManager,
    IOptions<DhtOptions> dhtOptions)
    : StorageService.StorageServiceBase
{
    //public IOptions<DhtOptions> DhtOptions { get; }
    private readonly ILogger<StorageServiceImpl> _logger = logger;
    private readonly IMetadataManager _metadataManager = metadataManager;
    private readonly IDataManager _dataManager = dataManager;
    private readonly INodeClient _nodeClient = nodeClient;
    private readonly NodeOptions _nodeOptions = nodeOptions.Value;
    private readonly IReplicationManager _replicationManager = replicationManager;
    private readonly StorageOptions _storageOptions = storageOptions.Value;
    private readonly DhtOptions _dhtOptions = dhtOptions.Value;

    private int ChunkSize => _storageOptions.ChunkSize;

    #region ListFiles Methods
    /// <summary>
    /// Handles requests to list available files, aggregating results from self and online peers.
    /// </summary>
    public override async Task<ListFilesReply> ListFiles(ListFilesRequest request, ServerCallContext context)
    {
        _logger.LogInformation("ListFiles request received from {Peer}. Aggregating from self and peers (excluding 'Deleting' state)...", context.Peer);

        var aggregatedFiles = new ConcurrentDictionary<string, FileMetadata>();
        var semaphore = new SemaphoreSlim(10); // Ограничение параллелизма

        try
        {
            // Обработка локальных файлов
            var localFiles = await _metadataManager.ListFilesAsync(context.CancellationToken);
            if (localFiles != null)
            {
                foreach (var fileCore in localFiles.Where(f => f.State != FileStateCore.Deleting))
                {
                    if (string.IsNullOrEmpty(fileCore.FileId))
                    {
                        _logger.LogWarning("Skipping local file with empty FileId.");
                        continue;
                    }
                    MergeFile(aggregatedFiles, MapFileMetadataCoreToProto(fileCore));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving local file list.");
        }
        
        var knownPeers = _nodeOptions.KnownNodes;
        var peerTasks = new List<Task>();

        foreach (var peer in knownPeers)
        {
            if (peer.NodeId == _nodeOptions.NodeId || string.IsNullOrEmpty(peer.Address)) 
                continue;

            await semaphore.WaitAsync(); // Ограничение параллелизма

            peerTasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var ctsPing = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                    ctsPing.CancelAfter(TimeSpan.FromSeconds(5));

                    // Проверка доступности пира
                    var pingReply = await _nodeClient.PingNodeAsync(peer.Address, new PingRequest { SenderNodeId = _nodeOptions.NodeId });
                    if (pingReply?.Success != true) return;
                    // Запрос списка файлов с таймаутом
                    using var ctsList = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                    ctsList.CancelAfter(TimeSpan.FromSeconds(15));

                    var sw = Stopwatch.StartNew();
                    var fileListReply = await _nodeClient.GetNodeFileListAsync(peer.Address, new GetNodeFileListRequest(), ctsList.Token);
                    _logger.LogDebug("Peer {PeerId} response time: {Elapsed} ms", peer.NodeId, sw.ElapsedMilliseconds);

                    if (fileListReply?.Files == null) return;

                    foreach (var fileProto in fileListReply.Files.Where(f => 
                        f.State != FileState.Deleting && !string.IsNullOrEmpty(f.FileId)))
                    {
                        MergeFile(aggregatedFiles, fileProto);
                    }
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Request to peer {PeerId} was canceled.", peer.NodeId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error querying peer {PeerId}", peer.NodeId);
                }
                finally
                {
                    semaphore.Release();
                }
            }, context.CancellationToken));
        }

        try { await Task.WhenAll(peerTasks); }
        catch (Exception ex) { _logger.LogError(ex, "Error waiting for peer tasks."); }

        // Формирование ответа
        var reply = new ListFilesReply();
        reply.Files.AddRange(aggregatedFiles.Values
            .OrderBy(f => f.FileName)
            .ThenBy(f => f.FileId));

        _logger.LogInformation("Returning {Count} files.", reply.Files.Count);
        return reply;
    }

    // Вспомогательный метод для агрегации
    private void MergeFile(ConcurrentDictionary<string, FileMetadata> aggregatedFiles, FileMetadata file)
    {
        aggregatedFiles.AddOrUpdate(file.FileId, file, (key, existing) =>
            file.ModificationTime.ToDateTimeOffset() > existing.ModificationTime.ToDateTimeOffset() 
                ? file 
                : existing
        );
    }
    
    private FileMetadata MapFileMetadataCoreToProto(FileMetadataCore fileCore)
    {
        return new FileMetadata // Use the correct proto message type
        {
            FileId = fileCore.FileId,
            FileName = fileCore.FileName,
            FileSize = fileCore.FileSize,
            CreationTime = Timestamp.FromDateTime(fileCore.CreationTime.ToUniversalTime()),
            ModificationTime = Timestamp.FromDateTime(fileCore.ModificationTime.ToUniversalTime()),
            ContentType = fileCore.ContentType ?? "",
            ChunkSize = fileCore.ChunkSize,
            TotalChunks = fileCore.TotalChunks,
            State = (FileState)fileCore.State 
        };
    }
    
    #endregion

    #region UploadFile Methods
    /// <summary>
    /// Determines an appropriate chunk size based on the total expected file size.
    /// </summary>
    private int DetermineOptimalChunkSize(long expectedFileSize)
    {
        const long kb = 1024;
        const long mb = 1024 * kb;
        const long gb = 1024 * mb;
        
        if (expectedFileSize <= 0) 
        {
            _logger.LogWarning("DetermineOptimalChunkSize called with non-positive file size ({FileSize} bytes). Falling back to default.", expectedFileSize);
            return _storageOptions.ChunkSize > 0 ? _storageOptions.ChunkSize : (int)(1 * mb);
        }
        else if (expectedFileSize <= 100 * mb) // Up to 100 mb
        {
            return (int)(1 * mb);    // Use 1mb chunks
        }
        else if (expectedFileSize <= 1 * gb)   // 100 mb to 1 gb
        {
            return (int)(4 * mb);    // Use 4mb chunks
        }
        else if (expectedFileSize <= 10 * gb)  // 1 gb to 10 gb
        {
            return (int)(16 * mb);   // Use 16mb chunks
        }
        else // Over 10 gb
        {
            return (int)(64 * mb);   // Use 64mb chunks
        }
    }
    
    /// <summary>
    /// Handles file uploads via client streaming.
    /// Receives metadata first, then chunks. Stores chunks locally and triggers replication.
    /// </summary>
    public override async Task<UploadFileReply> UploadFile(IAsyncStreamReader<UploadFileRequest> requestStream, ServerCallContext context)
    {
        _logger.LogInformation("UploadFile request initiated by client {Peer}", context.Peer);
        FileMetadata? fileMetadataProto = null;
        long totalBytesReceived = 0;
        int chunkCount = 0;
        string? fileId = null;
        var receivedChunks = new List<ChunkInfoCore>();
        int actualChunkSize = _storageOptions.ChunkSize;
        long expectedFileSize = 0;
        bool metadataReceived = false;
        int expectedChunkIndex = 0;

        try
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                _logger.LogDebug("Received message from client stream. PayloadCase: {PayloadCase}", request.PayloadCase);
                if (request.PayloadCase == UploadFileRequest.PayloadOneofCase.Metadata)
                {
                    _logger.LogDebug("Processing Metadata message...");
                    if (fileMetadataProto != null || metadataReceived)
                        throw new RpcException(new Status(StatusCode.InvalidArgument, "Metadata sent more than once."));
                    metadataReceived = true;
                    fileMetadataProto = request.Metadata;
                    fileId = Guid.NewGuid().ToString();
                    fileMetadataProto.FileId = fileId;

                    // Use ExpectedFileSize from client if provided (requires proto and client changes)
                    expectedFileSize = fileMetadataProto.ExpectedFileSize > 0 ? fileMetadataProto.ExpectedFileSize : 0;

                    actualChunkSize = expectedFileSize > 0
                        ? DetermineOptimalChunkSize(expectedFileSize)
                        : (_storageOptions.ChunkSize > 0 ? _storageOptions.ChunkSize : 1048576); // Fallback
                    fileMetadataProto.ChunkSize = actualChunkSize;

                    _logger.LogInformation("Received metadata for file: {FileName}, FileId: {FileId}, Using ChunkSize: {ChunkSize}, ExpectedSize: {ExpectedSize}",
                        fileMetadataProto.FileName, fileId, actualChunkSize, expectedFileSize > 0 ? expectedFileSize.ToString() : "Not Provided");
                }
                else if (request.PayloadCase == UploadFileRequest.PayloadOneofCase.Chunk)
                {
                    _logger.LogDebug("Processing Chunk message...");
                    if (!metadataReceived || fileMetadataProto == null || fileId == null)
                        throw new RpcException(new Status(StatusCode.InvalidArgument, "Chunk received before metadata."));
                    var chunkProto = request.Chunk;

                    if (chunkProto.ChunkIndex != expectedChunkIndex)
                        throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unexpected chunk index. Expected {expectedChunkIndex}, received {chunkProto.ChunkIndex}"));
                    expectedChunkIndex++;

                    _logger.LogInformation("Received Chunk Index: {Index}.", chunkProto.ChunkIndex);

                    var chunkInfo = new ChunkInfoCore
                    {
                        FileId = fileId,
                        ChunkId = chunkProto.ChunkId, // Assuming client generates this now
                        ChunkIndex = chunkProto.ChunkIndex,
                        Size = chunkProto.Data.Length,
                        StoredNodeId = _nodeOptions.NodeId
                    };
                    receivedChunks.Add(chunkInfo);

                    // Store locally
                    await using (var dataStream = new MemoryStream(chunkProto.Data.Memory.ToArray()))
                    {
                        await _dataManager.StoreChunkAsync(chunkInfo, dataStream, context.CancellationToken);
                    }

                    totalBytesReceived += chunkInfo.Size;
                    chunkCount++;

                    // --- Trigger Replication (Load-Aware - Simplified Ping Check) ---
                    int replicationFactor = Math.Max(2, _dhtOptions.ReplicationFactor);
                    int replicasNeeded = replicationFactor - 1;
                    if (replicasNeeded > 0)
                    {
                        var potentialPeers = (_nodeOptions.KnownNodes ?? new List<ChunkStorageNode>())
                                               .Where(n => n.NodeId != _nodeOptions.NodeId && !string.IsNullOrEmpty(n.Address))
                                               .ToList();
                        var onlinePeerNodes = new ConcurrentBag<ChunkStorageNode>();
                        var suitableTargetsInfo = new List<ChunkStorageNode>();
                        var pingTasks = new List<Task>();
                        foreach (var peer in potentialPeers)
                        {
                           pingTasks.Add(Task.Run(async () => {
                               try {
                                    _logger.LogTrace("Pinging peer {NodeId} ({Address}) for replication suitability.", peer.NodeId, peer.Address);
                                    var pingRequest = new PingRequest { SenderNodeId = _nodeOptions.NodeId };
                                    var options = new CallOptions(deadline: DateTime.UtcNow.AddSeconds(5));
                                    var reply = await _nodeClient.PingNodeAsync(peer.Address, pingRequest);

                                    if (reply.Success)
                                    {
                                        onlinePeerNodes.Add(peer);
                                        _logger.LogTrace("Peer {NodeId} is online.", peer.NodeId); 
                                    } else {
                                         _logger.LogTrace("Peer {NodeId} is offline or ping failed: {Reason}", peer.NodeId, reply.ResponderNodeId);
                                    } 
                               } catch (Exception ex) {
                                    _logger.LogWarning(ex, "Error pinging peer {NodeId} ({Address}) during replication target selection.", peer.NodeId, peer.Address);
                               }
                           }));
                        }
                        await Task.WhenAll(pingTasks);
                        suitableTargetsInfo = onlinePeerNodes.ToList();
                        var targetReplicaNodes = SelectReplicaTargetsFromInfo(suitableTargetsInfo, chunkInfo.ChunkId, replicasNeeded);

                        _logger.LogInformation("Chunk {ChunkIndex} ({ChunkId}): Storing locally. Triggering replication to {TargetCount} targets: {TargetNodes}",
                               chunkInfo.ChunkIndex, chunkInfo.ChunkId, targetReplicaNodes.Count, string.Join(", ", targetReplicaNodes.Select(n => n.NodeId)));

                        // Fetch/Construct Parent Metadata to send with replica
                        FileMetadataCore? parentMetadataCore = null;
                        if (fileMetadataProto != null) // Use the metadata received in this request
                        {
                             parentMetadataCore = new FileMetadataCore {
                                FileId = fileId, FileName = fileMetadataProto.FileName, FileSize = expectedFileSize, // Use expected size
                                CreationTime = fileMetadataProto.CreationTime?.ToDateTime() ?? DateTime.UtcNow, ModificationTime = DateTime.UtcNow, // Approx. times
                                ContentType = fileMetadataProto.ContentType, ChunkSize = actualChunkSize, TotalChunks = -1, // Indicate unknown total chunks yet
                                State = FileStateCore.Uploading // Indicate state during upload
                            };
                             _logger.LogDebug("Using temporary metadata from proto for replication trigger of Chunk {ChunkIndex}", chunkInfo.ChunkIndex);
                        } else {
                             _logger.LogWarning("Cannot construct parent metadata for replication trigger as initial metadata is null for File {FileId}", fileId);
                             // Optionally try fetching from DB as a fallback, but might not exist yet
                             // parentMetadataCore = await _metadataManager.GetFileMetadataAsync(fileId, context.CancellationToken);
                        }

                        foreach (var targetNode in targetReplicaNodes)
                        {
                            string targetAddress = targetNode.Address; // Get address from ChunkStorageNode
                            if (string.IsNullOrEmpty(targetAddress)) continue;

                            // Create ReplicateChunkRequest including Parent Metadata
                            var replicateRequest = new ReplicateChunkRequest
                            {
                                FileId = fileId,
                                ChunkId = chunkInfo.ChunkId,
                                ChunkIndex = chunkInfo.ChunkIndex,
                                Data = chunkProto.Data, // Pass the current chunk's data
                                OriginalNodeId = _nodeOptions.NodeId,
                                // Populate Parent File Metadata (ASSUMES PROTO UPDATED)
                                ParentFileMetadata = MapCoreToProtoMetadataPartial(parentMetadataCore) // Use mapping helper
                            };

                            // Fire-and-forget replication task
                            _ = Task.Run(async () => {
                                try {
                                    _logger.LogDebug("Replicating Chunk {ChunkId} to Node {TargetNodeId} ({TargetNodeAddress})", chunkInfo.ChunkId, targetNode.NodeId, targetAddress);
                                    var reply = await _nodeClient.ReplicateChunkToNodeAsync(targetAddress, replicateRequest);
                                    if (!reply.Success)
                                         _logger.LogWarning("Replication of chunk {ChunkId} to {TargetNodeAddress} failed: {Message}", chunkInfo.ChunkId, targetAddress, reply.Message);
                                    else
                                        _logger.LogInformation("Replication call for chunk {ChunkId} to {TargetNodeAddress} completed.", chunkInfo.ChunkId, targetAddress);
                                } catch (Exception ex) {
                                    _logger.LogError(ex, "Exception during background replication task for chunk {ChunkId} to {TargetNodeAddress}.", chunkInfo.ChunkId, targetAddress);
                                }
                            });
                        }
                    }
                     // Incrementing chunkCount/totalBytesReceived moved earlier
                }
                else // Handle unexpected message types
                {
                     _logger.LogWarning("Received unknown payload case from client stream: {PayloadCase}", request.PayloadCase);
                }
            } // End await foreach
            _logger.LogDebug("Finished reading request stream.");

            // Final checks and metadata saving
            if (fileMetadataProto == null || fileId == null || !metadataReceived)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Upload stream completed without metadata being processed."));
            if (expectedFileSize > 0 && totalBytesReceived != expectedFileSize)
            {
                // Log potential data loss but maybe allow completion? Or hard fail?
                _logger.LogWarning($"Uploaded file size mismatch. Expected: {expectedFileSize}, Received: {totalBytesReceived}. File {fileId} might be incomplete.");
            }

            _logger.LogInformation("Saving final file metadata for {FileId}. Final totalBytesReceived: {Size}, Final chunkCount: {Count}", fileId, totalBytesReceived, chunkCount);
            var finalMetadataCore = new FileMetadataCore
            {
                FileId = fileId,
                FileName = fileMetadataProto.FileName,
                FileSize = totalBytesReceived, // Use actual bytes received
                CreationTime = fileMetadataProto.CreationTime?.ToDateTime() ?? DateTime.UtcNow,
                ModificationTime = DateTime.UtcNow,
                ContentType = fileMetadataProto.ContentType,
                ChunkSize = actualChunkSize,
                TotalChunks = chunkCount, // Assign final count
                State = FileStateCore.Available
            };
            await _metadataManager.SaveFileMetadataAsync(finalMetadataCore, context.CancellationToken);

            _logger.LogDebug("Saving metadata for {Count} received chunks for file {FileId}...", receivedChunks.Count, fileId);
            foreach (var chunk in receivedChunks)
            {
                chunk.StoredNodeId = _nodeOptions.NodeId;
                await _metadataManager.SaveChunkMetadataAsync(chunk, new List<string> { _nodeOptions.NodeId }, context.CancellationToken);
            }
            _logger.LogDebug("Finished saving chunk metadata for file {FileId}.", fileId);

            _logger.LogInformation("File {FileName} (ID: {FileId}) uploaded successfully. Size: {FileSize}, Chunks: {ChunkCount}",
                finalMetadataCore.FileName, fileId, totalBytesReceived, chunkCount);
            return new UploadFileReply { Success = true, Message = "File uploaded successfully.", FileId = fileId };
        }
        catch (RpcException ex)
        {
             _logger.LogError(ex, "gRPC error during file upload: {StatusCode} - {Detail}", ex.StatusCode, ex.Status.Detail);
             // Consider cleanup
             return new UploadFileReply { Success = false, Message = $"Upload failed: {ex.Status.Detail}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file upload.");
             if (!string.IsNullOrEmpty(fileId)) {
                  _logger.LogWarning("Attempting cleanup for failed upload of file ID {FileId}", fileId);
                  try { await _metadataManager.DeleteFileMetadataAsync(fileId, CancellationToken.None); } catch { /* Ignore */ }
                  foreach(var chunk in receivedChunks) { try { await _dataManager.DeleteChunkAsync(chunk, CancellationToken.None); } catch { /* Ignore */ } }
             }
            return new UploadFileReply { Success = false, Message = $"Upload failed: {ex.Message}" };
        }
        finally
        {
             _logger.LogDebug("Exiting UploadFile method.");
        }
    }
    
    private static FileMetadata? MapCoreToProtoMetadataPartial(FileMetadataCore? core)
    {
        if (core == null) return null;

        return new FileMetadata // Use generated Proto type
        {
            FileName = core.FileName ?? string.Empty,
            FileSize = core.FileSize,
            CreationTime = Timestamp.FromDateTime(core.CreationTime.ToUniversalTime()), // Ensure UTC
            ModificationTime = Timestamp.FromDateTime(core.ModificationTime.ToUniversalTime()), // Ensure UTC
            ContentType = core.ContentType ?? string.Empty,
            ChunkSize = core.ChunkSize,
            TotalChunks = core.TotalChunks,
            State = (FileState)core.State // Cast Core enum to Proto enum
        };
    }
    
    private List<ChunkStorageNode> SelectReplicaTargetsFromInfo(List<ChunkStorageNode> onlinePeers, string chunkId, int replicasNeeded)
    {
        if (onlinePeers == null || !onlinePeers.Any() || replicasNeeded <= 0)
        {
            return new List<ChunkStorageNode>();
        }
        if (onlinePeers.Count <= replicasNeeded)
        {
            return onlinePeers;
        }
        int hashCode = Math.Abs(chunkId.GetHashCode());
        int startIndex = hashCode % onlinePeers.Count;
        var selectedTargets = new List<ChunkStorageNode>();
        for (int i = 0; i < replicasNeeded; i++)
        {
            selectedTargets.Add(onlinePeers[(startIndex + i) % onlinePeers.Count]);
        }
        return selectedTargets;
    }
    #endregion
    
    // Helper method to return replica node IDs
    private async Task<(ChunkInfoCore Chunk, List<string> ReplicaNodeIds)> ReplicateChunkToTargetsAsync(
        ByteString chunkData,
        ChunkInfoCore chunkInfo,
        List<ChunkStorageNode> targetNodes,
        CancellationToken cancellationToken)
    {
        var replicaNodeIds = new ConcurrentBag<string>();

        var tasks = targetNodes.Select(async node =>
        {
            if (string.IsNullOrWhiteSpace(node.Address)) return;
            try
            {
                var request = new ReplicateChunkRequest
                {
                    FileId = chunkInfo.FileId,
                    ChunkId = chunkInfo.ChunkId,
                    ChunkIndex = chunkInfo.ChunkIndex,
                    Data = chunkData,
                    OriginalNodeId = _nodeOptions.NodeId
                };

                var reply = await _nodeClient.ReplicateChunkToNodeAsync(node.Address, request);

                if (reply.Success)
                    replicaNodeIds.Add(node.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Replication to node {NodeId} failed.", node.NodeId);
            }
        });

        await Task.WhenAll(tasks);

        return (chunkInfo, replicaNodeIds.ToList());
    }

    /// <summary>
    /// Handles file downloads via server streaming.
    /// Retrieves metadata first, then streams chunks from local storage or potentially other nodes.
    /// </summary>
    public override async Task DownloadFile(DownloadFileRequest request, IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
        {
            _logger.LogInformation("Установка файла с ID:: {FileId} из узла: {Peer}", request.FileId, context.Peer);
            try
            {
                var fileMetadataCore = await _metadataManager.GetFileMetadataAsync(request.FileId, context.CancellationToken);
                if (fileMetadataCore == null) throw new RpcException(new Status(StatusCode.NotFound, $"File with ID '{request.FileId}' not found."));
                _logger.LogInformation("Found metadata for file: {FileName}, Size: {FileSize}, Chunks: {TotalChunks}",
                    fileMetadataCore.FileName, fileMetadataCore.FileSize, fileMetadataCore.TotalChunks);
                
                var fileMetadataProto = new FileMetadata {
                    FileId = fileMetadataCore.FileId, FileName = fileMetadataCore.FileName, FileSize = fileMetadataCore.FileSize,
                    CreationTime = Timestamp.FromDateTime(fileMetadataCore.CreationTime.ToUniversalTime()),
                    ModificationTime = Timestamp.FromDateTime(fileMetadataCore.ModificationTime.ToUniversalTime()),
                    ContentType = fileMetadataCore.ContentType ?? "", ChunkSize = fileMetadataCore.ChunkSize, TotalChunks = fileMetadataCore.TotalChunks,
                    State = (FileState)fileMetadataCore.State
                };
                fileMetadataProto.FileId = fileMetadataCore.FileId;
                fileMetadataProto.FileName = fileMetadataCore.FileName;
                fileMetadataProto.FileSize = fileMetadataCore.FileSize;
                fileMetadataProto.CreationTime = Timestamp.FromDateTime(fileMetadataCore.CreationTime.ToUniversalTime());
                fileMetadataProto.ModificationTime = Timestamp.FromDateTime(fileMetadataCore.ModificationTime.ToUniversalTime());
                fileMetadataProto.ContentType = fileMetadataCore.ContentType ?? "";
                fileMetadataProto.ChunkSize = fileMetadataCore.ChunkSize;
                fileMetadataProto.TotalChunks = fileMetadataCore.TotalChunks;
                fileMetadataProto.State = (FileState)fileMetadataCore.State;
                
                await responseStream.WriteAsync(new DownloadFileReply { Metadata = fileMetadataProto });
                
                var chunkInfos = await _metadataManager.GetChunksMetadataForFileAsync(request.FileId, context.CancellationToken);
                if (chunkInfos == null || !chunkInfos.Any()) throw new RpcException(new Status(StatusCode.Internal, $"Chunk metadata missing for file ID '{request.FileId}'."));

                _logger.LogDebug("Found {ChunkCount} chunk metadata entries for File ID: {FileId}. Streaming data...", chunkInfos.Count(), request.FileId);

                foreach (var chunkInfo in chunkInfos.OrderBy(c => c.ChunkIndex)) 
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    bool chunkSent = false;
                    Stream? chunkStream = null; 
                    // --- Check if chunk is stored locally ---
                    var storageNodes = (await _metadataManager.GetChunkStorageNodesAsync(chunkInfo.FileId, chunkInfo.ChunkId, context.CancellationToken)).ToList();
                    bool isLocal = storageNodes.Contains(_nodeOptions.NodeId);

                    if (isLocal)
                    {
                        _logger.LogDebug("Retrieving chunk {ChunkIndex} ({ChunkId}) data locally.", chunkInfo.ChunkIndex, chunkInfo.ChunkId);
                        try
                        {
                            var chunkInfoForLocalRetrieval = new ChunkInfoCore { FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, StoredNodeId = _nodeOptions.NodeId };
                            chunkStream = await _dataManager.RetrieveChunkAsync(chunkInfoForLocalRetrieval, context.CancellationToken);

                            if (chunkStream != null)
                            {
                                byte[] buffer = new byte[65536]; //kb
                                int bytesRead;
                                while ((bytesRead = await chunkStream.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0)
                                {
                                    var fileChunkProto = new FileChunk {
                                        FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, ChunkIndex = chunkInfo.ChunkIndex,
                                        Data = ByteString.CopyFrom(buffer, 0, bytesRead), Size = bytesRead 
                                    };
                                    await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto });
                                }
                                chunkSent = true;
                                _logger.LogDebug("Finished streaming local chunk {ChunkIndex} ({ChunkId})", chunkInfo.ChunkIndex, chunkInfo.ChunkId);
                            }
                            else {
                                 _logger.LogWarning("Local chunk data stream for Chunk {ChunkId} was null, despite metadata indicating local storage.", chunkInfo.ChunkId);
                            }
                        }
                        catch (Exception ex) {
                            _logger.LogError(ex, "Error retrieving or streaming local chunk {ChunkId}.", chunkInfo.ChunkId);
                        }
                        finally {
                            if (chunkStream != null) { await chunkStream.DisposeAsync(); }
                        }
                    } 

                    // --- If not sent locally, try remote nodes ---
                    if (!chunkSent)
                    {
                        _logger.LogDebug("Chunk {ChunkIndex} ({ChunkId}) not found or retrieved locally. Attempting remote fetch.", chunkInfo.ChunkIndex, chunkInfo.ChunkId);
                        var remoteNodeIds = storageNodes.Where(id => id != _nodeOptions.NodeId).ToList();

                        if (!remoteNodeIds.Any())
                        {
                            _logger.LogError("Chunk {ChunkId} not stored locally and no remote nodes found in metadata!", chunkInfo.ChunkId);
                            throw new RpcException(new Status(StatusCode.Internal, $"Chunk {chunkInfo.ChunkIndex} (ID: {chunkInfo.ChunkId}) is unavailable. No known storage locations."));
                        }
                        
                        foreach (var remoteNodeId in remoteNodeIds)
                        {
                             context.CancellationToken.ThrowIfCancellationRequested();

                             var targetNodeInfo = _nodeOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == remoteNodeId);
                             if (targetNodeInfo == null || string.IsNullOrEmpty(targetNodeInfo.Address))
                             {
                                 _logger.LogWarning("Could not find address for remote node {RemoteNodeId} holding chunk {ChunkId}. Skipping.", remoteNodeId, chunkInfo.ChunkId);
                                 continue;
                             }

                             _logger.LogInformation("Attempting to fetch Chunk {ChunkId} from remote node {RemoteNodeId} ({RemoteNodeAddress})", chunkInfo.ChunkId, remoteNodeId, targetNodeInfo.Address);

                             AsyncServerStreamingCall<RequestChunkReply>? remoteCall = null;
                             try
                             {
                                 var remoteRequest = new RequestChunkRequest { FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId };
                                 remoteCall = await _nodeClient.RequestChunkFromNodeAsync(targetNodeInfo.Address, remoteRequest, context.CancellationToken);

                                 if (remoteCall != null)
                                 {
                                     await foreach (var replyChunk in remoteCall.ResponseStream.ReadAllAsync(context.CancellationToken))
                                     {
                                         if (replyChunk.Data != null && replyChunk.Data.Length > 0)
                                         {
                                             var fileChunkProto = new FileChunk {
                                                 FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, ChunkIndex = chunkInfo.ChunkIndex,
                                                 Data = replyChunk.Data, Size = replyChunk.Data.Length
                                             };
                                             await responseStream.WriteAsync(new DownloadFileReply { Chunk = fileChunkProto });
                                         }
                                     }
                                     chunkSent = true; 
                                     _logger.LogInformation("Finished streaming chunk {ChunkId} fetched from remote node {RemoteNodeId}", chunkInfo.ChunkId, remoteNodeId);
                                     break; 
                                 }
                                 else
                                 {
                                     _logger.LogWarning("Failed to initiate RequestChunk call to node {RemoteNodeId} for chunk {ChunkId}.", remoteNodeId, chunkInfo.ChunkId);
                                 }
                             }
                             catch (RpcException ex) {
                                 _logger.LogWarning(ex, "gRPC error fetching chunk {ChunkId} from node {RemoteNodeId}: {StatusCode}", chunkInfo.ChunkId, remoteNodeId, ex.StatusCode);
                             }
                             catch (OperationCanceledException) {
                                  _logger.LogInformation("Fetching chunk {ChunkId} from {RemoteNodeId} was cancelled.", chunkInfo.ChunkId, remoteNodeId);
                                  throw; 
                             }
                             catch (Exception ex) {
                                 _logger.LogError(ex, "Error fetching or streaming chunk {ChunkId} from remote node {RemoteNodeId}", chunkInfo.ChunkId, remoteNodeId);
                             }
                             finally {
                                 remoteCall?.Dispose();
                             }
                        } 

                        if (!chunkSent) 
                        {
                             _logger.LogError("Failed to retrieve chunk {ChunkId} from all known locations ({Locations})!", chunkInfo.ChunkId, string.Join(",", storageNodes));
                             throw new RpcException(new Status(StatusCode.Internal, $"Chunk {chunkInfo.ChunkIndex} (ID: {chunkInfo.ChunkId}) could not be retrieved from any known source."));
                        }
                    } 
                } 

                _logger.LogInformation("Finished streaming all chunks for File ID: {FileId}", request.FileId);
            }
            catch (RpcException ex) 
            {
                 _logger.LogError(ex, "gRPC error during file download for File ID {FileId}: {StatusCode} - {Detail}", request.FileId, ex.StatusCode, ex.Status.Detail);
                 throw;
            }
            catch (OperationCanceledException) {
                 _logger.LogInformation("DownloadFile for File ID {FileId} was cancelled.", request.FileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during file download for File ID: {FileId}", request.FileId);
                throw new RpcException(new Status(StatusCode.Internal, $"An internal error occurred during download: {ex.Message}"));
            }
        }


    /// <summary>
    /// Handles requests to delete a file.
    /// Deletes local data/metadata and triggers deletion on replica nodes.
    /// </summary>
    public override async Task<DeleteFileReply> DeleteFile(DeleteFileRequest request, ServerCallContext context)
    {
        string fileId = request.FileId;
        _logger.LogInformation("DeleteFile request for File ID: {FileId} from {Peer}. Using notify-then-cleanup approach.", fileId, context.Peer);

        FileMetadataCore? fileMeta = null;
        var chunkNodeMap = new Dictionary<string, List<string>>();

        try
        {
            // --- Step 1: Check current state ---
            fileMeta = await _metadataManager.GetFileMetadataAsync(fileId, context.CancellationToken);

            if (fileMeta == null)
            {
                _logger.LogWarning("File {FileId} not found. Cannot delete.", fileId);
                return new DeleteFileReply { Success = true, Message = "File not found, assumed already deleted." };
            }

            // Optional: Check if already deleting, though this process should be quick now
            // if (fileMeta.State == FileStateCore.Deleting) { ... }

            // --- Step 2: Mark file state as Deleting (optional but good practice) ---
            _logger.LogInformation("Marking File {FileId} state as Deleting.", fileId);
            await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Deleting, context.CancellationToken);

            // --- Step 3: Get Chunk Locations and Notify Remote Nodes (Fire-and-Forget) ---
            var chunkInfos = await _metadataManager.GetChunksMetadataForFileAsync(fileId, context.CancellationToken);
            if (chunkInfos != null && chunkInfos.Any())
            {
                var nodesToDeleteFrom = new HashSet<string>();

                foreach (var chunkInfo in chunkInfos) // Populate map efficiently
                {
                    var nodeIds = await _metadataManager.GetChunkStorageNodesAsync(fileId, chunkInfo.ChunkId, context.CancellationToken);
                    if (nodeIds != null && nodeIds.Any())
                    {
                        chunkNodeMap[chunkInfo.ChunkId] = nodeIds.ToList();
                        foreach (var nodeId in nodeIds) nodesToDeleteFrom.Add(nodeId);
                    }
                }

                _logger.LogInformation("Notifying {NodeCount} unique nodes for file {FileId} deletion: {NodeIds}",
                   nodesToDeleteFrom.Count, fileId, string.Join(", ", nodesToDeleteFrom));

                // Trigger asynchronous DeleteChunk on remote nodes (fire and forget)
                foreach (var nodeId in nodesToDeleteFrom)
                {
                    if (nodeId == _nodeOptions.NodeId) continue; // Skip self

                    var nodeInfo = _nodeOptions.KnownNodes?.FirstOrDefault(n => n.NodeId == nodeId);
                    if (nodeInfo == null || string.IsNullOrEmpty(nodeInfo.Address)) {
                         _logger.LogWarning("Could not find address for Node ID {NodeId}. Cannot trigger remote delete notify.", nodeId);
                         continue;
                    }
                    string nodeAddress = nodeInfo.Address;

                    var chunksOnThisNode = chunkInfos
                        .Where(ci => chunkNodeMap.ContainsKey(ci.ChunkId) && chunkNodeMap[ci.ChunkId].Contains(nodeId))
                        .Select(ci => ci.ChunkId)
                        .ToList();

                    if (chunksOnThisNode.Any())
                    {
                         _logger.LogInformation("Notifying remote node {NodeId} ({NodeAddress}) to delete {ChunkCount} chunks.", nodeId, nodeAddress, chunksOnThisNode.Count);

                        // Fire and forget task for each node/chunk combination
                        _ = Task.Run(async () => { // Use Task.Run for fire-and-forget
                            foreach (var chunkId in chunksOnThisNode) {
                                var deleteChunkRequest = new DeleteChunkRequest { FileId = fileId, ChunkId = chunkId };
                                try {
                                    var reply = await _nodeClient.DeleteChunkOnNodeAsync(nodeAddress, deleteChunkRequest);
                                    if (!reply.Success)
                                         _logger.LogWarning("Remote DeleteChunk notification failed for Chunk {ChunkId} on Node {NodeAddress}: {Message}", chunkId, nodeAddress, reply.Message);
                                    // No need to track success here
                                } catch (Exception ex) {
                                    _logger.LogError(ex, "Exception during remote DeleteChunk notification for Chunk {ChunkId} on Node {NodeAddress}", chunkId, nodeAddress);
                                }
                            }
                        });
                    }
                } // End foreach node
            } // End if (chunkInfos found)

            // --- Step 4: Delete Local Chunk Data ---
            if (chunkInfos != null)
            {
                 _logger.LogInformation("Attempting to delete local chunk data files for File ID: {FileId}", fileId);
                 foreach (var chunkInfo in chunkInfos)
                 {
                     // Use the previously built map if available, otherwise query
                     List<string>? storageNodes = null;
                     if (chunkNodeMap != null && chunkNodeMap.TryGetValue(chunkInfo.ChunkId, out var value)) {
                         storageNodes = value;
                     } else {
                         // Fallback query if map wasn't built (e.g., error during chunk location fetch)
                         storageNodes = (await _metadataManager.GetChunkStorageNodesAsync(fileId, chunkInfo.ChunkId, CancellationToken.None))?.ToList();
                     }

                     if (storageNodes != null && storageNodes.Contains(_nodeOptions.NodeId))
                     {
                          _logger.LogDebug("Deleting local chunk data file for Chunk {ChunkId}", chunkInfo.ChunkId);
                          try
                          {
                              var localChunkInfo = new ChunkInfoCore { FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, StoredNodeId = _nodeOptions.NodeId };
                              await _dataManager.DeleteChunkAsync(localChunkInfo, context.CancellationToken);
                          }
                          catch (Exception ex) { _logger.LogError(ex, "Error deleting local chunk data file for Chunk {ChunkId}. Continuing cleanup.", chunkInfo.ChunkId); }
                     }
                 }
            }

            // --- Step 5: Delete Local File Metadata (which should cascade) ---
            _logger.LogInformation("Deleting local file metadata entry for File ID: {FileId}", fileId);
            await _metadataManager.DeleteFileMetadataAsync(fileId, context.CancellationToken);

            return new DeleteFileReply { Success = true, Message = $"Local deletion processed for {fileId}. Remote deletion notifications sent." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during delete process for file {FileId}.", fileId);
            
             if (fileMeta is { State: FileStateCore.Deleting }) { 
                  try { await _metadataManager.UpdateFileStateAsync(fileId, FileStateCore.Error, CancellationToken.None); } catch { /* Ignore */ }
             }
            throw new RpcException(new Status(StatusCode.Internal, $"Failed to process file deletion: {ex.Message}"));
        }
    }
    
    /// <summary>
    /// Gets the status of nodes in the network by pinging known peers.
    /// </summary>
    public override async Task<GetNodeStatusesReply> GetNodeStatuses(GetNodeStatusesRequest request, ServerCallContext context)
        {
            _logger.LogInformation("GetNodeStatuses request received from {Peer}", context.Peer);
            var reply = new GetNodeStatusesReply();
            // Use KnownNodes from options (now fixed)
            var knownNodes = _nodeOptions.KnownNodes ?? new List<ChunkStorageNode>();

            _logger.LogDebug("Checking status of {KnownNodeCount} known nodes.", knownNodes.Count);

            var pingTasks = new List<Task<NodeStatusInfo>>();

            // Add self
            reply.Nodes.Add(new NodeStatusInfo {
                NodeId = _nodeOptions.NodeId, Address = GetSelfAddress(),
                Status = NodeState.Online, Details = "Online (Self)"
            });
            
            foreach (var node in knownNodes)
            {
                if (IsSelf(node.Address)) continue;

                pingTasks.Add(Task.Run(async () => { 
                    _logger.LogDebug("Pinging node {NodeId} at {NodeAddress}", node.NodeId, node.Address);
                    var pingRequest = new PingRequest { SenderNodeId = _nodeOptions.NodeId };
                    var pingReply = await _nodeClient.PingNodeAsync(node.Address, pingRequest); 

                    var statusInfo = new NodeStatusInfo {
                        NodeId = node.NodeId, Address = node.Address,
                        Status = pingReply.Success ? NodeState.Online : NodeState.Offline,
                        Details = pingReply.Success ? $"Online (Responded)" : $"Offline ({pingReply.ResponderNodeId})"
                    };
                    _logger.LogDebug("Ping result for {NodeId}: {Status} - {Details}", node.NodeId, statusInfo.Status, statusInfo.Details);
                    return statusInfo;
                }));
            }

            // Wait for all pings
            var results = await Task.WhenAll(pingTasks);
            reply.Nodes.AddRange(results);

            _logger.LogInformation("Returning status for {NodeCount} nodes.", reply.Nodes.Count);
            return reply;
        }

    public override Task<GetNodeConfigurationReply> GetNodeConfiguration(GetNodeConfigurationRequest request, ServerCallContext context)
    {
        _logger.LogInformation("GetNodeConfiguration request received from {Peer}", context.Peer);

        try
        {
            var nodeOpts = _nodeOptions;   
            var storageOpts = _storageOptions;
            var dhtOpts = _dhtOptions;    

            var reply = new GetNodeConfigurationReply
            {
                NodeId = nodeOpts.NodeId ?? "N/A",
                ListenAddress = nodeOpts.Address ?? "N/A", 
                StorageBasePath = storageOpts.BasePath ?? "N/A",
                ReplicationFactor = dhtOpts.ReplicationFactor,
                DefaultChunkSize = storageOpts.ChunkSize,
                Success = true
            };

            _logger.LogInformation("Returning Node Configuration: NodeId={NodeId}, Address={Address}, Storage={Storage}, RepFactor={RepFactor}, ChunkSize={ChunkSize}",
                reply.NodeId, reply.ListenAddress, reply.StorageBasePath, reply.ReplicationFactor, reply.DefaultChunkSize);

            return Task.FromResult(reply);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving node configuration settings.");
            return Task.FromResult(new GetNodeConfigurationReply
            {
                Success = false,
                ErrorMessage = $"Internal server error retrieving configuration: {ex.Message}"
            });
        }
    }
        
    /// <summary>
    /// Checks if the given address corresponds to the current node.
    /// This is a basic implementation and might need refinement based on how addresses are formatted.
    /// </summary>
    private bool IsSelf(string? targetAddress)
    {
        if (string.IsNullOrEmpty(targetAddress) || string.IsNullOrEmpty(_nodeOptions.Address)) return false;
        try {
            // Normalize addresses for comparison
            var selfUri = new Uri(_nodeOptions.Address.Replace("*", "localhost").Replace("0.0.0.0", "localhost").Replace("[::]", "localhost"));
            var targetUri = new Uri(targetAddress.StartsWith("http") ? targetAddress : "http://" + targetAddress);
            return Uri.Compare(selfUri, targetUri, UriComponents.HostAndPort, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
        } catch (UriFormatException ex) {
            _logger.LogWarning(ex, "Could not compare addresses due to format issue: Self='{SelfAddr}', Target='{TargetAddr}'", _nodeOptions.Address, targetAddress);
            return false; // Treat as not self if format is bad
        }
    }
    private string GetSelfAddress() => _nodeOptions.Address ?? "Unknown";

    // --- Placeholder / Unimplemented Methods ---
    public override Task<GetFileStatusReply> GetFileStatus(GetFileStatusRequest request, ServerCallContext context)
    {
        _logger.LogWarning("GetFileStatus method is not implemented.");
        throw new RpcException(new Status(StatusCode.Unimplemented, "GetFileStatus method is not implemented."));
    }
        
        // --- Placeholder / Unimplemented Methods ---
}
