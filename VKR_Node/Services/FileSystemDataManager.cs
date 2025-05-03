using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;

namespace VKR_Node.Services
{
    /// <summary>
    /// Implements IDataManager using the local file system for storing chunk data.
    /// Each chunk is stored as a separate file within a directory structure based on FileId.
    /// Includes optimizations for I/O performance, data integrity, and scalability.
    /// </summary>
    public class FileSystemDataManager : IDataManager, IAsyncInitializable, IDisposable
    {
        private readonly ILogger<FileSystemDataManager> _logger;
        private readonly StorageOptions _storageOptions;
        private readonly string _baseStoragePath;
        private readonly string _tempDirPath;
        private readonly string _drivePath;
        
        // Cache frequently accessed drive info to reduce I/O operations
        private readonly ConcurrentDictionary<string, DateTime> _chunkExistsCache = new();
        private long _lastKnownFreeSpace;
        private long _lastKnownTotalSpace;
        private DateTime _lastSpaceCheckTime;
        private readonly TimeSpan _spaceCheckCacheTime = TimeSpan.FromSeconds(10);
        
        // Semaphore to limit concurrent disk operations
        private readonly SemaphoreSlim _diskOperationSemaphore;
        
        // Cleanup timer for temporary files and cache
        private readonly Timer _cleanupTimer;
        private bool _isDisposed;

        /// <summary>
        /// Constructor for FileSystemDataManager.
        /// </summary>
        /// <param name="storageOptions">Options for storage configuration</param>
        /// <param name="logger">Logger instance</param>
        public FileSystemDataManager(IOptions<StorageOptions> storageOptions, ILogger<FileSystemDataManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageOptions = storageOptions?.Value ?? throw new ArgumentNullException(nameof(storageOptions));

            // Validate and resolve the base storage path
            if (string.IsNullOrWhiteSpace(_storageOptions.BasePath))
            {
                _logger.LogError("Storage base path is not configured in StorageOptions");
                throw new InvalidOperationException("Storage base path must be configured");
            }

            // Combine with application base directory if the path is relative
            _baseStoragePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _storageOptions.BasePath));
            _tempDirPath = Path.Combine(_baseStoragePath, ".tmp");

            try
            {
                // Store the drive path for later use
                _drivePath = Path.GetPathRoot(_baseStoragePath) ?? 
                    throw new InvalidOperationException($"Could not determine root path for {_baseStoragePath}");
                
                // Verify drive exists
                var driveInfo = new DriveInfo(_drivePath);
                
                _logger.LogInformation("File system data manager initialized. Base storage path: {BasePath}, Drive: {DriveName}", 
                    _baseStoragePath, driveInfo.Name);
                    
                // Initialize disk space info
                _lastKnownFreeSpace = driveInfo.AvailableFreeSpace;
                _lastKnownTotalSpace = driveInfo.TotalSize;
                _lastSpaceCheckTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing drive info for path: {BasePath}", _baseStoragePath);
                throw;
            }
            
            // Initialize the semaphore with a reasonable limit for I/O operations
            int maxConcurrentOperations = Environment.ProcessorCount * 4;
            _diskOperationSemaphore = new SemaphoreSlim(maxConcurrentOperations);
            
