using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
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

        /// <summary>
        /// Creates a new instance of the NodeStatusUpdaterService.
        /// </summary>
        /// <param name="logger">Logger for this service</param>
        /// <param name="serviceProvider">Service provider for creating scoped services</param>
        /// <param name="nodeOptions">Node configuration options</param>
        /// <param name="dhtOptions">DHT configuration options containing timing settings</param>
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
            
            // Get interval from configuration or use default
            int intervalSeconds = dhtOptions.Value?.StabilizationIntervalSeconds > 0 
                ? dhtOptions.Value.StabilizationIntervalSeconds 
                : 60; // Default to 60 seconds
            
            _updateInterval = TimeSpan.FromSeconds(intervalSeconds);
            _initialDelay = TimeSpan.FromSeconds(15); // Short initial delay
            
            _logger.LogInformation("NodeStatusUpdaterService initialized for Node {NodeId}. Update interval: {Interval}",
                _nodeOptions.NodeId ?? "Unknown", _updateInterval);
        }

        /// <summary>
        /// Main execution loop of the service.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NodeStatusUpdaterService starting execution loop");
            
            // Initial delay to allow other services to initialize
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
                    await UpdateNodeStatusAsync(stoppingToken);
                    
                    // Reset failure count on success
                    if (_consecutiveFailures > 0)
                    {
                        _consecutiveFailures = 0;
                        _logger.LogInformation("Node status update succeeded after previous failures");
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("NodeStatusUpdaterService stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;
                    
                    // Log with higher severity if multiple consecutive failures
                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _logger.LogError(ex, "Critical: {Count} consecutive errors updating node status", 
                            _consecutiveFailures);
                    }
                    else
                    {
                        _logger.LogWarning(ex, "Error updating node status (Failure {Count})", 
                            _consecutiveFailures);
                    }
                }

                // Calculate backoff interval based on failures
                TimeSpan waitInterval = _consecutiveFailures > 0
                    ? TimeSpan.FromSeconds(Math.Min(60, _updateInterval.TotalSeconds * (1 + _consecutiveFailures * 0.5)))
                    : _updateInterval;
                
                try
                {
                    await Task.Delay(waitInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("NodeStatusUpdaterService stopping during delay");
                    break;
                }
            }
            
            _logger.LogInformation("NodeStatusUpdaterService execution stopped");
        }

        /// <summary>
        /// Updates the node's status information in the metadata database.
        /// </summary>
        private async Task UpdateNodeStatusAsync(CancellationToken cancellationToken)
        {
            string? localNodeId = _nodeOptions.NodeId;
            string? localAddress = _nodeNetworkOptionsOptions.ListenAddress;

            if (string.IsNullOrEmpty(localNodeId) || string.IsNullOrEmpty(localAddress))
            {
                _logger.LogError("Cannot update node status: Node ID or Address is not configured");
                throw new InvalidOperationException("Node ID or Address is not configured");
            }

            _logger.LogDebug("Updating status for Node {NodeId}", localNodeId);

            using var scope = _serviceProvider.CreateScope();
            var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
            var dataManager = scope.ServiceProvider.GetRequiredService<IDataManager>();

            // Collect system metrics
            var metrics = await CollectNodeMetricsAsync(dataManager, metadataManager, cancellationToken);

            // Create status info object
            var statusInfo = new NodeModel
            {
                Id = localNodeId,
                Address = localAddress,
                State = NodeStateCore.Online, // Assume online since this code is running
                LastSeen = DateTime.UtcNow,
                DiskSpaceAvailableBytes = metrics.DiskSpaceAvailable,
                DiskSpaceTotalBytes = metrics.DiskSpaceTotal,
                StoredChunkCount = metrics.ChunkCount,
                LastSuccessfulPingTimestamp = DateTime.UtcNow
            };

            // Save status to metadata store
            await metadataManager.SaveNodeStateAsync(statusInfo, cancellationToken);
            _logger.LogInformation("Updated status for Node {NodeId}: DiskFree={DiskFree}, Chunks={ChunkCount}", 
                localNodeId, FormatBytes(metrics.DiskSpaceAvailable), metrics.ChunkCount);
        }

        /// <summary>
        /// Collects various metrics about the node's status.
        /// </summary>
        private async Task<(long DiskSpaceAvailable, long DiskSpaceTotal, int ChunkCount)> CollectNodeMetricsAsync(
            IDataManager dataManager, 
            IMetadataManager metadataManager, 
            CancellationToken cancellationToken)
        {
            long diskFree = -1;
            long diskTotal = -1;
            int chunkCount = -1;

            // Get disk space info
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

            // Get locally stored chunk count
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

            return (diskFree, diskTotal, chunkCount);
        }

        /// <summary>
        /// Formats a byte count into a human-readable size.
        /// </summary>
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

        /// <summary>
        /// Called when the service is stopping.
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("NodeStatusUpdaterService stopping");
            return base.StopAsync(cancellationToken);
        }
    }
}