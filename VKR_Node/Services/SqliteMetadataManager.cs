using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using VKR_Core.Enums; 
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Persistance;
using VKR_Node.Persistance.Entities;

namespace VKR_Node.Services
{
    /// <summary>
    /// Manages metadata storage and retrieval using SQLite via Entity Framework Core.
    /// </summary>
    public class SqliteMetadataManager : IMetadataManager
    {
        private readonly IDbContextFactory<NodeDbContext> _contextFactory;
        private readonly ILogger<SqliteMetadataManager> _logger;
        private readonly string _localNodeId;
        private readonly DatabaseOptions _databaseOptions;
        
        private readonly ConcurrentDictionary<string, (object Value, DateTime Expiry)> _cacheItems = new();
        private readonly TimeSpan _defaultCacheExpiry = TimeSpan.FromMinutes(1);
        private int _lastCacheCleanup = Environment.TickCount;

        /// <summary>
        /// Creates a new instance of the SqliteMetadataManager.
        /// </summary>
        public SqliteMetadataManager(
            IDbContextFactory<NodeDbContext> contextFactory,
            ILogger<SqliteMetadataManager> logger,
            IOptions<NodeIdentityOptions> nodeOptions,
            IOptions<DatabaseOptions> databaseOptions)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _localNodeId = nodeOptions.Value?.NodeId ?? throw new ArgumentException("NodeId is not configured", nameof(nodeOptions));
            _databaseOptions = databaseOptions.Value;
            
            _logger.LogInformation("SqliteMetadataManager initialized for Node {NodeId} using database path: {Path}", 
                _localNodeId, _databaseOptions.DatabasePath);
        }

        #region Initialization

        /// <summary>
        /// Initializes the metadata manager, ensuring the database exists and migrations are applied.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Initializing metadata database...");
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                // Ensure database exists and apply pending migrations
                await context.Database.MigrateAsync(cancellationToken);
                
                _logger.LogInformation("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize database or apply migrations");
                throw; // Re-throw critical initialization error
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Executes an operation within a transaction, with proper error handling and logging.
        /// </summary>
        private async Task<T> ExecuteInTransactionAsync<T>(
            string operationName,
            Func<NodeDbContext, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            
            try
            {
                var result = await operation(context, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(ex, "Concurrency conflict during {Operation}", operationName);
                throw;
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogWarning(ex, "Constraint violation during {Operation} - likely concurrent operation", operationName);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Error during {Operation}", operationName);
                throw;
            }
        }

        /// <summary>
        /// Maps a FileEntity to a FileModel object.
        /// </summary>
        private FileModel MapFileEntityToCore(FileEntity entity)
        {
            return new FileModel
            {
                FileId = entity.FileId,
                FileName = entity.FileName,
                FileSize = entity.FileSize,
                CreationTime = entity.CreationTime,
                ModificationTime = entity.ModificationTime,
                ContentType = entity.ContentType,
                ChunkSize = (int)entity.ChunkSize,
                TotalChunks = entity.TotalChunks,
                State = (FileStateCore)entity.State
            };
        }

        /// <summary>
        /// Maps a ChunkEntity to a ChunkModel object.
        /// </summary>
        private ChunkModel MapChunkEntityToCore(ChunkEntity entity, string storedNodeId = "")
        {
            return new ChunkModel
            {
                FileId = entity.FileId,
                ChunkId = entity.ChunkId,
                ChunkIndex = entity.ChunkIndex,
                Size = entity.Size,
                ChunkHash = entity.ChunkHash,
                StoredNodeId = storedNodeId
            };
        }

        /// <summary>
        /// Maps a NodeEntity to a NodeModel object.
        /// </summary>
        private NodeModel MapNodeEntityToCore(NodeEntity entity)
        {
            return new NodeModel
            {
                Id = entity.NodeId,
                Address = entity.Address,
                State = (NodeStateCore)entity.State,
                LastSeen = entity.LastSeen,
                LastSuccessfulPingTimestamp = entity.LastSuccessfulPingTimestamp,
                DiskSpaceAvailableBytes = entity.DiskSpaceAvailableBytes,
                DiskSpaceTotalBytes = entity.DiskSpaceTotalBytes,
                StoredChunkCount = entity.StoredChunkCount
            };
        }

        /// <summary>
        /// Updates fields of a FileEntity from a FileModel object.
        /// </summary>
        private void UpdateFileMetadataFields(FileEntity entity, FileModel metadata)
        {
            entity.FileName = metadata.FileName;
            entity.FileSize = metadata.FileSize;
            entity.ModificationTime = metadata.ModificationTime.ToUniversalTime();
            entity.ContentType = metadata.ContentType;
            entity.ChunkSize = metadata.ChunkSize;
            entity.TotalChunks = metadata.TotalChunks;
            
            // Only update state in specific cases
            if (entity.State == (int)FileStateCore.Incomplete && metadata.State != FileStateCore.Incomplete)
            {
                entity.State = (int)metadata.State;
            }
            else if (entity.State == (int)FileStateCore.Unknown)
            {
                entity.State = (int)metadata.State;
            }
        }

        /// <summary>
        /// Retrieves a value from the cache if available and not expired.
        /// </summary>
        private T? GetFromCache<T>(string key) where T : class
        {
            if (_cacheItems.TryGetValue(key, out var item) && item.Expiry > DateTime.UtcNow)
            {
                return item.Value as T;
            }
            return null;
        }

        /// <summary>
        /// Adds a value to the cache with an optional expiry time.
        /// </summary>
        private void AddToCache<T>(string key, T value, TimeSpan? expiry = null) where T : class
        {
            var expiryTime = DateTime.UtcNow + (expiry ?? _defaultCacheExpiry);
            _cacheItems[key] = (value, expiryTime);
            
            // Periodically clean up expired cache items (roughly every minute)
            var ticksNow = Environment.TickCount;
            if (Math.Abs(ticksNow - _lastCacheCleanup) > 60000)
            {
                _lastCacheCleanup = ticksNow;
                CleanupExpiredCache();
            }
        }

        /// <summary>
        /// Removes expired items from the cache.
        /// </summary>
        private void CleanupExpiredCache()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _cacheItems
                .Where(kvp => kvp.Value.Expiry < now)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in expiredKeys)
            {
                _cacheItems.TryRemove(key, out _);
            }
            
