using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO; // Required for Path, File, Directory operations
using System.Threading.Tasks;
using VKR_Core.Models;
using VKR_Core.Services; // Required for IDataManager interface
using VKR_Node.Configuration; // Required for StorageOptions

namespace VKR_Node.Services
{
    /// <summary>
    /// Implements IDataManager using the local file system for storing chunk data.
    /// Each chunk is stored as a separate file within a directory structure based on FileId.
    /// </summary>
    public class FileSystemDataManager : IDataManager, IAsyncInitializable // Implement IAsyncInitializable
    {
        private readonly ILogger<FileSystemDataManager> _logger;
        private readonly StorageOptions _storageOptions;
        private readonly string _baseStoragePath; 
        private readonly DriveInfo? _driveInfo; 
        
        public FileSystemDataManager(IOptions<StorageOptions> storageOptions, ILogger<FileSystemDataManager> logger)
        {
            _logger = logger;
            _storageOptions = storageOptions.Value;

            // Validate and resolve the base storage path on instantiation
            if (string.IsNullOrWhiteSpace(_storageOptions.BasePath))
            {
                _logger.LogError("Storage base path is not configured in StorageOptions.");
                throw new InvalidOperationException("Storage base path must be configured.");
            }

            // Combine with application base directory if the path is relative
            _baseStoragePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _storageOptions.BasePath));
            _logger.LogInformation("File system data manager initialized. Base storage path: {BasePath}", _baseStoragePath);
        }

        /// <summary>
        /// Initializes the data manager by ensuring the base storage directory exists.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation (though not actively used in this implementation).</param>
        public Task InitializeAsync(CancellationToken cancellationToken = default) // Added CancellationToken
        {
            // Check for cancellation before proceeding, although operation is quick.
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(_baseStoragePath))
                {
                    Directory.CreateDirectory(_baseStoragePath);
                    _logger.LogInformation("Created base storage directory: {BasePath}", _baseStoragePath);
                }
                else
                {
                    _logger.LogDebug("Base storage directory already exists: {BasePath}", _baseStoragePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize file system data manager. Error creating base directory: {BasePath}", _baseStoragePath);
                throw new InvalidOperationException($"Failed to create or access storage directory '{_baseStoragePath}'.", ex);
            }
            return Task.CompletedTask;
        }


        /// <summary>
        /// Stores a chunk's data from a stream to a file on the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk being stored.</param>
        /// <param name="dataStream">The stream containing the chunk data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The ChunkId of the stored chunk.</returns>
        public async Task<string> StoreChunkAsync(ChunkInfoCore chunkInfo, Stream dataStream, CancellationToken cancellationToken = default)
        {
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to store chunk data from stream to: {FilePath}", filePath);

            try
            {
                // Ensure the directory for the fileId exists
                var directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    _logger.LogDebug("Created directory for file ID {FileId} at: {DirectoryPath}", chunkInfo.FileId, directoryPath);
                }

                // Open a file stream for writing (creates or overwrites)
                // Use a buffer, async, and configure FileShare appropriately
                await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true)) // 80KB buffer
                {
                    // Copy data from the input stream to the file stream asynchronously
                    await dataStream.CopyToAsync(fileStream, cancellationToken);
                } // fileStream is disposed here, ensuring data is flushed.

                // Get file size for logging (optional)
                long fileSize = -1;
                try { fileSize = new FileInfo(filePath).Length; } catch { /* Ignore error getting size */ }

                _logger.LogInformation("Successfully stored chunk {ChunkId} for file {FileId} at {FilePath}. Size: {Size} bytes.",
                                       chunkInfo.ChunkId, chunkInfo.FileId, filePath, fileSize >= 0 ? fileSize.ToString() : "N/A");

                return chunkInfo.ChunkId; // Return the ID as per interface (assuming)
            }
            catch (OperationCanceledException)
            {
                 _logger.LogWarning("StoreChunkAsync cancelled for chunk {ChunkId} at {FilePath}", chunkInfo.ChunkId, filePath);
                 // Clean up potentially partially written file
                 try { if (File.Exists(filePath)) File.Delete(filePath); } catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Failed to cleanup partial chunk file during cancellation: {FilePath}", filePath); }
                 throw; // Re-throw cancellation exception
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing chunk {ChunkId} for file {FileId} from stream to {FilePath}", chunkInfo.ChunkId, chunkInfo.FileId, filePath);
                // Clean up potentially partially written file
                try { if (File.Exists(filePath)) File.Delete(filePath); } catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Failed to cleanup partial chunk file after error: {FilePath}", filePath); }
                throw new IOException($"Failed to store chunk '{chunkInfo.ChunkId}' for file '{chunkInfo.FileId}'.", ex);
            }
        }
        

        /// <summary>
        /// Retrieves a chunk's data as a readable stream from the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to retrieve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A readable Stream containing the chunk data, or null if the chunk file is not found. The caller is responsible for disposing the stream.</returns>
        public Task<Stream?> RetrieveChunkAsync(ChunkInfoCore chunkInfo, CancellationToken cancellationToken = default)
        {
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to retrieve chunk data stream from: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Chunk file not found at: {FilePath}", filePath);
                    return Task.FromResult<Stream?>(null); // Return null Task result
                }

                // Open the file for reading asynchronously. Caller must dispose the stream.
                Stream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true); // 80KB buffer
                _logger.LogInformation("Opened stream for chunk {ChunkId} for file {FileId} from {FilePath}", chunkInfo.ChunkId, chunkInfo.FileId, filePath);
                return Task.FromResult<Stream?>(fileStream);
            }
            catch (FileNotFoundException) // Should be caught by File.Exists, but good practice
            {
                 _logger.LogWarning("Chunk file not found (FileNotFoundException) at: {FilePath}", filePath);
                 return Task.FromResult<Stream?>(null);
            }
            catch (IOException ioEx) // Permissions, etc.
            {
                 _logger.LogError(ioEx, "IO Error opening stream for chunk {ChunkId} from {FilePath}", chunkInfo.ChunkId, filePath);
                 return Task.FromResult<Stream?>(null); // Cannot provide stream
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error opening stream for chunk {ChunkId} from {FilePath}", chunkInfo.ChunkId, filePath);
                throw new IOException($"Failed to retrieve chunk '{chunkInfo.ChunkId}' for file '{chunkInfo.FileId}'.", ex); // Re-throw unexpected errors
            }
        }
        
        /// <summary>
        /// Deletes a chunk's data file from the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the file was successfully deleted or did not exist; false otherwise.</returns>
        public Task<bool> DeleteChunkAsync(ChunkInfoCore chunkInfo, CancellationToken cancellationToken = default)
        {
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to delete chunk file at: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();
            bool fileExisted = false;

            try
            {
                if (File.Exists(filePath))
                {
                    fileExisted = true;
                    File.Delete(filePath);
                    _logger.LogInformation("Successfully deleted chunk file: {FilePath}", filePath);
                    return Task.FromResult(true);
                    // Consider optional empty directory cleanup here or in a separate task
                }
                else
                {
                    _logger.LogWarning("Attempted to delete chunk file, but it did not exist: {FilePath}", filePath);
                    return Task.FromResult(true);
                    // Idempotency: Consider success if already gone
                }
            }
            catch (IOException ioEx) // File in use, permissions
            {
                _logger.LogError(ioEx, "IO Error deleting chunk file {FilePath}", filePath);
                return Task.FromResult(false); // Indicate failure
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting chunk file {FilePath}", filePath);
                return Task.FromResult(false); // Indicate failure
            }
        }
        
        
        /// <summary>
        /// Gets the total available free space on the drive containing the storage path.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total available free space in bytes, or -1 if unable to determine.</returns>
        public Task<long> GetFreeDiskSpaceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_driveInfo == null)
            {
                _logger.LogWarning("Cannot get free disk space: DriveInfo is not available.");
                return Task.FromResult(-1L); // Indicate unavailable
            }

            try
            {
                // Data is refreshed when property is accessed, no Refresh() method needed.
                long freeSpace = _driveInfo.AvailableFreeSpace; // Gets space available to the current user
                _logger.LogDebug("Available free space on drive {DriveName}: {FreeSpace} bytes", _driveInfo.Name, freeSpace);
                return Task.FromResult(freeSpace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting free disk space for drive {DriveName}", _driveInfo?.Name ?? "N/A");
                return Task.FromResult(-1L); // Indicate error
            }
        }

        /// <summary>
        /// Gets the total size of the drive containing the storage path.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total drive size in bytes, or -1 if unable to determine.</returns>
        public Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_driveInfo == null)
            {
                _logger.LogWarning("Cannot get total disk space: DriveInfo is not available.");
                return Task.FromResult(-1L); // Indicate unavailable
            }

            try
            {
                // Data is refreshed when property is accessed, no Refresh() method needed.
                long totalSpace = _driveInfo.TotalSize;
                _logger.LogDebug("Total size of drive {DriveName}: {TotalSpace} bytes", _driveInfo.Name, totalSpace);
                return Task.FromResult(totalSpace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total disk space for drive {DriveName}", _driveInfo?.Name ?? "N/A");
                return Task.FromResult(-1L); // Indicate error
            }
        }
        
        // --- Helper Methods ---

        /// <summary>
        /// Constructs the full path for a given chunk based on FileId and ChunkId.
        /// Uses a subdirectory structure based on FileId.
        /// </summary>
        /// <param name="fileId">The ID of the parent file.</param>
        /// <param name="chunkId">The unique ID of the chunk.</param>
        /// <returns>The full path to the chunk file.</returns>
        private string GetChunkPath(string fileId, string chunkId)
        {
            // Basic sanitization (replace directory separators)
            // Consider more robust sanitization based on expected ID formats
            var sanitizedFileId = fileId.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
            var sanitizedChunkId = chunkId.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');

            // Path: BasePath / FileId / ChunkId.chunk
            var directoryPath = Path.Combine(_baseStoragePath, sanitizedFileId);
            var fileName = $"{sanitizedChunkId}.chunk";
            return Path.Combine(directoryPath, fileName);
        }

        public Task<bool> ChunkExistsAsync(ChunkInfoCore chunkInfo, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

         // --- Obsolete Methods (Kept for reference during transition, remove later) ---
         // These were the methods from the previous version that didn't match the interface.

        /// <summary>
        /// OBSOLETE version: Stores a chunk's data as a file on the local file system.
        /// </summary>
        [Obsolete("Use StoreChunkAsync(ChunkInfoCore, Stream, CancellationToken) instead.")]
        public async Task StoreChunkAsync(string fileId, string chunkId, byte[] data)
        {
            var filePath = GetChunkPath(fileId, chunkId);
             _logger.LogDebug("[Obsolete] Attempting to store chunk data at: {FilePath}", filePath);
             var directoryPath = Path.GetDirectoryName(filePath);
             if (directoryPath != null && !Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
             await File.WriteAllBytesAsync(filePath, data);
        }

        /// <summary>
        /// OBSOLETE version: Retrieves a chunk's data from a file on the local file system.
        /// </summary>
        [Obsolete("Use RetrieveChunkAsync(ChunkInfoCore, CancellationToken) which returns a Stream instead.")]
        public async Task<byte[]?> RetrieveChunkAsync(string fileId, string chunkId)
        {
             var filePath = GetChunkPath(fileId, chunkId);
             _logger.LogDebug("[Obsolete] Attempting to retrieve chunk data from: {FilePath}", filePath);
             if (!File.Exists(filePath)) return null;
             return await File.ReadAllBytesAsync(filePath);
        }

        /*
         /// <summary>
        /// OBSOLETE version: Deletes a chunk's data file from the local file system.
        /// </summary>
        [Obsolete("Use DeleteChunkAsync(ChunkInfoCore, CancellationToken) instead.")]
        public Task<bool> DeleteChunkAsync(string fileId, string chunkId)
        {
             var filePath = GetChunkPath(fileId, chunkId);
             _logger.LogDebug("[Obsolete] Attempting to delete chunk file at: {FilePath}", filePath);
             try { if (File.Exists(filePath)) File.Delete(filePath); return Task.FromResult(true); }
             catch { return Task.FromResult(false); }
        }
        */
        
    }
}
