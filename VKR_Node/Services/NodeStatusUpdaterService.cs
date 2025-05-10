using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NickStrupat;
using VKR_Core.Models;
using VKR_Core.Enums;
using VKR_Core.Services;
using VKR_Node.Configuration;

namespace VKR_Node.Services
{
    /// <summary>
    /// Background service that periodically updates the local node's status in the metadata database.
    /// Collects information like disk space, chunk count, and node state.
    /// </summary>
    public class NodeStatusUpdaterService : BackgroundService
    {
        private readonly ILogger<NodeStatusUpdaterService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly NodeIdentityOptions _nodeOptions;
        private readonly NetworkOptions _nodeNetworkOptionsOptions;
        private readonly TimeSpan _updateInterval;
        private readonly TimeSpan _initialDelay;
        private int _consecutiveFailures;
        private const int MaxConsecutiveFailures = 3;
        private TimeSpan _currentInterval;
        private readonly TimeSpan _minInterval;
        private readonly TimeSpan _maxInterval;
        private readonly TimeSpan _standardInterval;
        private DateTime _lastSuccessfulUpdate = DateTime.MinValue;
        private int _consecutiveSuccesses = 0;
        private bool _isHealthy = true;
        private string _healthStatus = "Starting";
        
        public NodeStatusUpdaterService(
            ILogger<NodeStatusUpdaterService> logger,
            IServiceProvider serviceProvider,
            IOptions<NodeIdentityOptions> nodeOptions,
            IOptions<DhtOptions> dhtOptions,
            IOptions<NetworkOptions> networkOptions)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _nodeOptions = nodeOptions.Value;
            _nodeNetworkOptionsOptions = networkOptions.Value;
            
            int intervalSeconds = dhtOptions.Value?.StabilizationIntervalSeconds > 0 
                ? dhtOptions.Value.StabilizationIntervalSeconds 
                : 60; 
            
            _updateInterval = TimeSpan.FromSeconds(intervalSeconds);
            _initialDelay = TimeSpan.FromSeconds(15); 
            _standardInterval = TimeSpan.FromSeconds(intervalSeconds);
            _minInterval = TimeSpan.FromSeconds(Math.Max(15, intervalSeconds / 4));
            _maxInterval = TimeSpan.FromSeconds(intervalSeconds * 2);
            _currentInterval = _standardInterval;
            
            _logger.LogInformation("NodeStatusUpdaterService initialized for Node {NodeId}. Update interval: {Interval}",
                _nodeOptions.NodeId ?? "Unknown", _updateInterval);
        }

