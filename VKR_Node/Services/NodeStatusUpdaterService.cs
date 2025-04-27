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
    public class NodeStatusUpdaterService : BackgroundService
    {
        private readonly ILogger<NodeStatusUpdaterService> _logger;
        private readonly IServiceProvider _serviceProvider; // Use IServiceProvider for scoped services
        private readonly IOptions<NodeOptions> _nodeOptions;
        private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(1); // How often to update status

        public NodeStatusUpdaterService(
            ILogger<NodeStatusUpdaterService> logger,
            IServiceProvider serviceProvider, // Inject IServiceProvider
            IOptions<NodeOptions> nodeOptions)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _nodeOptions = nodeOptions;
            _logger.LogInformation("NodeStatusUpdaterService initialized for Node {NodeId}. Update interval: {Interval}",
                _nodeOptions.Value?.NodeId ?? "Unknown", _updateInterval);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NodeStatusUpdaterService starting execution loop.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Create a scope to resolve scoped services (like DbContext via IMetadataManager)
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
                        var dataManager = scope.ServiceProvider.GetRequiredService<IDataManager>();
                        string? localNodeId = _nodeOptions.Value?.NodeId;
                        string? localAddress = _nodeOptions.Value?.Address;

                        if (string.IsNullOrEmpty(localNodeId) || string.IsNullOrEmpty(localAddress))
                        {
                            _logger.LogWarning("Cannot update node status: Local NodeId or Address is not configured.");
                            await Task.Delay(_updateInterval, stoppingToken); // Still wait before retrying
                            continue;
                        }

                        _logger.LogDebug("Updating local status for Node {NodeId}...", localNodeId);

                        // Gather Status Information
                        long diskFree = -1;
                        long diskTotal = -1;
                        int chunkCount = -1;

                        try { diskFree = await dataManager.GetFreeDiskSpaceAsync(stoppingToken); }
                        catch (Exception ex) { _logger.LogError(ex, "Error getting free disk space for status update."); }

                        try { diskTotal = await dataManager.GetTotalDiskSpaceAsync(stoppingToken); }
                        catch (Exception ex) { _logger.LogError(ex, "Error getting total disk space for status update."); }

                        try
                        {
                            var localChunks = await metadataManager.GetChunksStoredLocallyAsync(stoppingToken);
                            chunkCount = localChunks?.Count() ?? -1;
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Error getting local chunk count for status update."); }

                        // Prepare DTO
                        var statusInfo = new NodeStateCoreInfo
                        {
                            NodeId = localNodeId,
                            Address = localAddress,
                            State = NodeStateCore.Online, // Assume online if service is running
                            LastSeen = DateTime.UtcNow,
                            DiskSpaceAvailableBytes = diskFree >= 0 ? diskFree : null,
                            DiskSpaceTotalBytes = diskTotal >= 0 ? diskTotal : null,
                            StoredChunkCount = chunkCount >= 0 ? chunkCount : null,
                            LastSuccessfulPingTimestamp = DateTime.UtcNow // Update ping time as well (or get from discovery?)
                        };

                        // Save Status via MetadataManager
                        await metadataManager.SaveNodeStateAsync(statusInfo, stoppingToken);
                        _logger.LogInformation("Successfully updated local node status for {NodeId}.", localNodeId);

                    } // Scope disposed here
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("NodeStatusUpdaterService stopping.");
                    break; // Exit loop if cancellation requested
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception in NodeStatusUpdaterService execution loop.");
                    // Wait before retrying to avoid tight loop on persistent error
                }

                // Wait for the next interval
                try
                {
                    await Task.Delay(_updateInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                     _logger.LogInformation("NodeStatusUpdaterService stopping during delay.");
                    break; // Exit loop if cancellation requested during delay
                }
            }
            _logger.LogInformation("NodeStatusUpdaterService execution stopped.");
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("NodeStatusUpdaterService stopping async.");
            return base.StopAsync(cancellationToken);
        }
    }
}