            // Start cleanup timer (run every 15 minutes)
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        }

        /// <summary>
        /// Initializes the data manager by ensuring the base storage directory exists.
        /// Also creates the directory structure for efficient data storage.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                // Create the main storage directory
                if (!Directory.Exists(_baseStoragePath))
                {
                    Directory.CreateDirectory(_baseStoragePath);
                    _logger.LogInformation("Created base storage directory: {BasePath}", _baseStoragePath);
                }
                
                // Create the temporary directory
                if (!Directory.Exists(_tempDirPath))
                {
                    Directory.CreateDirectory(_tempDirPath);
                    _logger.LogDebug("Created temporary files directory: {TempPath}", _tempDirPath);
                }
                
                // Create hash-based subdirectories if enabled
                if (_storageOptions.UseHashBasedDirectories)
                {
                    await CreateHashBasedDirectoryStructureAsync(cancellationToken);
                }
                
                // Verify the directory is writable by creating and deleting a test file
                string testFilePath = Path.Combine(_tempDirPath, $"test_{Guid.NewGuid()}.tmp");
                try
                {
                    await using (var fs = new FileStream(testFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                    {
                        await fs.WriteAsync(new byte[] { 1, 2, 3, 4 }, 0, 4, cancellationToken);
                    }
                    
                    File.Delete(testFilePath);
                    _logger.LogDebug("Write permission confirmed for storage directory");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Storage directory is not writable: {BasePath}", _baseStoragePath);
                    throw new UnauthorizedAccessException($"Storage directory is not writable: {_baseStoragePath}", ex);
                }
                
                // Perform integrity check if enabled
                if (_storageOptions.PerformIntegrityCheckOnStartup)
                {
                    await Task.Run(() => PerformIntegrityCheck(cancellationToken), cancellationToken);
                }
                
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Initialization cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize file system data manager. Error creating/accessing base directory: {BasePath}", _baseStoragePath);
                throw new InvalidOperationException($"Failed to create or access storage directory '{_baseStoragePath}'", ex);
            }
        }

        /// <summary>
        /// Creates the hash-based directory structure for efficient file organization.
        /// </summary>
        private async Task CreateHashBasedDirectoryStructureAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creating hash-based directory structure with depth {Depth}", _storageOptions.HashDirectoryDepth);
            
            // Use hex characters (0-9, a-f) for directories
            char[] hexChars = "0123456789abcdef".ToCharArray();
            int depth = Math.Min(_storageOptions.HashDirectoryDepth, 3); // Cap at 3 for sanity
            
            // For depth 1, create 16 directories (0-f)
            // For depth 2, create 256 directories (00-ff)
            // For depth 3, create 4096 directories (000-fff)
            
            await Task.Run(() => {
                // First level
                foreach (char c1 in hexChars)
                {
                    string dir1 = Path.Combine(_baseStoragePath, c1.ToString());
                    
                    if (!Directory.Exists(dir1))
                    {
                        Directory.CreateDirectory(dir1);
                    }
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Second level (if requested)
                    if (depth >= 2)
                    {
                        foreach (char c2 in hexChars)
                        {
                            string dir2 = Path.Combine(dir1, c2.ToString());
                            
                            if (!Directory.Exists(dir2))
                            {
                                Directory.CreateDirectory(dir2);
                            }
                            
                            // Third level (if requested)
                            if (depth >= 3)
                            {
                                foreach (char c3 in hexChars)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    
                                    string dir3 = Path.Combine(dir2, c3.ToString());
                                    
                                    if (!Directory.Exists(dir3))
                                    {
                                        Directory.CreateDirectory(dir3);
                                    }
                                }
                            }
                        }
                    }
                }
                
                _logger.LogInformation("Successfully created hash-based directory structure");
            }, cancellationToken);
        }

        /// <summary>
        /// Performs an integrity check on the storage directory, removing temporary files
        /// and checking for corrupted files.
        /// </summary>
        private void PerformIntegrityCheck(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting storage integrity check");
            
            try
            {
                // Clean up any temporary files
                int tempFilesRemoved = CleanupTemporaryFiles();
                _logger.LogInformation("Removed {Count} temporary files during integrity check", tempFilesRemoved);
                
                // Check available space 
                var driveInfo = new DriveInfo(_drivePath);
                
                double usedPercentage = 100.0 - ((double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100.0);
                _logger.LogInformation(
                    "Disk space check: {FreeSpace}/{TotalSpace} ({UsedPercent:F1}% used)", 
                    FormatBytes(driveInfo.AvailableFreeSpace), 
                    FormatBytes(driveInfo.TotalSize), 
                    usedPercentage);
                    
                if (usedPercentage > 90)
                {
                    _logger.LogWarning("Disk space critically low: {FreeSpace} available ({Percent:F1}%)", 
                        FormatBytes(driveInfo.AvailableFreeSpace), 100 - usedPercentage);
                }
                
                _logger.LogInformation("Storage integrity check completed successfully");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Storage integrity check cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during storage integrity check");
            }
        }

        /// <summary>
        /// Stores a chunk's data from a stream to a file on the local file system.
        /// Uses an optimized approach with temporary files to ensure data integrity.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk being stored.</param>
        /// <param name="dataStream">The stream containing the chunk data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The chunk's path or ID.</returns>
        public async Task<string> StoreChunkAsync(ChunkModel chunkInfo, Stream dataStream, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            
            if (dataStream == null)
                throw new ArgumentNullException(nameof(dataStream));
                
            if (!dataStream.CanRead)
                throw new ArgumentException("Data stream must be readable", nameof(dataStream));
            
            string finalPath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            string tempPath = GetTemporaryPath(chunkInfo.ChunkId);
            
            _logger.LogDebug("Storing chunk data from stream for {ChunkId} (temp: {TempPath})", 
                chunkInfo.ChunkId, Path.GetFileName(tempPath));

            // Limit concurrent disk operations
            await _diskOperationSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                // Ensure the directory for the fileId exists
                string directoryPath = Path.GetDirectoryName(finalPath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    _logger.LogDebug("Created directory for file ID {FileId} at: {DirectoryPath}", 
                        chunkInfo.FileId, directoryPath);
                }

                // Check if we have enough disk space (with 1% buffer)
                long requiredSpace = dataStream.CanSeek ? dataStream.Length : chunkInfo.Size;
                requiredSpace = (long)(requiredSpace * 1.01);
                
                long availableSpace = await GetFreeDiskSpaceAsync(cancellationToken);
                if (availableSpace < requiredSpace)
                {
                    _logger.LogError("Not enough disk space to store chunk {ChunkId}. Required: {Required}, Available: {Available}", 
                        chunkInfo.ChunkId, FormatBytes(requiredSpace), FormatBytes(availableSpace));
                    throw new IOException($"Not enough disk space to store chunk. Required: {FormatBytes(requiredSpace)}, Available: {FormatBytes(availableSpace)}");
                }

                // First write to a temporary file to avoid partial writes
                await using (var fileStream = new FileStream(
                    tempPath, 
                    FileMode.Create, 
                    FileAccess.Write, 
                    FileShare.None, 
                    bufferSize: 81920, 
                    useAsync: true))
                {
                    await dataStream.CopyToAsync(fileStream, 81920, cancellationToken);
                    await fileStream.FlushAsync(cancellationToken);
                }

                // Move the temporary file to the final location (atomic operation)
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }
                
                File.Move(tempPath, finalPath);

                // Compute hash if requested
                if (!string.IsNullOrEmpty(chunkInfo.ChunkHash))
                {
                    string calculatedHash = await ComputeFileHashAsync(finalPath, cancellationToken);
                    
                    if (chunkInfo.ChunkHash != calculatedHash)
                    {
                        _logger.LogWarning("Hash mismatch for chunk {ChunkId}. Expected: {Expected}, Actual: {Actual}", 
                            chunkInfo.ChunkId, chunkInfo.ChunkHash, calculatedHash);
                            
                        // We proceed anyway, but log the warning
                    }
                }

                // Get file size for logging
                long fileSize = new FileInfo(finalPath).Length;

                // Update cache
                _chunkExistsCache[GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId)] = DateTime.UtcNow;

                _logger.LogInformation("Successfully stored chunk {ChunkId} for file {FileId} at {FilePath}. Size: {Size} bytes",
                    chunkInfo.ChunkId, chunkInfo.FileId, finalPath, fileSize);

                return chunkInfo.ChunkId;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("StoreChunkAsync cancelled for chunk {ChunkId}", chunkInfo.ChunkId);
                // Clean up partially written file
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } 
                catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Failed to cleanup partial chunk file during cancellation: {FilePath}", tempPath); }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing chunk {ChunkId} for file {FileId} from stream to {FilePath}", 
                    chunkInfo.ChunkId, chunkInfo.FileId, finalPath);
                // Clean up partially written files
                try 
                { 
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                } 
                catch (Exception cleanupEx) 
                { 
                    _logger.LogError(cleanupEx, "Failed to cleanup partial chunk files after error: {FilePath}", finalPath); 
                }
                throw new IOException($"Failed to store chunk '{chunkInfo.ChunkId}' for file '{chunkInfo.FileId}'", ex);
            }
            finally
            {
                // Always release the semaphore
                _diskOperationSemaphore.Release();
            }
        }

        /// <summary>
        /// Retrieves a chunk's data as a readable stream from the local file system.
        /// Uses optimized file access for better performance.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to retrieve.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A readable Stream containing the chunk data, or null if the chunk file is not found.</returns>
        public async Task<Stream?> RetrieveChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to retrieve chunk data stream from: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            // Check cache first for quick "not found" responses
            string cacheKey = GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId);
            if (!_chunkExistsCache.ContainsKey(cacheKey) && !File.Exists(filePath))
            {
                _logger.LogWarning("Chunk file not found at: {FilePath}", filePath);
                return null;
            }

            // Limit concurrent disk operations
            await _diskOperationSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Chunk file not found at: {FilePath}", filePath);
                    return null;
                }

                // Update cache
                _chunkExistsCache[cacheKey] = DateTime.UtcNow;

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
                
                return fileStream;
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("Chunk file not found (FileNotFoundException) at: {FilePath}", filePath);
                return null;
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "IO Error opening stream for chunk {ChunkId} from {FilePath}", chunkInfo.ChunkId, filePath);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error opening stream for chunk {ChunkId} from {FilePath}", chunkInfo.ChunkId, filePath);
                throw new IOException($"Failed to retrieve chunk '{chunkInfo.ChunkId}' for file '{chunkInfo.FileId}'", ex);
            }
            finally
            {
                // Always release the semaphore
                _diskOperationSemaphore.Release();
            }
        }

        /// <summary>
        /// Deletes a chunk's data file from the local file system.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to delete.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the file was successfully deleted or did not exist; false otherwise.</returns>
        public async Task<bool> DeleteChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to delete chunk file at: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            // Limit concurrent disk operations
            await _diskOperationSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                if (File.Exists(filePath))
                {
                    // First, try to move to temp with a deleted marker for safer deletion
                    string tempDeletePath = GetTemporaryPath($"del_{chunkInfo.ChunkId}");
                    
                    try
                    {
                        // Move is better than direct delete - more atomic
                        File.Move(filePath, tempDeletePath);
                        File.Delete(tempDeletePath);
                    }
                    catch
                    {
                        // If move fails, try direct delete
                        File.Delete(filePath);
                    }
                    
                    // Remove from cache
                    _chunkExistsCache.TryRemove(GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId), out _);
                    
                    _logger.LogInformation("Successfully deleted chunk file: {FilePath}", filePath);
                    
                    // Clean up empty directories if this was the last chunk
                    await CleanupEmptyDirectoriesAsync(chunkInfo.FileId, filePath);
                    
                    return true;
                }
                else
                {
                    _logger.LogWarning("Attempted to delete chunk file, but it did not exist: {FilePath}", filePath);
                    return true; // Idempotent operation - still successful
                }
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "IO Error deleting chunk file {FilePath}", filePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting chunk file {FilePath}", filePath);
                return false;
            }
            finally
            {
                // Always release the semaphore
                _diskOperationSemaphore.Release();
            }
        }

        /// <summary>
        /// Checks if a chunk exists in the local file system. Uses cache for efficiency.
        /// </summary>
        /// <param name="chunkInfo">Metadata of the chunk to check.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the chunk exists, false otherwise.</returns>
        public async Task<bool> ChunkExistsAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            cancellationToken.ThrowIfCancellationRequested();
            
            // Check cache first for quick responses
            string cacheKey = GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId);
            if (_chunkExistsCache.TryGetValue(cacheKey, out _))
            {
                return true;
            }
            
            try
            {
                // For exists check, we don't need to limit with semaphore (lightweight operation)
                bool exists = File.Exists(filePath);
                if (exists)
                {
                    // Update cache only if file exists
                    _chunkExistsCache[cacheKey] = DateTime.UtcNow;
                }
                
                _logger.LogDebug("Chunk {ChunkId} file at {FilePath} exists: {Exists}", 
                    chunkInfo.ChunkId, filePath, exists);
                    
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking chunk file existence at {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Gets the total available free space on the drive containing the storage path.
        /// Uses caching to reduce frequent disk operations.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total available free space in bytes, or -1 if unable to determine.</returns>
        public Task<long> GetFreeDiskSpaceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                // Check if we need to refresh the cached value
                if (_lastKnownFreeSpace < 0 || 
                    (DateTime.UtcNow - _lastSpaceCheckTime) > _spaceCheckCacheTime)
                {
                    // Create a new DriveInfo object to get fresh information
                    var driveInfo = new DriveInfo(_drivePath);
                    
                    _lastKnownFreeSpace = driveInfo.AvailableFreeSpace;
                    _lastKnownTotalSpace = driveInfo.TotalSize;
                    _lastSpaceCheckTime = DateTime.UtcNow;
                }
                
                return Task.FromResult(_lastKnownFreeSpace);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting free disk space for drive path {DrivePath}", _drivePath);
                return Task.FromResult(-1L);
            }
        }

        /// <summary>
        /// Gets the total size of the drive containing the storage path.
        /// Uses caching to reduce frequent disk operations.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Total drive size in bytes, or -1 if unable to determine.</returns>
        public Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                // Check if we need to refresh the cached value
                if (_lastKnownTotalSpace < 0 || 
                    (DateTime.UtcNow - _lastSpaceCheckTime) > _spaceCheckCacheTime)
                {
                    // Create a new DriveInfo object to get fresh information
                    var driveInfo = new DriveInfo(_drivePath);
                    
                    _lastKnownFreeSpace = driveInfo.AvailableFreeSpace;
                    _lastKnownTotalSpace = driveInfo.TotalSize;
                    _lastSpaceCheckTime = DateTime.UtcNow;
                }
                
                return Task.FromResult(_lastKnownTotalSpace);
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
        /// Uses a subdirectory structure based on FileId or hashing.
        /// </summary>
        /// <param name="fileId">The ID of the parent file.</param>
        /// <param name="chunkId">The unique ID of the chunk.</param>
        /// <returns>The full path to the chunk file.</returns>
        private string GetChunkPath(string fileId, string chunkId)
        {
            // Sanitize IDs to ensure they're safe for file paths
            var sanitizedFileId = SanitizePathComponent(fileId);
            var sanitizedChunkId = SanitizePathComponent(chunkId);

            if (_storageOptions.UseHashBasedDirectories)
            {
                return GetHashBasedPath(sanitizedFileId, sanitizedChunkId);
            }
            else
            {
                // Path structure: BasePath / FileId / ChunkId.chunk
                var directoryPath = Path.Combine(_baseStoragePath, sanitizedFileId);
                var fileName = $"{sanitizedChunkId}.chunk";
                return Path.Combine(directoryPath, fileName);
            }
        }

        /// <summary>
        /// Gets a path based on hash distribution for better file system performance.
        /// </summary>
        private string GetHashBasedPath(string fileId, string chunkId)
        {
            // Take first characters of hash for directory structure
            string hash = chunkId.Length >= 6 ? chunkId.Substring(0, 6) : chunkId.PadLeft(6, '0');
            
            string path = _baseStoragePath;
            int depth = Math.Min(_storageOptions.HashDirectoryDepth, 3);
            
            // Add hash directories levels according to configured depth
            for (int i = 0; i < depth; i++)
            {
                if (i < hash.Length)
                {
                    path = Path.Combine(path, hash[i].ToString());
                }
            }
            
            // Then add fileId and chunk filename
            path = Path.Combine(path, fileId);
            return Path.Combine(path, $"{chunkId}.chunk");
        }

        /// <summary>
        /// Gets a temporary file path for in-progress operations.
        /// </summary>
        private string GetTemporaryPath(string identifier)
        {
            // Ensure temp directory exists
            if (!Directory.Exists(_tempDirPath))
            {
                Directory.CreateDirectory(_tempDirPath);
            }
            
            return Path.Combine(_tempDirPath, $"{identifier}_{Guid.NewGuid()}.tmp");
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

            return input;
        }
        /// <summary>
        /// Gets a key for the chunk exists cache.
        /// </summary>
        private static string GetCacheKey(string fileId, string chunkId)
        {
            return $"{fileId}:{chunkId}";
        }
        
        /// <summary>
        /// Computes a hash for a file, used for integrity verification.
        /// </summary>
        private async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var hashAlgorithm = SHA256.Create();
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            
            byte[] hashBytes = await hashAlgorithm.ComputeHashAsync(fileStream, cancellationToken);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Cleans up empty directories after chunk deletion.
        /// </summary>
        private async Task CleanupEmptyDirectoriesAsync(string fileId, string filePath)
        {
            await Task.Run(() => {
                try
                {
                    // Get the directory containing the chunk
                    string directoryPath = Path.GetDirectoryName(filePath);
                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                        return;
                        
                    // Check if the directory is empty
                    if (Directory.GetFileSystemEntries(directoryPath).Length == 0)
                    {
                        Directory.Delete(directoryPath);
                        _logger.LogDebug("Removed empty directory after chunk deletion: {DirectoryPath}", directoryPath);
                        
                        // If using hash-based directories, try to clean up parent directories
                        if (_storageOptions.UseHashBasedDirectories)
                        {
                            // Get parent directory
                            string parentPath = Path.GetDirectoryName(directoryPath);
                            
                            // Loop up to the configured hash depth
                            for (int i = 0; i < _storageOptions.HashDirectoryDepth; i++)
                            {
                                if (string.IsNullOrEmpty(parentPath) || !Directory.Exists(parentPath) || 
                                    parentPath == _baseStoragePath || Directory.GetFileSystemEntries(parentPath).Length > 0)
                                    break;
                                    
                                Directory.Delete(parentPath);
                                _logger.LogDebug("Removed empty parent directory: {ParentPath}", parentPath);
                                
                                parentPath = Path.GetDirectoryName(parentPath);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error cleaning up empty directories after chunk deletion");
                }
            });
        }

        /// <summary>
        /// Cleanup callback for removing temporary files and pruning cache.
        /// </summary>
        private void CleanupCallback(object state)
        {
            if (_isDisposed) return;
            
            try
            {
                // Cleanup temporary files
                int filesRemoved = CleanupTemporaryFiles();
                
                // Prune the chunk exists cache (keep entries added in the last hour)
                int beforeCount = _chunkExistsCache.Count;
                DateTime cutoffTime = DateTime.UtcNow.AddHours(-1);
                
                foreach (var key in _chunkExistsCache.Keys.ToList())
                {
                    if (_chunkExistsCache.TryGetValue(key, out var timestamp) && timestamp < cutoffTime)
                    {
                        _chunkExistsCache.TryRemove(key, out _);
                    }
                }
                
                int removedCount = beforeCount - _chunkExistsCache.Count;
                
                if (filesRemoved > 0 || removedCount > 0)
                {
                    _logger.LogInformation("Cleanup completed: Removed {TempFiles} temporary files and {CacheEntries} stale cache entries",
                        filesRemoved, removedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup operation");
            }
        }

        /// <summary>
        /// Cleans up temporary files from the temporary directory.
        /// </summary>
        private int CleanupTemporaryFiles()
        {
            int count = 0;
            
            try
            {
                if (!Directory.Exists(_tempDirPath))
                    return 0;
                    
                // Get all temp files older than 1 hour
                var cutoffTime = DateTime.UtcNow.AddHours(-1);
                var tempFiles = new DirectoryInfo(_tempDirPath).GetFiles("*.tmp");
                
                foreach (var file in tempFiles)
                {
                    try
                    {
                        if (file.CreationTimeUtc < cutoffTime || file.LastWriteTimeUtc < cutoffTime)
                        {
                            file.Delete();
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error deleting temporary file: {FileName}", file.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up temporary files");
            }
            
            return count;
        }

        /// <summary>
        /// Validates that the chunk info contains required fields.
        /// </summary>
        /// <param name="chunkInfo">The chunk info to validate.</param>
        private static void ValidateChunkInfo(ChunkModel chunkInfo)
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
        
        /// <summary>
        /// Formats file size in a human-readable format
        /// </summary>
        private static string FormatBytes(long bytes)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Disposes resources used by the data manager.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            
            _isDisposed = true;
            _cleanupTimer?.Dispose();
            _diskOperationSemaphore?.Dispose();
            
            // Clean up cache
            _chunkExistsCache.Clear();
            
            GC.SuppressFinalize(this);
        }
        
        #endregion
    }
}