        public (bool IsHealthy, string Status, DateTime LastSuccessfulUpdate) GetHealthStatus()
        {
            return (_isHealthy, _healthStatus, _lastSuccessfulUpdate);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NodeStatusUpdaterService starting execution loop");
            
            try
            {
                await Task.Delay(_initialDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("NodeStatusUpdaterService stopped during initial delay");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    bool success = await UpdateNodeStatusAsync(stoppingToken);
        
                    if (success)
                    {
                        _consecutiveSuccesses++;
                        _lastSuccessfulUpdate = DateTime.UtcNow;
                        _isHealthy = true;
                        _healthStatus = "Healthy";
            
                        if (_consecutiveSuccesses > 3 && _currentInterval < _maxInterval)
                        {
                            _currentInterval = TimeSpan.FromSeconds(Math.Min(
                                _currentInterval.TotalSeconds * 1.2, 
                                _maxInterval.TotalSeconds));
                    
                            _logger.LogDebug("Increasing update interval to {Interval}s after {SuccessCount} successes", 
                                _currentInterval.TotalSeconds, _consecutiveSuccesses);
                        }
                    }
                    else
                    {
                        _consecutiveFailures++;
                        
                        if (_consecutiveFailures >= MaxConsecutiveFailures)
                        {
                            _isHealthy = false;
                            _healthStatus = $"Unhealthy: {_consecutiveFailures} consecutive update failures";
                            _logger.LogError("Node status updater is unhealthy: {FailureCount} consecutive failures", 
                                _consecutiveFailures);
                        }
            
                        _currentInterval = TimeSpan.FromSeconds(Math.Max(
                            _currentInterval.TotalSeconds * 0.7, 
                            _minInterval.TotalSeconds));
                
                        _logger.LogDebug("Decreasing update interval to {Interval}s after failure", 
                            _currentInterval.TotalSeconds);
                    }
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    _isHealthy = false;
                    _healthStatus = $"Error: {ex.Message}";
                    
                    _consecutiveSuccesses = 0;
                    _currentInterval = _minInterval; 
                    _logger.LogError(ex, "Error updating node status");
                }
    
                try
                {
                    await Task.Delay(_currentInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("NodeStatusUpdaterService stopping");
                    break;
                }
            }
            
            _logger.LogInformation("NodeStatusUpdaterService execution stopped");
        }
        
        private async Task<bool> UpdateNodeStatusAsync(CancellationToken cancellationToken)
        {
            string? localNodeId = _nodeOptions.NodeId;
            string? localAddress = _nodeNetworkOptionsOptions.ListenAddress;

            if (string.IsNullOrEmpty(localNodeId) || string.IsNullOrEmpty(localAddress))
            {
                _logger.LogError("Cannot update node status: Node ID or Address is not configured");
                return false;
            }

            _logger.LogDebug("Updating status for Node {NodeId}", localNodeId);

            try 
            {
                using var scope = _serviceProvider.CreateScope();
                var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
                var dataManager = scope.ServiceProvider.GetRequiredService<IDataManager>();

                var metrics = await CollectNodeMetricsAsync(dataManager, metadataManager, cancellationToken);

                var statusInfo = new NodeModel
                {
                    Id = localNodeId,
                    Address = localAddress,
                    State = NodeStateCore.Online, 
                    LastSeen = DateTime.UtcNow,
                    DiskSpaceAvailableBytes = metrics.DiskSpaceAvailable,
                    DiskSpaceTotalBytes = metrics.DiskSpaceTotal,
                    StoredChunkCount = metrics.ChunkCount,
                    LastSuccessfulPingTimestamp = DateTime.UtcNow,
                    CpuUsagePercent = metrics.CpuUsage,
                    MemoryUsedBytes = metrics.MemoryUsed,
                    MemoryTotalBytes = metrics.MemoryTotal
                };

                await metadataManager.SaveNodeStateAsync(statusInfo, cancellationToken);
                _logger.LogInformation("Updated status for Node {NodeId}: DiskFree={DiskFree}, Chunks={ChunkCount}, CPU={CpuUsage}%", 
                    localNodeId, FormatBytes(metrics.DiskSpaceAvailable), metrics.ChunkCount, metrics.CpuUsage);
                
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Node status update cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update status for Node {NodeId}", localNodeId);
                return false;
            }
        }

        private async Task<(long DiskSpaceAvailable, long DiskSpaceTotal, int ChunkCount, double CpuUsage, long MemoryUsed, long MemoryTotal)> 
            CollectNodeMetricsAsync(IDataManager dataManager, IMetadataManager metadataManager, CancellationToken cancellationToken)
        {
            long diskFree = -1;
            long diskTotal = -1;
            int chunkCount = -1;
            double cpuUsage = -1;
            long memoryUsed = -1;
            long memoryTotal = -1;

            try
            {
                diskFree = await dataManager.GetFreeDiskSpaceAsync(cancellationToken);
                _logger.LogTrace("Free disk space: {DiskFree}", FormatBytes(diskFree));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting free disk space");
            }

            try
            {
                diskTotal = await dataManager.GetTotalDiskSpaceAsync(cancellationToken);
                _logger.LogTrace("Total disk space: {DiskTotal}", FormatBytes(diskTotal));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total disk space");
            }

            try
            {
                var localChunks = await metadataManager.GetChunksStoredLocallyAsync(cancellationToken);
                chunkCount = localChunks?.Count() ?? 0;
                _logger.LogTrace("Local chunk count: {ChunkCount}", chunkCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting local chunk count");
            }
            
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                await Task.Delay(200, cancellationToken); 
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds * Environment.ProcessorCount;
                
                cpuUsage = Math.Round(cpuUsedMs / totalMsPassed * 100, 1);
                _logger.LogTrace("CPU usage: {CpuUsage}%", cpuUsage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error measuring CPU usage");
            }
            
            try
            {
                var process = Process.GetCurrentProcess();
                memoryUsed = process.WorkingSet64;
                _logger.LogTrace("Memory used: {MemoryUsed}", FormatBytes(memoryUsed));
                
                memoryTotal = GetTotalPhysicalMemory();
                _logger.LogTrace("Total memory: {MemoryTotal}", FormatBytes(memoryTotal));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting memory information");
            }

            return (diskFree, diskTotal, chunkCount, cpuUsage, memoryUsed, memoryTotal);
        }

        private long GetTotalPhysicalMemory()
        {
            try
            {
                var computerInfo = new ComputerInfo();
                return (long)computerInfo.TotalPhysicalMemory;
            }
            catch
            {
                return -1;
            }
        }
        
        private string FormatBytes(long bytes)
        {
            if (bytes < 0) return "Unknown";
            
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            
            return $"{len:0.##} {sizes[order]}";
        }
        
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("NodeStatusUpdaterService stopping");
            return base.StopAsync(cancellationToken);
        }
    }
}