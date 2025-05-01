using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;

namespace VKR_Node.Services
{
    /// <summary>
    /// Implements IDataManager using the local file system for storing chunk data.
    /// Each chunk is stored as a separate file within a directory structure based on FileId.
    /// </summary>
    public class FileSystemDataManager : IDataManager, IAsyncInitializable
    {
        private readonly ILogger<FileSystemDataManager> _logger;
        private readonly StorageOptions _storageOptions;
        private readonly string _baseStoragePath;
        private readonly string _drivePath; // Store the drive path instead of a DriveInfo instance

        /// <summary>
        /// Constructor for FileSystemDataManager.
        /// </summary>
        /// <param name="storageOptions">Options for storage configuration</param>
        /// <param name="logger">Logger instance</param>
        public FileSystemDataManager(IOptions<StorageOptions> storageOptions, ILogger<FileSystemDataManager> logger)
        {
            _logger = logger;
            _storageOptions = storageOptions.Value;

            // Validate and resolve the base storage path
            if (string.IsNullOrWhiteSpace(_storageOptions.BasePath))
            {
                _logger.LogError("Storage base path is not configured in StorageOptions");
                throw new InvalidOperationException("Storage base path must be configured");
            }

            // Combine with application base directory if the path is relative
            _baseStoragePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _storageOptions.BasePath));

            try
            {
                // Store the drive path for later use
                _drivePath = Path.GetPathRoot(_baseStoragePath) ?? 
                    throw new InvalidOperationException($"Could not determine root path for {_baseStoragePath}");
                
                // Verify drive exists
                var driveInfo = new DriveInfo(_drivePath);
                
                _logger.LogInformation("File system data manager initialized. Base storage path: {BasePath}, Drive: {DriveName}", 
                    _baseStoragePath, driveInfo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing drive info for path: {BasePath}", _baseStoragePath);
                throw;
            }
        }

        /// <summary>
        /// Initializes the data manager by ensuring the base storage directory exists.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
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
                
