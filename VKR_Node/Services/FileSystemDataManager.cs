using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using VKR_Core.Models;
using VKR_Core.Services;
using VKR_Node.Configuration;

namespace VKR_Node.Services
{
    public class FileSystemDataManager : IDataManager, IAsyncInitializable, IDisposable
    {
        private readonly ILogger<FileSystemDataManager> _logger;
        private readonly StorageOptions _storageOptions;
        private readonly string _baseStoragePath;
        private readonly string _tempDirPath;
        private readonly string _drivePath;
        
        private readonly ConcurrentDictionary<string, DateTime> _chunkExistsCache = new();
        private long _lastKnownFreeSpace;
        private long _lastKnownTotalSpace;
        private DateTime _lastSpaceCheckTime;
        private readonly TimeSpan _spaceCheckCacheTime = TimeSpan.FromSeconds(10);
        
        private readonly SemaphoreSlim _diskOperationSemaphore;
        
        private readonly Timer _cleanupTimer;
        private bool _isDisposed;
        
        public FileSystemDataManager(IOptions<StorageOptions> storageOptions, ILogger<FileSystemDataManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageOptions = storageOptions.Value ?? throw new ArgumentNullException(nameof(storageOptions));
            
            if (string.IsNullOrWhiteSpace(_storageOptions.BasePath))
            {
                _logger.LogError("Storage base path is not configured in StorageOptions");
                throw new InvalidOperationException("Storage base path must be configured");
            }
            
            _baseStoragePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _storageOptions.BasePath));
            _tempDirPath = Path.Combine(_baseStoragePath, ".tmp");

            try
            {
                _drivePath = Path.GetPathRoot(_baseStoragePath) ?? 
                    throw new InvalidOperationException($"Could not determine root path for {_baseStoragePath}");
                
                var driveInfo = new DriveInfo(_drivePath);
                
                _logger.LogInformation("File system data manager initialized. Base storage path: {BasePath}, Drive: {DriveName}", 
                    _baseStoragePath, driveInfo.Name);
                    
                
                _lastKnownFreeSpace = driveInfo.AvailableFreeSpace;
                _lastKnownTotalSpace = driveInfo.TotalSize;
                _lastSpaceCheckTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing drive info for path: {BasePath}", _baseStoragePath);
                throw;
            }
            
            
            int maxConcurrentOperations = Environment.ProcessorCount * 4;
            _diskOperationSemaphore = new SemaphoreSlim(maxConcurrentOperations);
            
            
            _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                
                if (!Directory.Exists(_baseStoragePath))
                {
                    Directory.CreateDirectory(_baseStoragePath);
                    _logger.LogInformation("Created base storage directory: {BasePath}", _baseStoragePath);
                }
                
                
                if (!Directory.Exists(_tempDirPath))
                {
                    Directory.CreateDirectory(_tempDirPath);
                    _logger.LogDebug("Created temporary files directory: {TempPath}", _tempDirPath);
                }
                
                
                if (_storageOptions.UseHashBasedDirectories)
                {
                    await CreateHashBasedDirectoryStructureAsync(cancellationToken);
                }
                
                
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

        
        
        
        private async Task CreateHashBasedDirectoryStructureAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creating hash-based directory structure with depth {Depth}", _storageOptions.HashDirectoryDepth);
            
            
            char[] hexChars = "0123456789abcdef".ToCharArray();
            int depth = Math.Min(_storageOptions.HashDirectoryDepth, 3); 
            
            
            
            
            