            _logger.LogTrace("Cache cleanup: removed {Count} expired items, {Remaining} remaining", 
                expiredKeys.Count, _cacheItems.Count);
        }

        /// <summary>
        /// Invalidates a specific cache entry or a range of entries matching a prefix.
        /// </summary>
        private void InvalidateCache(string keyOrPrefix, bool isPrefix = false)
        {
            if (isPrefix)
            {
                var keysToRemove = _cacheItems.Keys
                    .Where(k => k.StartsWith(keyOrPrefix, StringComparison.Ordinal))
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    _cacheItems.TryRemove(key, out _);
                }
            }
            else
            {
                _cacheItems.TryRemove(keyOrPrefix, out _);
            }
        }

        #endregion

        #region File Metadata Operations

        /// <summary>
        /// Saves or updates file metadata in the database.
        /// </summary>
        public async Task SaveFileMetadataAsync(FileModel metadata, CancellationToken cancellationToken = default)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (string.IsNullOrWhiteSpace(metadata.FileId)) throw new ArgumentException("FileId cannot be empty", nameof(metadata));
            
            try
            {
                await ExecuteInTransactionAsync("SaveFileMetadata", async (context, ct) => 
                {
                    var existingEntity = await context.FilesMetadata
                        .FirstOrDefaultAsync(f => f.FileId == metadata.FileId, ct);

                    if (existingEntity != null)
                    {
                        _logger.LogDebug("Updating existing metadata for File ID: {FileId}", metadata.FileId);
                        UpdateFileMetadataFields(existingEntity, metadata);
                        context.FilesMetadata.Update(existingEntity);
                    }
                    else
                    {
                        _logger.LogDebug("Creating new metadata for File ID: {FileId}", metadata.FileId);
                        var newEntity = new FileEntity
                        {
                            FileId = metadata.FileId,
                            FileName = metadata.FileName,
                            FileSize = metadata.FileSize,
                            CreationTime = metadata.CreationTime.ToUniversalTime(),
                            ModificationTime = metadata.ModificationTime.ToUniversalTime(),
                            ContentType = metadata.ContentType,
                            ChunkSize = metadata.ChunkSize,
                            TotalChunks = metadata.TotalChunks,
                            State = (int)metadata.State
                        };
                        await context.FilesMetadata.AddAsync(newEntity, ct);
                    }
                    
                    await context.SaveChangesAsync(ct);
                    
                    // Invalidate cache
                    InvalidateCache($"file:{metadata.FileId}");
                    InvalidateCache("files:list", isPrefix: true);
                    
                    _logger.LogInformation("Successfully saved metadata for File ID: {FileId}", metadata.FileId);
                    return true;
                }, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlEx && sqlEx.SqliteErrorCode == 19)
            {
                _logger.LogWarning(ex, "Constraint violation during file metadata save for {FileId} - attempting retry", metadata.FileId);
                
                // Retry as update in case of race condition
                await using var retryContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
                try
                {
                    var existingEntity = await retryContext.FilesMetadata
                        .FirstOrDefaultAsync(f => f.FileId == metadata.FileId, cancellationToken);

                    if (existingEntity != null)
                    {
                        _logger.LogDebug("[Retry] Updating metadata for File ID: {FileId}", metadata.FileId);
                        UpdateFileMetadataFields(existingEntity, metadata);
                        retryContext.FilesMetadata.Update(existingEntity);
                        await retryContext.SaveChangesAsync(cancellationToken);
                        
                        // Invalidate cache
                        InvalidateCache($"file:{metadata.FileId}");
                        InvalidateCache("files:list", isPrefix: true);
                        
                        _logger.LogInformation("[Retry] Successfully updated metadata for File ID: {FileId}", metadata.FileId);
                    }
                    else
                    {
                        _logger.LogError("[Retry] Failed to find metadata for File ID {FileId} after constraint violation", metadata.FileId);
                        throw new InvalidOperationException($"Failed to save metadata for {metadata.FileId} after retry", ex);
                    }
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "[Retry] Error during update retry for File ID: {FileId}", metadata.FileId);
                    throw; // Re-throw the retry exception
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving file metadata for File ID: {FileId}", metadata.FileId);
                throw;
            }
        }

        /// <summary>
        /// Retrieves file metadata by ID, with caching.
        /// </summary>
        public async Task<FileModel?> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                _logger.LogWarning("Invalid file ID provided for metadata retrieval");
                return null;
            }
            
            // Check cache first
            string cacheKey = $"file:{fileId}";
            var cached = GetFromCache<FileModel>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved file metadata from cache for ID: {FileId}", fileId);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await context.FilesMetadata
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FileId == fileId, cancellationToken);

                if (entity == null)
                {
                    _logger.LogDebug("No metadata found for File ID: {FileId}", fileId);
                    return null;
                }

                var result = MapFileEntityToCore(entity);
                
                // Store in cache
                AddToCache(cacheKey, result);
                
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("GetFileMetadataAsync cancelled for File ID: {FileId}", fileId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file metadata for File ID: {FileId}", fileId);
                return null; 
            }
        }

        /// <summary>
        /// Lists all files in the database with optional filtering.
        /// </summary>
        public async Task<IEnumerable<FileModel>> ListFilesAsync(CancellationToken cancellationToken = default)
        {
            // Check cache first
            string cacheKey = "files:list";
            var cached = GetFromCache<List<FileModel>>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved file list from cache ({Count} files)", cached.Count);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entities = await context.FilesMetadata
                    .AsNoTracking()
                    .OrderBy(f => f.FileName)
                    .ToListAsync(cancellationToken);

                var result = entities.Select(MapFileEntityToCore).ToList();
                
                // Store in cache with shorter expiry (files might change more frequently)
                AddToCache(cacheKey, result, TimeSpan.FromSeconds(30));
                
                _logger.LogDebug("Retrieved {Count} files", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files metadata");
                return Enumerable.Empty<FileModel>();
            }
        }

        /// <summary>
        /// Deletes a file and all related metadata, using a transaction.
        /// </summary>
        public async Task DeleteFileMetadataAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                _logger.LogWarning("Invalid fileId provided for deletion");
                throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            }
            
            try
            {
                await ExecuteInTransactionAsync("DeleteFileMetadata", async (context, ct) =>
                {
                    // Use include to ensure cascade delete works properly
                    var fileEntity = await context.FilesMetadata
                        .Include(f => f.Chunks)
                        .ThenInclude(c => c.Locations)
                        .FirstOrDefaultAsync(f => f.FileId == fileId, ct);

                    if (fileEntity != null)
                    {
                        context.FilesMetadata.Remove(fileEntity);
                        await context.SaveChangesAsync(ct);
                        
                        // Invalidate caches
                        InvalidateCache($"file:{fileId}");
                        InvalidateCache("files:list", isPrefix: true);
                        InvalidateCache($"chunks:file:{fileId}", isPrefix: true);
                        
                        _logger.LogInformation("Successfully deleted metadata for File ID: {FileId}", fileId);
                    }
                    else
                    {
                        _logger.LogWarning("Attempted to delete metadata for non-existent File ID: {FileId}", fileId);
                    }
                    
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file metadata for File ID: {FileId}", fileId);
                throw;
            }
        }

        /// <summary>
        /// Updates just the state of a file.
        /// </summary>
        public async Task UpdateFileStateAsync(string fileId, FileStateCore newState, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                _logger.LogWarning("Invalid file ID provided for state update");
                throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            }

            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                // Use ExecuteUpdateAsync for efficient update without loading the entity
                int updatedCount = await context.FilesMetadata
                    .Where(f => f.FileId == fileId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(f => f.State, (int)newState)
                        .SetProperty(f => f.ModificationTime, DateTime.UtcNow),
                        cancellationToken);

                if (updatedCount > 0)
                {
                    // Invalidate cache
                    InvalidateCache($"file:{fileId}");
                    InvalidateCache("files:list", isPrefix: true);
                    
                    _logger.LogInformation("Successfully updated state for File ID {FileId} to {NewState}", 
                        fileId, newState);
                }
                else
                {
                    _logger.LogWarning("Attempted to update state for non-existent File ID: {FileId}", fileId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating file state for File ID: {FileId}", fileId);
                throw;
            }
        }

        #endregion

        #region Chunk Metadata Operations

        /// <summary>
        /// Saves or updates chunk metadata and storage locations.
        /// </summary>
        public async Task SaveChunkMetadataAsync(
            ChunkModel chunkInfo, 
            IEnumerable<string> initialNodeIds, 
            CancellationToken cancellationToken = default)
        {
            if (chunkInfo == null) throw new ArgumentNullException(nameof(chunkInfo));
            if (string.IsNullOrWhiteSpace(chunkInfo.FileId)) throw new ArgumentException("FileId cannot be empty", nameof(chunkInfo));
            if (string.IsNullOrWhiteSpace(chunkInfo.ChunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkInfo));

            try
            {
                await ExecuteInTransactionAsync("SaveChunkMetadata", async (context, ct) =>
                {
                    var fileEntity = await context.FilesMetadata
                        .FindAsync(new object[] { chunkInfo.FileId }, ct);
                    
                    if (fileEntity == null)
                    {
                        _logger.LogWarning("Parent FileMetadata not found for File ID {FileId} while saving chunk {ChunkId}. Creating placeholder.", 
                            chunkInfo.FileId, chunkInfo.ChunkId);
                        
                        fileEntity = new FileEntity 
                        {
                            FileId = chunkInfo.FileId, 
                            FileName = $"_placeholder_{chunkInfo.FileId}",
                            CreationTime = DateTime.UtcNow, 
                            ModificationTime = DateTime.UtcNow,
                            State = (int)FileStateCore.Incomplete,
                            ChunkSize = chunkInfo.Size,
                            TotalChunks = 0
                        };
                        
                        await context.FilesMetadata.AddAsync(fileEntity, ct);
                        
                        try
                        {
                            await context.SaveChangesAsync(ct);
                        }
                        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
                        {
                            _logger.LogWarning("Race condition during placeholder file insert. Reloading.");
                            // Reload the entity that was inserted by another process
                            fileEntity = await context.FilesMetadata
                                .FirstOrDefaultAsync(f => f.FileId == chunkInfo.FileId, ct);
                                
                            if (fileEntity == null)
                            {
                                _logger.LogError("Failed to create or find parent file metadata for chunk {ChunkId}", chunkInfo.ChunkId);
                                throw;
                            }
                        }
                    }

                    // Find or create ChunkEntity
                    var chunkEntity = await context.ChunksMetadata
                        .Include(c => c.Locations)
                        .FirstOrDefaultAsync(c => c.FileId == chunkInfo.FileId && c.ChunkId == chunkInfo.ChunkId, ct);

                    if (chunkEntity == null)
                    {
                        _logger.LogDebug("Creating new ChunkEntity for Chunk ID: {ChunkId}, File ID: {FileId}", 
                            chunkInfo.ChunkId, chunkInfo.FileId);
                            
                        chunkEntity = new ChunkEntity
                        {
                            ChunkId = chunkInfo.ChunkId, 
                            FileId = chunkInfo.FileId,
                            ChunkIndex = chunkInfo.ChunkIndex, 
                            Size = chunkInfo.Size, 
                            ChunkHash = chunkInfo.ChunkHash
                        };
                        
                        await context.ChunksMetadata.AddAsync(chunkEntity, ct);
                        await context.SaveChangesAsync(ct);
                    }
                    else
                    {
                        // Update existing if needed
                        bool updated = false;
                        if (chunkEntity.Size != chunkInfo.Size)
                        {
                            chunkEntity.Size = chunkInfo.Size;
                            updated = true;
                        }
                        if (chunkEntity.ChunkHash != chunkInfo.ChunkHash)
                        {
                            chunkEntity.ChunkHash = chunkInfo.ChunkHash;
                            updated = true;
                        }
                        
                        if (updated)
                        {
                            context.ChunksMetadata.Update(chunkEntity);
                            await context.SaveChangesAsync(ct);
                        }
                    }

                    // Add ChunkLocationEntity for initial nodes
                    foreach (var nodeId in initialNodeIds ?? Enumerable.Empty<string>())
                    {
                        bool locationExists = chunkEntity.Locations.Any(loc => loc.StoredNodeId == nodeId);
                        if (!locationExists)
                        {
                            _logger.LogDebug("Adding storage location for Chunk ID: {ChunkId} on Node ID: {NodeId}", 
                                chunkInfo.ChunkId, nodeId);
                                
                            chunkEntity.Locations.Add(new ChunkLocationEntity 
                            {
                                FileId = chunkInfo.FileId, 
                                ChunkId = chunkInfo.ChunkId, 
                                StoredNodeId = nodeId, 
                                ReplicationTime = DateTime.UtcNow
                            });
                        }
                    }

                    await context.SaveChangesAsync(ct);
                    
                    // Invalidate cache
                    InvalidateCache($"chunk:{chunkInfo.FileId}:{chunkInfo.ChunkId}");
                    InvalidateCache($"chunks:file:{chunkInfo.FileId}", isPrefix: true);
                    InvalidateCache($"chunknodes:{chunkInfo.FileId}:{chunkInfo.ChunkId}");
                    InvalidateCache("chunks:local", isPrefix: true);
                    
                    _logger.LogInformation("Successfully saved metadata for Chunk ID: {ChunkId}, File ID: {FileId}", 
                        chunkInfo.ChunkId, chunkInfo.FileId);
                        
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chunk metadata for Chunk ID: {ChunkId}, File ID: {FileId}", 
                    chunkInfo.ChunkId, chunkInfo.FileId);
                throw;
            }
        }

        /// <summary>
        /// Retrieves metadata for a specific chunk.
        /// </summary>
        public async Task<ChunkModel?> GetChunkMetadataAsync(
            string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            
            string cacheKey = $"chunk:{fileId}:{chunkId}";
            var cached = GetFromCache<ChunkModel>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved chunk metadata from cache for ID: {ChunkId}", chunkId);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entity = await context.ChunksMetadata
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.FileId == fileId && c.ChunkId == chunkId, cancellationToken);

                if (entity == null)
                {
                    _logger.LogDebug("No metadata found for Chunk ID: {ChunkId}, File ID: {FileId}", chunkId, fileId);
                    return null;
                }

                var result = MapChunkEntityToCore(entity);
                
                // Store in cache
                AddToCache(cacheKey, result);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunk metadata for File ID: {FileId}, Chunk ID: {ChunkId}", 
                    fileId, chunkId);
                return null;
            }
        }

        /// <summary>
        /// Retrieves all chunks for a specific file.
        /// </summary>
        public async Task<IEnumerable<ChunkModel>> GetChunksMetadataForFileAsync(
            string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            
            string cacheKey = $"chunks:file:{fileId}:list";
            var cached = GetFromCache<List<ChunkModel>>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved file chunks from cache for File ID: {FileId} ({Count} chunks)", 
                    fileId, cached.Count);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                var entities = await context.ChunksMetadata
                    .AsNoTracking()
                    .Where(c => c.FileId == fileId)
                    .OrderBy(c => c.ChunkIndex)
                    .ToListAsync(cancellationToken);

                var result = entities.Select(entity => MapChunkEntityToCore(entity)).ToList();
                
                // Store in cache
                AddToCache(cacheKey, result);
                
                _logger.LogDebug("Retrieved {Count} chunks for File ID: {FileId}", result.Count, fileId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunks metadata for File ID: {FileId}", fileId);
                return Enumerable.Empty<ChunkModel>();
            }
        }

        /// <summary>
        /// Retrieves all chunks stored on the local node.
        /// </summary>
        public async Task<IEnumerable<ChunkModel>> GetChunksStoredLocallyAsync(
            CancellationToken cancellationToken = default)
        {
            string cacheKey = "chunks:local:list";
            var cached = GetFromCache<List<ChunkModel>>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved local chunks from cache ({Count} chunks)", cached.Count);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                var localChunks = await context.ChunkLocations
                    .AsNoTracking()
                    .Where(loc => loc.StoredNodeId == _localNodeId)
                    .Join(
                        context.ChunksMetadata,
                        loc => new { loc.FileId, loc.ChunkId },
                        chunk => new { chunk.FileId, chunk.ChunkId },
                        (loc, chunk) => new { Chunk = chunk, Location = loc }
                    )
                    .OrderBy(x => x.Chunk.FileId)
                    .ThenBy(x => x.Chunk.ChunkIndex)
                    .Select(x => x.Chunk)
                    .ToListAsync(cancellationToken);

                var result = localChunks.Select(entity => 
                    MapChunkEntityToCore(entity, _localNodeId)).ToList();
                
                // Store in cache with shorter expiry (local chunks change more frequently)
                AddToCache(cacheKey, result, TimeSpan.FromSeconds(30));
                
                _logger.LogDebug("Found {Count} chunks stored locally on node {NodeId}", result.Count, _localNodeId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunks stored locally on node {NodeId}", _localNodeId);
                return Enumerable.Empty<ChunkModel>();
            }
        }

        /// <summary>
        /// Deletes metadata for a specific chunk.
        /// </summary>
        public async Task DeleteChunkMetadataAsync(
            string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            
            try
            {
                await ExecuteInTransactionAsync("DeleteChunkMetadata", async (context, ct) =>
                {
                    // Find the chunk with its locations
                    var chunkEntity = await context.ChunksMetadata
                        .Include(c => c.Locations)
                        .FirstOrDefaultAsync(c => c.FileId == fileId && c.ChunkId == chunkId, ct);

                    if (chunkEntity != null)
                    {
                        context.ChunksMetadata.Remove(chunkEntity);
                        await context.SaveChangesAsync(ct);
                        
                        // Invalidate cache
                        InvalidateCache($"chunk:{fileId}:{chunkId}");
                        InvalidateCache($"chunks:file:{fileId}", isPrefix: true);
                        InvalidateCache($"chunknodes:{fileId}:{chunkId}");
                        InvalidateCache("chunks:local", isPrefix: true);
                        
                        _logger.LogInformation("Successfully deleted metadata for Chunk ID: {ChunkId}, File ID: {FileId}", 
                            chunkId, fileId);
                    }
                    else
                    {
                        _logger.LogWarning("Attempted to delete metadata for non-existent Chunk ID: {ChunkId}, File ID: {FileId}", 
                            chunkId, fileId);
                    }
                    
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chunk metadata for Chunk ID: {ChunkId}, File ID: {FileId}", 
                    chunkId, fileId);
                throw;
            }
        }

        /// <summary>
        /// Adds a node to the list of storage locations for a chunk.
        /// </summary>
        public async Task AddChunkStorageNodeAsync(
            string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("NodeId cannot be empty", nameof(nodeId));
            
            try
            {
                await ExecuteInTransactionAsync("AddChunkStorageNode", async (context, ct) =>
                {
                    bool chunkExists = await context.ChunksMetadata
                        .AnyAsync(c => c.FileId == fileId && c.ChunkId == chunkId, ct);
                        
                    if (!chunkExists)
                    {
                        _logger.LogError("Cannot add storage node {NodeId} for non-existent chunk File ID: {FileId}, Chunk ID: {ChunkId}", 
                            nodeId, fileId, chunkId);
                        throw new InvalidOperationException($"Chunk metadata not found for File: {fileId}, Chunk: {chunkId}");
                    }

                    bool locationExists = await context.ChunkLocations
                        .AnyAsync(loc => loc.FileId == fileId && loc.ChunkId == chunkId && loc.StoredNodeId == nodeId, ct);

                    if (!locationExists)
                    {
                        var newLocation = new ChunkLocationEntity 
                        {
                            FileId = fileId, 
                            ChunkId = chunkId, 
                            StoredNodeId = nodeId, 
                            ReplicationTime = DateTime.UtcNow
                        };
                        
                        await context.ChunkLocations.AddAsync(newLocation, ct);
                        await context.SaveChangesAsync(ct);
                        
                        InvalidateCache($"chunknodes:{fileId}:{chunkId}");
                        if (nodeId == _localNodeId)
                        {
                            InvalidateCache("chunks:local", isPrefix: true);
                        }
                        
                        _logger.LogInformation("Successfully added storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", 
                            nodeId, chunkId, fileId);
                    }
                    else
                    {
                        _logger.LogDebug("Storage node {NodeId} already exists for Chunk ID: {ChunkId}, File ID: {FileId}", 
                            nodeId, chunkId, fileId);
                    }
                    
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", 
                    nodeId, chunkId, fileId);
                throw;
            }
        }

        /// <summary>
        /// Removes a node from the list of storage locations for a chunk.
        /// </summary>
        public async Task<bool> RemoveChunkStorageNodeAsync(
            string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("NodeId cannot be empty", nameof(nodeId));
            
            try
            {
                return await ExecuteInTransactionAsync("RemoveChunkStorageNode", async (context, ct) =>
                {
                    int deletedCount = await context.ChunkLocations
                        .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId && loc.StoredNodeId == nodeId)
                        .ExecuteDeleteAsync(ct);

                    if (deletedCount > 0)
                    {
                        _logger.LogInformation("Successfully removed storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", 
                            nodeId, chunkId, fileId);
                            
                        int remainingCount = await context.ChunkLocations
                            .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId)
                            .CountAsync(ct);
                            
                        if (remainingCount == 0)
                        {
                            _logger.LogInformation("Last location removed for Chunk ID: {ChunkId}, also removing chunk metadata", chunkId);
                            
                            await context.ChunksMetadata
                                .Where(c => c.FileId == fileId && c.ChunkId == chunkId)
                                .ExecuteDeleteAsync(ct);
                        }
                        
                        InvalidateCache($"chunknodes:{fileId}:{chunkId}");
                        if (nodeId == _localNodeId)
                        {
                            InvalidateCache("chunks:local", isPrefix: true);
                        }
                        
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning("Chunk location not found for File ID: {FileId}, Chunk ID: {ChunkId}, Node ID: {NodeId}", 
                            fileId, chunkId, nodeId);
                        return false;
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", 
                    nodeId, chunkId, fileId);
                return false;
            }
        }

        /// <summary>
        /// Gets the list of nodes currently storing a specific chunk.
        /// </summary>
        public async Task<IEnumerable<string>> GetChunkStorageNodesAsync(
            string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            
            string cacheKey = $"chunknodes:{fileId}:{chunkId}";
            var cached = GetFromCache<List<string>>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved chunk storage nodes from cache for ID: {ChunkId} ({Count} nodes)", 
                    chunkId, cached.Count);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                // Optimized query to directly fetch just the node IDs
                var nodeIds = await context.ChunkLocations
                    .AsNoTracking()
                    .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId)
                    .Select(loc => loc.StoredNodeId)
                    .ToListAsync(cancellationToken);

                // Store in cache
                AddToCache(cacheKey, nodeIds);
                
                _logger.LogDebug("Found {NodeCount} storage nodes for Chunk {ChunkId}", nodeIds.Count, chunkId);
                return nodeIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving storage nodes for File ID: {FileId}, Chunk ID: {ChunkId}", 
                    fileId, chunkId);
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// Updates the complete list of storage nodes for a chunk.
        /// </summary>
        public async Task UpdateChunkStorageNodesAsync(
            string fileId, string chunkId, IEnumerable<string> currentNodeIds, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("FileId cannot be empty", nameof(fileId));
            if (string.IsNullOrWhiteSpace(chunkId)) throw new ArgumentException("ChunkId cannot be empty", nameof(chunkId));
            if (currentNodeIds == null) throw new ArgumentNullException(nameof(currentNodeIds));
            
            try
            {
                await ExecuteInTransactionAsync("UpdateChunkStorageNodes", async (context, ct) =>
                {
                    // Get existing locations
                    var existingLocations = await context.ChunkLocations
                        .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId)
                        .ToListAsync(ct);

                    // Determine nodes to add and remove
                    var currentNodeIdsList = currentNodeIds.ToList();
                    var nodesToAdd = currentNodeIdsList
                        .Except(existingLocations.Select(loc => loc.StoredNodeId))
                        .ToList();
                        
                    var locationsToRemove = existingLocations
                        .Where(loc => !currentNodeIdsList.Contains(loc.StoredNodeId))
                        .ToList();

                    // Remove old locations
                    if (locationsToRemove.Any())
                    {
                        context.ChunkLocations.RemoveRange(locationsToRemove);
                        _logger.LogDebug("Removing {Count} storage nodes for Chunk {ChunkId}: {Nodes}", 
                            locationsToRemove.Count, chunkId, string.Join(", ", locationsToRemove.Select(l => l.StoredNodeId)));
                    }

                    // Add new locations
                    if (nodesToAdd.Any())
                    {
                        foreach (var nodeId in nodesToAdd)
                        {
                            await context.ChunkLocations.AddAsync(new ChunkLocationEntity 
                            {
                                FileId = fileId, 
                                ChunkId = chunkId, 
                                StoredNodeId = nodeId, 
                                ReplicationTime = DateTime.UtcNow
                            }, ct);
                        }
                        
                        _logger.LogDebug("Adding {Count} storage nodes for Chunk {ChunkId}: {Nodes}", 
                            nodesToAdd.Count, chunkId, string.Join(", ", nodesToAdd));
                    }

                    if (locationsToRemove.Any() || nodesToAdd.Any())
                    {
                        await context.SaveChangesAsync(ct);
                        
                        // Invalidate cache
                        InvalidateCache($"chunknodes:{fileId}:{chunkId}");
                        if (nodesToAdd.Contains(_localNodeId) || locationsToRemove.Any(loc => loc.StoredNodeId == _localNodeId))
                        {
                            InvalidateCache("chunks:local", isPrefix: true);
                        }
                    }
                    
                    _logger.LogInformation("Updated storage nodes for Chunk ID: {ChunkId}, File ID: {FileId}. Added: {AddCount}, Removed: {RemoveCount}", 
                        chunkId, fileId, nodesToAdd.Count, locationsToRemove.Count);
                        
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating storage nodes for Chunk ID: {ChunkId}, File ID: {FileId}", 
                    chunkId, fileId);
                throw;
            }
        }

        #endregion

        #region Node State Operations

        /// <summary>
        /// Saves or updates the state information for a node.
        /// </summary>
        public async Task SaveNodeStateAsync(
            NodeModel nodeStateInfo, CancellationToken cancellationToken = default)
        {
            if (nodeStateInfo == null) throw new ArgumentNullException(nameof(nodeStateInfo));
            if (string.IsNullOrWhiteSpace(nodeStateInfo.Id)) throw new ArgumentException("NodeId cannot be empty", nameof(nodeStateInfo));
            if (string.IsNullOrWhiteSpace(nodeStateInfo.Address)) throw new ArgumentException("Address cannot be empty", nameof(nodeStateInfo));
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                var existingEntity = await context.NodeStates
                    .FirstOrDefaultAsync(n => n.NodeId == nodeStateInfo.Id, cancellationToken);

                if (existingEntity != null)
                {
                    _logger.LogDebug("Updating existing NodeEntity for Node ID: {NodeId}", nodeStateInfo.Id);
                    
                    // Update fields
                    existingEntity.Address = nodeStateInfo.Address;
                    existingEntity.State = (int)nodeStateInfo.State;
                    existingEntity.LastSeen = nodeStateInfo.LastSeen.ToUniversalTime();
                    existingEntity.LastSuccessfulPingTimestamp = nodeStateInfo.LastSuccessfulPingTimestamp?.ToUniversalTime();
                    existingEntity.DiskSpaceAvailableBytes = nodeStateInfo.DiskSpaceAvailableBytes;
                    existingEntity.DiskSpaceTotalBytes = nodeStateInfo.DiskSpaceTotalBytes;
                    existingEntity.StoredChunkCount = nodeStateInfo.StoredChunkCount;
                    
                    context.NodeStates.Update(existingEntity);
                }
                else
                {
                    _logger.LogDebug("Creating new NodeEntity for Node ID: {NodeId}", nodeStateInfo.Id);
                    
                    // Create new entity
                    var newEntity = new NodeEntity
                    {
                        NodeId = nodeStateInfo.Id,
                        Address = nodeStateInfo.Address,
                        State = (int)nodeStateInfo.State,
                        LastSeen = nodeStateInfo.LastSeen.ToUniversalTime(),
                        LastSuccessfulPingTimestamp = nodeStateInfo.LastSuccessfulPingTimestamp?.ToUniversalTime(),
                        DiskSpaceAvailableBytes = nodeStateInfo.DiskSpaceAvailableBytes,
                        DiskSpaceTotalBytes = nodeStateInfo.DiskSpaceTotalBytes,
                        StoredChunkCount = nodeStateInfo.StoredChunkCount
                    };
                    
                    await context.NodeStates.AddAsync(newEntity, cancellationToken);
                }
                
                await context.SaveChangesAsync(cancellationToken);
                
                // Invalidate cache
                InvalidateCache($"nodestate:{nodeStateInfo.Id}");
                InvalidateCache("nodestates:all", isPrefix: true);
                
                _logger.LogTrace("Successfully saved node state for Node ID: {NodeId}", nodeStateInfo.Id);
                
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving node state for Node ID: {NodeId}", nodeStateInfo.Id);
                throw;
            }
        }

        /// <summary>
        /// Gets the state information for specific nodes.
        /// </summary>
        public async Task<IEnumerable<NodeModel>> GetNodeStatesAsync(
            IEnumerable<string> nodeIds, CancellationToken cancellationToken = default)
        {
            if (nodeIds == null) throw new ArgumentNullException(nameof(nodeIds));
            
            var nodeIdsList = nodeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (!nodeIdsList.Any())
            {
                _logger.LogDebug("No valid node IDs provided for state retrieval");
                return Enumerable.Empty<NodeModel>();
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                var entities = await context.NodeStates
                    .AsNoTracking()
                    .Where(n => nodeIdsList.Contains(n.NodeId))
                    .ToListAsync(cancellationToken);

                var result = entities.Select(MapNodeEntityToCore).ToList();
                
                _logger.LogDebug("Retrieved {Count} node states of {Requested} requested", 
                    result.Count, nodeIdsList.Count);
                    
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving node states for Node IDs: {NodeIds}", 
                    string.Join(", ", nodeIdsList));
                return Enumerable.Empty<NodeModel>();
            }
        }

        /// <summary>
        /// Gets the state information for all known nodes.
        /// </summary>
        public async Task<IEnumerable<NodeModel>> GetAllNodeStatesAsync(
            CancellationToken cancellationToken = default)
        {
            string cacheKey = "nodestates:all:list";
            var cached = GetFromCache<List<NodeModel>>(cacheKey);
            if (cached != null)
            {
                _logger.LogTrace("Retrieved all node states from cache ({Count} nodes)", cached.Count);
                return cached;
            }
            
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                
                var entities = await context.NodeStates
                    .AsNoTracking()
                    .OrderBy(n => n.NodeId)
                    .ToListAsync(cancellationToken);

                var result = entities.Select(MapNodeEntityToCore).ToList();
                
                // Store in cache with short expiry (node states change frequently)
                AddToCache(cacheKey, result, TimeSpan.FromSeconds(15));
                
                _logger.LogDebug("Retrieved {Count} node states", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all node states");
                return Enumerable.Empty<NodeModel>();
            }
        }

        #endregion

        #region Backup Operation

        /// <summary>
        /// Creates a backup of the database.
        /// </summary>
        public async Task<bool> BackupDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(backupFilePath))
            {
                _logger.LogError("Invalid backup file path provided");
                return false;
            }

            _logger.LogInformation("Starting database backup to '{BackupPath}'...", backupFilePath);
            
            // Ensure backup directory exists
            string? backupDir = Path.GetDirectoryName(backupFilePath);
            if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
            {
                try
                {
                    Directory.CreateDirectory(backupDir);
                    _logger.LogDebug("Created backup directory: {BackupDir}", backupDir);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create backup directory: {BackupDir}", backupDir);
                    return false;
                }
            }
            
            // Get the connection string from the context
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var sourceConnectionString = context.Database.GetConnectionString();
            
            if (string.IsNullOrEmpty(sourceConnectionString))
            {
                _logger.LogError("Cannot backup database: Source connection string is null or empty");
                return false;
            }

            SqliteConnection? sourceConnection = null;
            SqliteConnection? backupConnection = null;
            
            try
            {
                // Use SQLite specific backup API for online backup
                sourceConnection = new SqliteConnection(sourceConnectionString);
                await sourceConnection.OpenAsync(cancellationToken);

                backupConnection = new SqliteConnection($"Data Source={backupFilePath}");
                await backupConnection.OpenAsync(cancellationToken);

                sourceConnection.BackupDatabase(backupConnection);

                _logger.LogInformation("Database backup completed successfully to '{BackupPath}'", backupFilePath);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Database backup cancelled");
                // Delete incomplete backup
                try { if (File.Exists(backupFilePath)) File.Delete(backupFilePath); } 
                catch { /* Ignore cleanup errors */ }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database backup failed. Target: '{BackupPath}'", backupFilePath);
                // Attempt to delete potentially incomplete backup file
                try { if (File.Exists(backupFilePath)) File.Delete(backupFilePath); } 
                catch { /* Ignore cleanup errors */ }
                return false;
            }
            finally
            {
                if (sourceConnection != null)
                {
                    await sourceConnection.CloseAsync();
                    await sourceConnection.DisposeAsync();
                }
                
                if (backupConnection != null)
                {
                    await backupConnection.CloseAsync();
                    await backupConnection.DisposeAsync();
                }
            }
        }

        #endregion
    }
}