                // Verify the directory is writable by creating and deleting a test file
                string testFilePath = Path.Combine(_baseStoragePath, $".test_{Guid.NewGuid()}.tmp");
                try
                {
                    File.WriteAllText(testFilePath, "Test");
                    File.Delete(testFilePath);
                    _logger.LogDebug("Write permission confirmed for storage directory");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Storage directory is not writable: {BasePath}", _baseStoragePath);
                    throw new UnauthorizedAccessException($"Storage directory is not writable: {_baseStoragePath}", ex);
                }
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize file system data manager. Error creating/accessing base directory: {BasePath}", _baseStoragePath);
                throw new InvalidOperationException($"Failed to create or access storage directory '{_baseStoragePath}'", ex);
            }
        }

        /// <summary>
        /// Stores a chunk's data from a stream to a file on the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk being stored.</param>
        /// <param name="dataStream">The stream containing the chunk data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The chunk's path or ID.</returns>
        public async Task<string> StoreChunkAsync(ChunkInfoCore chunkInfo, Stream dataStream, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Storing chunk data from stream to: {FilePath}", filePath);

            try
            {
                // Ensure the directory for the fileId exists
                var directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    _logger.LogDebug("Created directory for file ID {FileId} at: {DirectoryPath}", chunkInfo.FileId, directoryPath);
                }

                // Check if we have enough disk space
                long availableSpace = await GetFreeDiskSpaceAsync(cancellationToken);
                if (dataStream.CanSeek && availableSpace < dataStream.Length)
                {
                    _logger.LogError("Not enough disk space to store chunk {ChunkId}. Required: {Required}B, Available: {Available}B", 
                        chunkInfo.ChunkId, dataStream.Length, availableSpace);
                    throw new IOException($"Not enough disk space to store chunk. Required: {dataStream.Length}B, Available: {availableSpace}B");
                }

                // Open a file stream for writing with proper buffer size
                await using var fileStream = new FileStream(
                    filePath, 
                    FileMode.Create, 
                    FileAccess.Write, 
                    FileShare.None, 
                    bufferSize: 81920, 
                    useAsync: true);

                // Copy data from the input stream to the file stream asynchronously
                await dataStream.CopyToAsync(fileStream, 81920, cancellationToken);

                // Get file size for logging
                long fileSize = new FileInfo(filePath).Length;

                _logger.LogInformation("Successfully stored chunk {ChunkId} for file {FileId} at {FilePath}. Size: {Size} bytes",
                    chunkInfo.ChunkId, chunkInfo.FileId, filePath, fileSize);

                return chunkInfo.ChunkId;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("StoreChunkAsync cancelled for chunk {ChunkId} at {FilePath}", chunkInfo.ChunkId, filePath);
                // Clean up partially written file
                try { if (File.Exists(filePath)) File.Delete(filePath); } 
                catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Failed to cleanup partial chunk file during cancellation: {FilePath}", filePath); }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing chunk {ChunkId} for file {FileId} from stream to {FilePath}", 
                    chunkInfo.ChunkId, chunkInfo.FileId, filePath);
                // Clean up partially written file
                try { if (File.Exists(filePath)) File.Delete(filePath); } 
                catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Failed to cleanup partial chunk file after error: {FilePath}", filePath); }
                throw new IOException($"Failed to store chunk '{chunkInfo.ChunkId}' for file '{chunkInfo.FileId}'", ex);
            }
        }

        /// <summary>
        /// Retrieves a chunk's data as a readable stream from the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to retrieve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A readable Stream containing the chunk data, or null if the chunk file is not found.</returns>
        public Task<Stream?> RetrieveChunkAsync(ChunkInfoCore chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to retrieve chunk data stream from: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Chunk file not found at: {FilePath}", filePath);
                    return Task.FromResult<Stream?>(null);
                }

                // Open the file with proper buffer size and async flag
                Stream fileStream = new FileStream(
                    filePath, 
                    FileMode.Open, 
                    FileAccess.Read, 
                    FileShare.Read, 
                    bufferSize: 81920, 
                    useAsync: true);
                
                _logger.LogInformation("Opened stream for chunk {ChunkId} for file {FileId} from {FilePath}", 
                    chunkInfo.ChunkId, chunkInfo.FileId, filePath);
                
                return Task.FromResult<Stream?>(fileStream);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("Chunk file not found (FileNotFoundException) at: {FilePath}", filePath);
                return Task.FromResult<Stream?>(null);
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "IO Error opening stream for chunk {ChunkId} from {FilePath}", chunkInfo.ChunkId, filePath);
                return Task.FromResult<Stream?>(null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error opening stream for chunk {ChunkId} from {FilePath}", chunkInfo.ChunkId, filePath);
                throw new IOException($"Failed to retrieve chunk '{chunkInfo.ChunkId}' for file '{chunkInfo.FileId}'", ex);
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
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to delete chunk file at: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Successfully deleted chunk file: {FilePath}", filePath);
                    
                    // Clean up empty directory if this was the last chunk
                    var directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath != null && Directory.Exists(directoryPath))
                    {
                        if (Directory.GetFiles(directoryPath).Length == 0)
                        {
                            Directory.Delete(directoryPath);
                            _logger.LogDebug("Removed empty directory after chunk deletion: {DirectoryPath}", directoryPath);
                        }
                    }
                    
                    return Task.FromResult(true);
                }
                else
                {
                    _logger.LogWarning("Attempted to delete chunk file, but it did not exist: {FilePath}", filePath);
                    return Task.FromResult(true); // Idempotent operation - still successful
                }
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "IO Error deleting chunk file {FilePath}", filePath);
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting chunk file {FilePath}", filePath);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Checks if a chunk exists in the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the chunk exists, false otherwise.</returns>
        public Task<bool> ChunkExistsAsync(ChunkInfoCore chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                bool exists = File.Exists(filePath);
                _logger.LogDebug("Chunk {ChunkId} file at {FilePath} exists: {Exists}", chunkInfo.ChunkId, filePath, exists);
                return Task.FromResult(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking chunk file existence at {FilePath}", filePath);
                return Task.FromResult(false);
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
            
            try
            {
                // Create a new DriveInfo object to get fresh information
                var driveInfo = new DriveInfo(_drivePath);
                
                long freeSpace = driveInfo.AvailableFreeSpace;
                _logger.LogDebug("Available free space on drive {DriveName}: {FreeSpace} bytes", driveInfo.Name, freeSpace);
                return Task.FromResult(freeSpace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting free disk space for drive path {DrivePath}", _drivePath);
                return Task.FromResult(-1L);
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
            
            try
            {
                // Create a new DriveInfo object to get fresh information
                var driveInfo = new DriveInfo(_drivePath);
                
                long totalSpace = driveInfo.TotalSize;
                _logger.LogDebug("Total size of drive {DriveName}: {TotalSpace} bytes", driveInfo.Name, totalSpace);
                return Task.FromResult(totalSpace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total disk space for drive path {DrivePath}", _drivePath);
                return Task.FromResult(-1L);
            }
        }

        #region Helper Methods

        /// <summary>
        /// Constructs the full path for a given chunk based on FileId and ChunkId.
        /// Uses a subdirectory structure based on FileId.
        /// </summary>
        /// <param name="fileId">The ID of the parent file.</param>
        /// <param name="chunkId">The unique ID of the chunk.</param>
        /// <returns>The full path to the chunk file.</returns>
        private string GetChunkPath(string fileId, string chunkId)
        {
            // Sanitize IDs to ensure they're safe for file paths
            var sanitizedFileId = SanitizePathComponent(fileId);
            var sanitizedChunkId = SanitizePathComponent(chunkId);

            // Path structure: BasePath / FileId / ChunkId.chunk
            var directoryPath = Path.Combine(_baseStoragePath, sanitizedFileId);
            var fileName = $"{sanitizedChunkId}.chunk";
            return Path.Combine(directoryPath, fileName);
        }

        /// <summary>
        /// Sanitizes a string to make it safe for use in a file path.
        /// </summary>
        /// <param name="input">The input string to sanitize.</param>
        /// <returns>A sanitized string safe for use in a file path.</returns>
        private static string SanitizePathComponent(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Path component cannot be null or empty", nameof(input));

            // Replace invalid characters with underscores
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                input = input.Replace(c, '_');
            }

            // Additional sanitization rules
            input = input.Replace("..", "_"); // Prevent relative path traversal
            
            return input;
        }

        /// <summary>
        /// Validates that the chunk info contains required fields.
        /// </summary>
        /// <param name="chunkInfo">The chunk info to validate.</param>
        private static void ValidateChunkInfo(ChunkInfoCore chunkInfo)
        {
            if (chunkInfo == null)
                throw new ArgumentNullException(nameof(chunkInfo));
            
            if (string.IsNullOrWhiteSpace(chunkInfo.FileId))
                throw new ArgumentException("FileId is required", nameof(chunkInfo));
            
            if (string.IsNullOrWhiteSpace(chunkInfo.ChunkId))
                throw new ArgumentException("ChunkId is required", nameof(chunkInfo));
            
            if (string.IsNullOrWhiteSpace(chunkInfo.StoredNodeId))
                throw new ArgumentException("StoredNodeId is required", nameof(chunkInfo));
        }

        #endregion
    }
}