            await Task.Run(() => {
                
                foreach (char c1 in hexChars)
                {
                    string dir1 = Path.Combine(_baseStoragePath, c1.ToString());
                    
                    if (!Directory.Exists(dir1))
                    {
                        Directory.CreateDirectory(dir1);
                    }
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    
                    if (depth >= 2)
                    {
                        foreach (char c2 in hexChars)
                        {
                            string dir2 = Path.Combine(dir1, c2.ToString());
                            
                            if (!Directory.Exists(dir2))
                            {
                                Directory.CreateDirectory(dir2);
                            }
                            
                            
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
       
        private void PerformIntegrityCheck(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting storage integrity check");
            
            try
            {
                
                int tempFilesRemoved = CleanupTemporaryFiles();
                _logger.LogInformation("Removed {Count} temporary files during integrity check", tempFilesRemoved);
                
                 
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

            
            await _diskOperationSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                
                var directoryPath = Path.GetDirectoryName(finalPath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                    _logger.LogDebug("Created directory for file ID {FileId} at: {DirectoryPath}", 
                        chunkInfo.FileId, directoryPath);
                }

                
                long requiredSpace = dataStream.CanSeek ? dataStream.Length : chunkInfo.Size;
                requiredSpace = (long)(requiredSpace * 1.01);
                
                long availableSpace = await GetFreeDiskSpaceAsync(cancellationToken);
                if (availableSpace < requiredSpace)
                {
                    _logger.LogError("Not enough disk space to store chunk {ChunkId}. Required: {Required}, Available: {Available}", 
                        chunkInfo.ChunkId, FormatBytes(requiredSpace), FormatBytes(availableSpace));
                    throw new IOException($"Not enough disk space to store chunk. Required: {FormatBytes(requiredSpace)}, Available: {FormatBytes(availableSpace)}");
                }

                
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

                
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }
                
                File.Move(tempPath, finalPath);

                
                if (!string.IsNullOrEmpty(chunkInfo.ChunkHash))
                {
                    string calculatedHash = await ComputeFileHashAsync(finalPath, cancellationToken);
                    
                    if (chunkInfo.ChunkHash != calculatedHash)
                    {
                        _logger.LogWarning("Hash mismatch for chunk {ChunkId}. Expected: {Expected}, Actual: {Actual}", 
                            chunkInfo.ChunkId, chunkInfo.ChunkHash, calculatedHash);
                            
                        
                    }
                }

                
                long fileSize = new FileInfo(finalPath).Length;

                
                _chunkExistsCache[GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId)] = DateTime.UtcNow;

                _logger.LogInformation("Successfully stored chunk {ChunkId} for file {FileId} at {FilePath}. Size: {Size} bytes",
                    chunkInfo.ChunkId, chunkInfo.FileId, finalPath, fileSize);

                return chunkInfo.ChunkId;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("StoreChunkAsync cancelled for chunk {ChunkId}", chunkInfo.ChunkId);
                
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } 
                catch (Exception cleanupEx) { _logger.LogError(cleanupEx, "Failed to cleanup partial chunk file during cancellation: {FilePath}", tempPath); }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing chunk {ChunkId} for file {FileId} from stream to {FilePath}", 
                    chunkInfo.ChunkId, chunkInfo.FileId, finalPath);
                
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
                
                _diskOperationSemaphore.Release();
            }
        }

        
        
        
        
        
        
        
        public async Task<Stream?> RetrieveChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to retrieve chunk data stream from: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            
            string cacheKey = GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId);
            if (!_chunkExistsCache.ContainsKey(cacheKey) && !File.Exists(filePath))
            {
                _logger.LogWarning("Chunk file not found at: {FilePath}", filePath);
                return null;
            }

            
            await _diskOperationSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Chunk file not found at: {FilePath}", filePath);
                    return null;
                }

                
                _chunkExistsCache[cacheKey] = DateTime.UtcNow;

                
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
                
                _diskOperationSemaphore.Release();
            }
        }
        
        public async Task<bool> DeleteChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            _logger.LogDebug("Attempting to delete chunk file at: {FilePath}", filePath);
            cancellationToken.ThrowIfCancellationRequested();

            
            await _diskOperationSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                if (File.Exists(filePath))
                {
                    
                    string tempDeletePath = GetTemporaryPath($"del_{chunkInfo.ChunkId}");
                    
                    try
                    {
                        
                        File.Move(filePath, tempDeletePath);
                        File.Delete(tempDeletePath);
                    }
                    catch
                    {
                        
                        File.Delete(filePath);
                    }
                    
                    
                    _chunkExistsCache.TryRemove(GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId), out _);
                    
                    _logger.LogInformation("Successfully deleted chunk file: {FilePath}", filePath);
                    
                    
                    await CleanupEmptyDirectoriesAsync(chunkInfo.FileId, filePath);
                    
                    return true;
                }
                else
                {
                    _logger.LogWarning("Attempted to delete chunk file, but it did not exist: {FilePath}", filePath);
                    return true; 
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
                
                _diskOperationSemaphore.Release();
            }
        }
        public async Task<bool> ChunkExistsAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default)
        {
            ValidateChunkInfo(chunkInfo);
            var filePath = GetChunkPath(chunkInfo.FileId, chunkInfo.ChunkId);
            cancellationToken.ThrowIfCancellationRequested();
            
            string cacheKey = GetCacheKey(chunkInfo.FileId, chunkInfo.ChunkId);
            if (_chunkExistsCache.TryGetValue(cacheKey, out _))
            {
                return true;
            }
            
            try
            {
                
                bool exists = File.Exists(filePath);
                if (exists)
                {
                    
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
        
        public Task<long> GetFreeDiskSpaceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                
                if (_lastKnownFreeSpace < 0 || 
                    (DateTime.UtcNow - _lastSpaceCheckTime) > _spaceCheckCacheTime)
                {
                    
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
        
        public Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (_lastKnownTotalSpace < 0 || 
                    (DateTime.UtcNow - _lastSpaceCheckTime) > _spaceCheckCacheTime)
                {
                    
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

        private string GetChunkPath(string fileId, string chunkId)
        {
            
            var sanitizedFileId = SanitizePathComponent(fileId);
            var sanitizedChunkId = SanitizePathComponent(chunkId);

            if (_storageOptions.UseHashBasedDirectories)
            {
                return GetHashBasedPath(sanitizedFileId, sanitizedChunkId);
            }
            else
            {
                
                var directoryPath = Path.Combine(_baseStoragePath, sanitizedFileId);
                var fileName = $"{sanitizedChunkId}.chunk";
                return Path.Combine(directoryPath, fileName);
            }
        }
        
        private string GetHashBasedPath(string fileId, string chunkId)
        {
            
            string hash = chunkId.Length >= 6 ? chunkId.Substring(0, 6) : chunkId.PadLeft(6, '0');
            
            string path = _baseStoragePath;
            int depth = Math.Min(_storageOptions.HashDirectoryDepth, 3);
            
            
            for (int i = 0; i < depth; i++)
            {
                if (i < hash.Length)
                {
                    path = Path.Combine(path, hash[i].ToString());
                }
            }
            path = Path.Combine(path, fileId);
            return Path.Combine(path, $"{chunkId}.chunk");
        }

        private string GetTemporaryPath(string identifier)
        {
            
            if (!Directory.Exists(_tempDirPath))
            {
                Directory.CreateDirectory(_tempDirPath);
            }
            
            return Path.Combine(_tempDirPath, $"{identifier}_{Guid.NewGuid()}.tmp");
        }

        private static string SanitizePathComponent(string input)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Path component cannot be null or empty", nameof(input));

            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                input = input.Replace(c, '_');
            }

            return input;
        }
        
        private static string GetCacheKey(string fileId, string chunkId)
        {
            return $"{fileId}:{chunkId}";
        }
        
        private async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var hashAlgorithm = SHA256.Create();
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            
            byte[] hashBytes = await hashAlgorithm.ComputeHashAsync(fileStream, cancellationToken);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        
        private async Task CleanupEmptyDirectoriesAsync(string fileId, string filePath)
        {
            await Task.Run(() => {
                try
                {
                    var directoryPath = Path.GetDirectoryName(filePath);
                    if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                        return;
                        
                    if (Directory.GetFileSystemEntries(directoryPath).Length == 0)
                    {
                        Directory.Delete(directoryPath);
                        _logger.LogDebug("Removed empty directory after chunk deletion: {DirectoryPath}", directoryPath);
                        
                        if (_storageOptions.UseHashBasedDirectories)
                        {
                            var parentPath = Path.GetDirectoryName(directoryPath);
                            
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
    
        private void CleanupCallback(object state)
        {
            if (_isDisposed) return;
            try
            {
                int filesRemoved = CleanupTemporaryFiles();
                
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
     
        private int CleanupTemporaryFiles()
        {
            int count = 0;
            
            try
            {
                if (!Directory.Exists(_tempDirPath))
                    return 0;
                    
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
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cleanupTimer.Dispose();
            _diskOperationSemaphore.Dispose();
            _chunkExistsCache.Clear();
            GC.SuppressFinalize(this);
        }
        
        #endregion
    }
}