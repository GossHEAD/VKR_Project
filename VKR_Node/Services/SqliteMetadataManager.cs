using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using VKR_Core.Enums; 
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Persistance;
using VKR_Node.Persistance.Entities;
using VKR.Node.Persistance.Entities;

namespace VKR_Node.Services
{
    /// <summary>
    /// Manages metadata storage using SQLite via Entity Framework Core.
    /// </summary>
    public class SqliteMetadataManager : IMetadataManager
    {
        
        private readonly IDbContextFactory<NodeDbContext> _contextFactory;
        private readonly ILogger<SqliteMetadataManager> _logger;
        private readonly NodeOptions _nodeOptions; 
        private readonly DatabaseOptions _databaseOptions;

        public SqliteMetadataManager(
            IDbContextFactory<NodeDbContext> contextFactory,
            ILogger<SqliteMetadataManager> logger,
            IOptions<NodeOptions> nodeOptions,
            IOptions<DatabaseOptions> databaseOptions) // Inject DatabaseOptions
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _nodeOptions = nodeOptions.Value;
            _databaseOptions = databaseOptions.Value; // Store DB options
        }

        /// <summary>
        /// Initializes the metadata manager, ensuring the database exists and migrations are applied.
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Initializing metadata manager and ensuring database is up-to-date...");
            try
            {
                await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
                // Ensure database exists and apply pending migrations
                await context.Database.MigrateAsync(cancellationToken);
                _logger.LogInformation("Database initialization complete.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize database or apply migrations.");
                throw; // Re-throw critical initialization error
            }
        }
        // --- File Metadata Operations ---

        public async Task SaveFileMetadataAsync(FileMetadataCore metadata, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var existingEntity = await context.FilesMetadata
                                                 .FirstOrDefaultAsync(f => 
                                                     f.FileId == metadata.FileId, cancellationToken);

                if (existingEntity != null)
                {
                    _logger.LogDebug("Updating existing metadata for File ID: {FileId}", metadata.FileId);
                    /*
                    existingEntity.FileName = metadata.FileName;
                    existingEntity.FileSize = metadata.FileSize;
                    existingEntity.ModificationTime = metadata.ModificationTime.ToUniversalTime();
                    existingEntity.ContentType = metadata.ContentType;
                    existingEntity.ChunkSize = metadata.ChunkSize;
                    existingEntity.TotalChunks = metadata.TotalChunks;
                    if (existingEntity.State == (int)FileStateCore.Incomplete && metadata.State != FileStateCore.Incomplete)
                    {
                        existingEntity.State = (int)metadata.State;
                    } else if (existingEntity.State == (int)FileStateCore.Unknown) {
                        existingEntity.State = (int)metadata.State; 
                    }
                    */
                    UpdateFileMetadataFields(existingEntity, metadata);
                    context.FilesMetadata.Update(existingEntity);
                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Successfully updated metadata for File ID: {FileId}", metadata.FileId);
                }
                else
                {
                    _logger.LogDebug("Attempting to save new metadata for File ID: {FileId}", metadata.FileId);
                    var newEntity = new FileMetadataEntity
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
                    await context.FilesMetadata.AddAsync(newEntity, cancellationToken);
                    //await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Successfully saved new metadata for File ID: {FileId}", metadata.FileId);
                }
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully saved metadata for File ID: {FileId}", metadata.FileId);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqliteEx && sqliteEx.SqliteErrorCode == 19 /* SQLITE_CONSTRAINT_UNIQUE */)
            {
                _logger.LogWarning(ex, "UNIQUE constraint violation for File ID: {FileId}. Likely race condition during insert. Retrying as update.", metadata.FileId);
                
                await using var retryContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
                try
                {
                    var existingEntity = await retryContext.FilesMetadata
                                                     .FirstOrDefaultAsync(f => f.FileId == metadata.FileId, cancellationToken);

                    if (existingEntity != null)
                    {
                         _logger.LogDebug("[Retry] Updating existing metadata for File ID: {FileId}", metadata.FileId);
                         // Re-apply updates (same logic as UPDATE PATH above)
                         existingEntity.FileName = metadata.FileName;
                         existingEntity.FileSize = metadata.FileSize;
                         existingEntity.ModificationTime = metadata.ModificationTime.ToUniversalTime();
                         existingEntity.ContentType = metadata.ContentType;
                         existingEntity.ChunkSize = metadata.ChunkSize;
                         existingEntity.TotalChunks = metadata.TotalChunks;
                         if (existingEntity.State == (int)FileStateCore.Incomplete && metadata.State != FileStateCore.Incomplete) {
                             existingEntity.State = (int)metadata.State;
                         } else if (existingEntity.State == (int)FileStateCore.Unknown) {
                             existingEntity.State = (int)metadata.State;
                         }
                         retryContext.FilesMetadata.Update(existingEntity);
                         await retryContext.SaveChangesAsync(cancellationToken);
                         _logger.LogInformation("[Retry] Successfully updated metadata for File ID: {FileId}", metadata.FileId);
                    }
                    else
                    {
                        // This case is unlikely if we hit the unique constraint, but handle it.
                         _logger.LogError("[Retry] Failed to find metadata for File ID {FileId} even after unique constraint error.", metadata.FileId);
                         throw new InvalidOperationException($"Failed to find metadata for {metadata.FileId} after constraint violation.", ex);
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
                throw; // Re-throw to indicate failure
            }
        }

        private void UpdateFileMetadataFields(FileMetadataEntity entity, FileMetadataCore metadata)
        {
            entity.FileName = metadata.FileName;
            entity.FileSize = metadata.FileSize;
            entity.ModificationTime = metadata.ModificationTime.ToUniversalTime();
            entity.ContentType = metadata.ContentType;
            entity.ChunkSize = metadata.ChunkSize;
            entity.TotalChunks = metadata.TotalChunks;
    
            if (entity.State == (int)FileStateCore.Incomplete && metadata.State != FileStateCore.Incomplete)
            {
                entity.State = (int)metadata.State;
            }
            else if (entity.State == (int)FileStateCore.Unknown)
            {
                entity.State = (int)metadata.State;
            }
        }
        
        public async Task<FileMetadataCore?> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                _logger.LogWarning("Invalid file ID provided for metadata retrieval.");
                return null;
            }
            
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var entity = await context.FilesMetadata
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.FileId == fileId, cancellationToken);

                if (entity == null) return null;

                return new FileMetadataCore
                {
                    FileId = entity.FileId,
                    FileName = entity.FileName,
                    FileSize = entity.FileSize,
                    CreationTime = entity.CreationTime,
                    ModificationTime = entity.ModificationTime,
                    ContentType = entity.ContentType,
                    ChunkSize = entity.ChunkSize,
                    TotalChunks = entity.TotalChunks,
                    State = (FileStateCore)entity.State
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving file metadata for File ID: {FileId}", fileId);
                return null; 
            }
        }

        public async Task<IEnumerable<FileMetadataCore>> ListFilesAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var entities = await context.FilesMetadata
                    .AsNoTracking()
                    .OrderBy(f => f.FileName)
                    .ToListAsync(cancellationToken);

                return entities.Select(entity => new FileMetadataCore
                {
                    FileId = entity.FileId,
                    FileName = entity.FileName,
                    FileSize = entity.FileSize,
                    CreationTime = entity.CreationTime,
                    ModificationTime = entity.ModificationTime,
                    ContentType = entity.ContentType,
                    ChunkSize = entity.ChunkSize,
                    TotalChunks = entity.TotalChunks,
                    State = (FileStateCore)entity.State
                }).ToList(); // Return concrete list or keep as IEnumerable? List is safer.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing files metadata.");
                return Enumerable.Empty<FileMetadataCore>(); // Return empty on error
            }
        }

        public async Task DeleteFileMetadataAsync(string fileId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                _logger.LogWarning("Invalid fileId provided for deletion.");
                return;
            }
            
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                 var fileEntity = await context.FilesMetadata
                                              .Include(f => f.Chunks) // Required if cascade delete is NOT handled by DB constraints
                                              .ThenInclude(c => c.Locations) // Required if cascade delete is NOT handled by DB constraints
                                              .FirstOrDefaultAsync(f => f.FileId == fileId, cancellationToken);

                 if (fileEntity != null)
                 {
                     context.FilesMetadata.Remove(fileEntity); 
                     await context.SaveChangesAsync(cancellationToken);
                     _logger.LogInformation("Successfully deleted metadata (and related via cascade) for File ID: {FileId}", fileId);
                 }
                 else
                 {
                      _logger.LogWarning("Attempted to delete metadata for non-existent File ID: {FileId}", fileId);
                 }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file metadata for File ID: {FileId}", fileId);
                throw; 
            }
        }

        public async Task UpdateFileStateAsync(string fileId, FileStateCore newState, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                int updatedCount = await context.FilesMetadata
                    .Where(f => f.FileId == fileId)
                    .ExecuteUpdateAsync(setters => setters
                            .SetProperty(f => f.State, (int)newState)
                            .SetProperty(f => f.ModificationTime, DateTime.UtcNow), // Update modification time
                        cancellationToken);

                if (updatedCount > 0)
                    _logger.LogInformation("Successfully updated state for File ID {FileId} to {NewState}.", fileId, newState);
                else
                    _logger.LogWarning("Attempted to update state for non-existent File ID: {FileId}", fileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating file state for File ID: {FileId}", fileId);
                throw;
            }
        }
        
        public async Task SaveChunkMetadataAsync(ChunkInfoCore chunkInfo, IEnumerable<string> initialNodeIds, CancellationToken cancellationToken = default)
        {
             await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
             // Use transaction for atomicity
             await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                
                var fileMetadataEntity = await context.FilesMetadata.FindAsync(new object[] { chunkInfo.FileId }, cancellationToken);
                if (fileMetadataEntity == null)
                {
                    _logger.LogWarning("Parent FileMetadata not found for File ID {FileId} while saving chunk {ChunkId}. Creating placeholder.", chunkInfo.FileId, chunkInfo.ChunkId);
                    fileMetadataEntity = new FileMetadataEntity {
                        FileId = chunkInfo.FileId, FileName = $"_placeholder_{chunkInfo.FileId}",
                        CreationTime = DateTime.UtcNow, ModificationTime = DateTime.UtcNow,
                        State = (int)FileStateCore.Incomplete
                    };
                    try
                    {
                        await context.FilesMetadata.AddAsync(fileMetadataEntity, cancellationToken);
                        await context.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
                    {
                        _logger.LogWarning("Race condition during placeholder file insert. Skipping.");
                    }
                }
                

                // Find or create ChunkMetadataEntity
                var chunkMetadataEntity = await context.ChunksMetadata
                                                      .Include(c => c.Locations) // Include locations
                                                      .FirstOrDefaultAsync(c => c.FileId == chunkInfo.FileId && c.ChunkId == chunkInfo.ChunkId, cancellationToken);

                if (chunkMetadataEntity == null)
                {
                    _logger.LogDebug("Creating new ChunkMetadataEntity for Chunk ID: {ChunkId}, File ID: {FileId}", chunkInfo.ChunkId, chunkInfo.FileId);
                    chunkMetadataEntity = new ChunkMetadataEntity
                    {
                        ChunkId = chunkInfo.ChunkId, FileId = chunkInfo.FileId,
                        ChunkIndex = chunkInfo.ChunkIndex, Size = chunkInfo.Size, ChunkHash = chunkInfo.ChunkHash
                    };
                    await context.ChunksMetadata.AddAsync(chunkMetadataEntity, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    // Update existing if needed (e.g., hash)
                    chunkMetadataEntity.Size = chunkInfo.Size;
                    chunkMetadataEntity.ChunkHash = chunkInfo.ChunkHash;
                    context.ChunksMetadata.Update(chunkMetadataEntity);
                }

                // Add ChunkLocationEntity for initial nodes
                foreach (var nodeId in initialNodeIds ?? Enumerable.Empty<string>())
                {
                    bool locationExists = chunkMetadataEntity.Locations.Any(loc => loc.StoredNodeId == nodeId);
                    if (!locationExists)
                    {
                         _logger.LogDebug("Adding initial ChunkLocationEntity for Chunk ID: {ChunkId} on Node ID: {NodeId}", chunkInfo.ChunkId, nodeId);
                         chunkMetadataEntity.Locations.Add(new ChunkLocationEntity {
                             FileId = chunkInfo.FileId, ChunkId = chunkInfo.ChunkId, StoredNodeId = nodeId, ReplicationTime = DateTime.UtcNow
                         });
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken); // Commit transaction
                _logger.LogInformation("Successfully saved metadata for Chunk ID: {ChunkId}, File ID: {FileId}, Initial Nodes: {Nodes}",
                                       chunkInfo.ChunkId, chunkInfo.FileId, string.Join(",", initialNodeIds ?? Enumerable.Empty<string>()));
            }
            catch (Exception ex)
            {
                 await transaction.RollbackAsync(cancellationToken); // Rollback on error
                _logger.LogError(ex, "Error saving chunk metadata for Chunk ID: {ChunkId}, File ID: {FileId}", chunkInfo.ChunkId, chunkInfo.FileId);
                throw;
            }
        }
        
        public async Task UpdateChunkStorageNodesAsync(string fileId, string chunkId, IEnumerable<string> currentNodeIds, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var existingLocations = await context.ChunkLocations
                                                     .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId)
                                                     .ToListAsync(cancellationToken);

                var nodesToAdd = currentNodeIds.Except(existingLocations.Select(loc => loc.StoredNodeId)).ToList();
                var locationsToRemove = existingLocations.Where(loc => !currentNodeIds.Contains(loc.StoredNodeId)).ToList();

                if (locationsToRemove.Any())
                {
                    context.ChunkLocations.RemoveRange(locationsToRemove);
                    _logger.LogDebug("Removing {Count} storage nodes for Chunk {ChunkId}: {Nodes}", locationsToRemove.Count, chunkId, string.Join(",", locationsToRemove.Select(l => l.StoredNodeId)));
                }

                if (nodesToAdd.Any())
                {
                    foreach (var nodeId in nodesToAdd)
                    {
                        await context.ChunkLocations.AddAsync(new ChunkLocationEntity {
                            FileId = fileId, ChunkId = chunkId, StoredNodeId = nodeId, ReplicationTime = DateTime.UtcNow
                        }, cancellationToken);
                    }
                    _logger.LogDebug("Adding {Count} storage nodes for Chunk {ChunkId}: {Nodes}", nodesToAdd.Count, chunkId, string.Join(",", nodesToAdd));
                }

                if (locationsToRemove.Any() || nodesToAdd.Any())
                {
                    await context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Updated storage nodes for Chunk ID: {ChunkId}, File ID: {FileId}", chunkId, fileId);

            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Error updating storage nodes for Chunk ID: {ChunkId}, File ID: {FileId}", chunkId, fileId);
                throw;
            }
        }
        
        public async Task<IEnumerable<ChunkInfoCore>> GetChunksMetadataForFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var entities = await context.ChunksMetadata
                    .AsNoTracking()
                    .Where(c => c.FileId == fileId)
                    .OrderBy(c => c.ChunkIndex)
                    .ToListAsync(cancellationToken);

                return entities.Select(entity => new ChunkInfoCore
                {
                    FileId = entity.FileId, ChunkId = entity.ChunkId,
                    ChunkIndex = entity.ChunkIndex, Size = entity.Size, ChunkHash = entity.ChunkHash,
                    StoredNodeId = string.Empty // Indicate StoredNodeId is not applicable here
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunks metadata for File ID: {FileId}", fileId);
                return Enumerable.Empty<ChunkInfoCore>();
            }
        }
        
        public async Task<IEnumerable<ChunkInfoCore>> GetChunksStoredLocallyAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var localNodeId = _nodeOptions.NodeId; // Get local node ID
            try
            {
                var localChunks = await context.ChunkLocations
                    .AsNoTracking()
                    .Where(loc => loc.StoredNodeId == localNodeId)
                    .Select(loc => loc.ChunkMetadataEntity) // Navigate to the ChunkMetadata
                    .OrderBy(c => c.FileId).ThenBy(c => c.ChunkIndex)
                    .ToListAsync(cancellationToken);

                _logger.LogDebug("Found {Count} chunks stored locally on node {NodeId}", localChunks.Count, localNodeId);

                return localChunks.Select(entity => new ChunkInfoCore
                {
                    FileId = entity.FileId, ChunkId = entity.ChunkId,
                    ChunkIndex = entity.ChunkIndex, Size = entity.Size, ChunkHash = entity.ChunkHash,
                    StoredNodeId = localNodeId // Explicitly set local node ID
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunks stored locally on node {NodeId}", localNodeId);
                return Enumerable.Empty<ChunkInfoCore>();
            }
        }
        
        public async Task DeleteChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                // Cascade delete should handle removing ChunkLocations
                var chunkEntity = await context.ChunksMetadata
                    .Include(c => c.Locations) // Needed if cascade delete is not handled by DB constraints
                    .FirstOrDefaultAsync(c => c.FileId == fileId && c.ChunkId == chunkId, cancellationToken);

                if (chunkEntity != null)
                {
                    context.ChunksMetadata.Remove(chunkEntity);
                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Successfully deleted metadata (and locations via cascade) for Chunk ID: {ChunkId}, File ID: {FileId}", chunkId, fileId);
                }
                else
                {
                    _logger.LogWarning("Attempted to delete metadata for non-existent Chunk ID: {ChunkId}, File ID: {FileId}", chunkId, fileId);
                }

                // Alt using ExecuteDelete (if DB handles cascades):
                // int deletedCount = await context.ChunksMetadata
                //                                 .Where(c => c.FileId == fileId && c.ChunkId == chunkId)
                //                                 .ExecuteDeleteAsync(cancellationToken);
                // Log based on deletedCount...

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting chunk metadata for Chunk ID: {ChunkId}, File ID: {FileId}", chunkId, fileId);
                throw;
            }
        }
        
        public async Task AddChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                // Check if chunk metadata exists
                 var chunkMetaExists = await context.ChunksMetadata.AnyAsync(c => c.FileId == fileId && c.ChunkId == chunkId, cancellationToken);
                 if (!chunkMetaExists) {
                      _logger.LogError("Cannot add storage node {NodeId} for non-existent chunk File ID: {FileId}, Chunk ID: {ChunkId}", nodeId, fileId, chunkId);
                      throw new InvalidOperationException($"Chunk metadata not found for File: {fileId}, Chunk: {chunkId}");
                 }

                // Check if location already exists
                bool locationExists = await context.ChunkLocations
                                                  .AnyAsync(loc => loc.FileId == fileId && loc.ChunkId == chunkId && loc.StoredNodeId == nodeId, cancellationToken);

                if (!locationExists)
                {
                    var newLocation = new ChunkLocationEntity {
                        FileId = fileId, ChunkId = chunkId, StoredNodeId = nodeId, ReplicationTime = DateTime.UtcNow
                    };
                    await context.ChunkLocations.AddAsync(newLocation, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Successfully added storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", nodeId, chunkId, fileId);
                }
                else
                {
                    _logger.LogWarning("Storage node {NodeId} already exists for Chunk ID: {ChunkId}, File ID: {FileId}", nodeId, chunkId, fileId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", nodeId, chunkId, fileId);
                throw;
            }
        }
        
        public async Task<ChunkInfoCore?> GetChunkMetadataAsync(string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var entity = await context.ChunksMetadata
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.FileId == fileId && c.ChunkId == chunkId, cancellationToken);

                if (entity == null) return null;

                // Note: StoredNodeId is not part of ChunkMetadata, it's in ChunkLocation.
                // This method returns general chunk info.
                return new ChunkInfoCore
                {
                    FileId = entity.FileId, ChunkId = entity.ChunkId,
                    ChunkIndex = entity.ChunkIndex, Size = entity.Size, ChunkHash = entity.ChunkHash,
                    StoredNodeId = string.Empty // Indicate StoredNodeId is not applicable here
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving chunk metadata for File ID: {FileId}, Chunk ID: {ChunkId}", fileId, chunkId);
                return null;
            }
        }
        
        public async Task<IEnumerable<string>> GetChunkStorageNodesAsync(string fileId, string chunkId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var nodeIds = await context.ChunkLocations
                    .AsNoTracking()
                    .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId)
                    .Select(loc => loc.StoredNodeId)
                    .ToListAsync(cancellationToken);

                _logger.LogDebug("Found {NodeCount} storage nodes for Chunk {ChunkId}: {NodeIds}", nodeIds.Count, chunkId, string.Join(", ", nodeIds));
                return nodeIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving storage nodes for File ID: {FileId}, Chunk ID: {ChunkId}", fileId, chunkId);
                return Enumerable.Empty<string>();
            }
        }

        public async Task<bool> RemoveChunkStorageNodeAsync(string fileId, string chunkId, string nodeId, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken); 
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken); // Use transaction
            bool success = false;
            try
            {
                 // Use ExecuteDelete for efficiency
                 int deletedCount = await context.ChunkLocations
                                                .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId && loc.StoredNodeId == nodeId)
                                                .ExecuteDeleteAsync(cancellationToken);

                 if (deletedCount > 0)
                 {
                      _logger.LogInformation("Successfully removed storage node {NodeId} for Chunk ID: {ChunkId}, File ID: {FileId}", nodeId, chunkId, fileId);
                      success = true;
                      
                     var remainingLocationsCount = await context.ChunkLocations
                         .Where(loc => loc.FileId == fileId && loc.ChunkId == chunkId)
                         .CountAsync(cancellationToken);

                     if (remainingLocationsCount == 0)
                     {
                         await context.ChunksMetadata
                             .Where(c => c.FileId == fileId && c.ChunkId == chunkId)
                             .ExecuteDeleteAsync(cancellationToken);
                     }
                     await transaction.CommitAsync(cancellationToken); // Commit
                 }
                 else
                 {
                      _logger.LogWarning("Chunk location metadata not found for File ID: {FileId}, Chunk ID: {ChunkId}, Node ID: {NodeId}. Cannot remove.", fileId, chunkId, nodeId);
                      await transaction.RollbackAsync(cancellationToken); // Rollback if nothing was deleted
                      success = true;
                 }

                 return success;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken); // Rollback on error
                _logger.LogError(ex, "Error removing storage node {NodeId} for File ID: {FileId}, Chunk ID: {ChunkId}", nodeId, fileId, chunkId);
                return false;
            }
        }
        
        
        // --- Backup ---
        public async Task<bool> BackupDatabaseAsync(string backupFilePath, CancellationToken cancellationToken = default)
        {
             await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
             var sourceConnectionString = context.Database.GetConnectionString();
             if (string.IsNullOrEmpty(sourceConnectionString))
             {
                 _logger.LogError("Cannot backup database: Source connection string is null or empty.");
                 return false;
             }

             _logger.LogInformation("Starting database backup from '{Source}' to '{BackupPath}'...", sourceConnectionString, backupFilePath);

             try
             {
                 // Ensure backup directory exists
                 var backupDir = Path.GetDirectoryName(backupFilePath);
                 if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                 {
                     Directory.CreateDirectory(backupDir);
                 }

                 // Use SQLite specific backup API for online backup
                 await using var sourceConnection = new SqliteConnection(sourceConnectionString);
                 await sourceConnection.OpenAsync(cancellationToken);

                 await using var backupConnection = new SqliteConnection($"Data Source={backupFilePath}");
                 await backupConnection.OpenAsync(cancellationToken);

                 sourceConnection.BackupDatabase(backupConnection); // Synchronous but usually fast for SQLite

                 _logger.LogInformation("Database backup completed successfully to '{BackupPath}'.", backupFilePath);
                 return true;
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Database backup failed. Source: '{Source}', Target: '{BackupPath}'", sourceConnectionString, backupFilePath);
                 // Attempt to delete potentially incomplete backup file
                 try { if (File.Exists(backupFilePath)) File.Delete(backupFilePath); } catch { }
                 return false;
             }
        }
        
        public async Task SaveNodeStateAsync(NodeStateCoreInfo nodeStateInfo, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var existingEntity = await context.NodeStates
                                                 .FirstOrDefaultAsync(n => n.NodeId == nodeStateInfo.NodeId, cancellationToken);

                if (existingEntity != null)
                {
                    _logger.LogDebug("Updating existing NodeStateEntity for Node ID: {NodeId}", nodeStateInfo.NodeId);
                    // Map DTO properties to Existing Entity
                    existingEntity.Address = nodeStateInfo.Address;
                    existingEntity.State = (int)nodeStateInfo.State; // Cast Enum to int
                    existingEntity.LastSeen = nodeStateInfo.LastSeen.ToUniversalTime();
                    existingEntity.LastSuccessfulPingTimestamp = nodeStateInfo.LastSuccessfulPingTimestamp?.ToUniversalTime();
                    existingEntity.DiskSpaceAvailableBytes = nodeStateInfo.DiskSpaceAvailableBytes;
                    existingEntity.DiskSpaceTotalBytes = nodeStateInfo.DiskSpaceTotalBytes;
                    existingEntity.StoredChunkCount = nodeStateInfo.StoredChunkCount;
                    // Update other mapped properties as needed

                    context.NodeStates.Update(existingEntity);
                }
                else
                {
                    _logger.LogDebug("Saving new NodeStateEntity for Node ID: {NodeId}", nodeStateInfo.NodeId);
                    // Map DTO properties to New Entity
                    var newEntity = new NodeStateEntity // Use the Persistence Entity type here
                    {
                        NodeId = nodeStateInfo.NodeId,
                        Address = nodeStateInfo.Address,
                        State = (int)nodeStateInfo.State, // Cast Enum to int
                        LastSeen = nodeStateInfo.LastSeen.ToUniversalTime(),
                        LastSuccessfulPingTimestamp = nodeStateInfo.LastSuccessfulPingTimestamp?.ToUniversalTime(),
                        DiskSpaceAvailableBytes = nodeStateInfo.DiskSpaceAvailableBytes,
                        DiskSpaceTotalBytes = nodeStateInfo.DiskSpaceTotalBytes,
                        StoredChunkCount = nodeStateInfo.StoredChunkCount
                        // Map other properties
                    };
                    await context.NodeStates.AddAsync(newEntity, cancellationToken);
                }
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogTrace("Successfully saved node state for Node ID: {NodeId}", nodeStateInfo.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving node state for Node ID: {NodeId}", nodeStateInfo.NodeId);
                throw; // Re-throw to indicate failure
            }
        }

        public async Task<IEnumerable<NodeStateCoreInfo>> GetNodeStatesAsync(IEnumerable<string> nodeIds, CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var nodeIdsList = nodeIds?.Distinct().ToList() ?? new List<string>();
                if (!nodeIdsList.Any())
                {
                    return Enumerable.Empty<NodeStateCoreInfo>();
                }

                var entities = await context.NodeStates
                    .AsNoTracking()
                    .Where(n => nodeIdsList.Contains(n.NodeId))
                    .ToListAsync(cancellationToken);

                // Map Entities to DTOs
                return entities.Select(entity => new NodeStateCoreInfo
                {
                    NodeId = entity.NodeId,
                    Address = entity.Address,
                    State = (NodeStateCore)entity.State, // Cast int to Enum
                    LastSeen = entity.LastSeen,
                    LastSuccessfulPingTimestamp = entity.LastSuccessfulPingTimestamp,
                    DiskSpaceAvailableBytes = entity.DiskSpaceAvailableBytes,
                    DiskSpaceTotalBytes = entity.DiskSpaceTotalBytes,
                    StoredChunkCount = entity.StoredChunkCount
                    // Map other properties
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving node states for Node IDs: {NodeIds}", string.Join(",", nodeIds ?? Enumerable.Empty<string>()));
                return Enumerable.Empty<NodeStateCoreInfo>(); // Return empty on error
            }
        }

        public async Task<IEnumerable<NodeStateCoreInfo>> GetAllNodeStatesAsync(CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            try
            {
                var entities = await context.NodeStates
                    .AsNoTracking()
                    // Optionally filter out self-node if desired: .Where(n => n.NodeId != _nodeOptions.Id)
                    .ToListAsync(cancellationToken);

                // Map Entities to DTOs
                return entities.Select(entity => new NodeStateCoreInfo
                {
                    NodeId = entity.NodeId,
                    Address = entity.Address,
                    State = (NodeStateCore)entity.State, // Cast int to Enum
                    LastSeen = entity.LastSeen,
                    LastSuccessfulPingTimestamp = entity.LastSuccessfulPingTimestamp,
                    DiskSpaceAvailableBytes = entity.DiskSpaceAvailableBytes,
                    DiskSpaceTotalBytes = entity.DiskSpaceTotalBytes,
                    StoredChunkCount = entity.StoredChunkCount
                    // Map other properties
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all node states.");
                return Enumerable.Empty<NodeStateCoreInfo>(); // Return empty on error
            }
        }
    }
}
