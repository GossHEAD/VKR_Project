using Microsoft.Extensions.DependencyInjection; // Required for IServiceProvider, CreateScope
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VKR_Core.Services;
using VKR_Node.Configuration;

namespace VKR_Node.Services
{
    public class ReplicationHealthService : BackgroundService
    {
        private readonly ILogger<ReplicationHealthService> _logger;
        private readonly IServiceProvider _serviceProvider; // Use IServiceProvider to get scoped services
        private readonly TimeSpan _checkInterval;

        public ReplicationHealthService(
            ILogger<ReplicationHealthService> logger,
            IServiceProvider serviceProvider,
            IOptions<DhtOptions> dhtOptions) // Get interval from DhtOptions
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            // Use a config value for interval, e.g., StabilizationIntervalSeconds * 2, or add a new one
            int intervalSeconds = dhtOptions.Value?.StabilizationIntervalSeconds > 5 ? dhtOptions.Value.StabilizationIntervalSeconds * 2 : 60; // Default 60s
             _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
             _logger.LogInformation("ReplicationHealthService initialized. Check interval: {CheckInterval}", _checkInterval);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReplicationHealthService starting execution loop.");

            // Optional: Add an initial delay before the first check
            // await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting replication health check cycle...");

                try
                {
                    // Create a dependency scope for this cycle
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
                        var replicationManager = scope.ServiceProvider.GetRequiredService<IReplicationManager>();

                        // Get chunks stored locally. Handle potential large lists if necessary.
                        var localChunks = await metadataManager.GetChunksStoredLocallyAsync(stoppingToken);

                        _logger.LogDebug("Found {Count} local chunks to check for replication health.", localChunks?.Count() ?? 0);

                        if (localChunks != null)
                        {
                            // Process chunks sequentially for simplicity, or add parallelism if needed
                            foreach (var chunk in localChunks)
                            {
                                 if (stoppingToken.IsCancellationRequested) break;

                                 // Call the manager to check and potentially heal this chunk
                                 // Pass FileId and ChunkId
                                 await replicationManager.EnsureChunkReplicationAsync(chunk.FileId, chunk.ChunkId, stoppingToken);
                            }
                        }
                    } // Scope disposed

                    _logger.LogInformation("Replication health check cycle finished.");
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled exception during replication health check cycle.");
                    // Avoid crashing the service; wait before the next cycle
                }

                // Wait for the next interval
                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                    break;
                }
            }
            _logger.LogInformation("ReplicationHealthService execution stopped.");
        }

         public override Task StopAsync(CancellationToken cancellationToken)
         {
             _logger.LogInformation("ReplicationHealthService stopping.");
             return base.StopAsync(cancellationToken);
         }
    }
}