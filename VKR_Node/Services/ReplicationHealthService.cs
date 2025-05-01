using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Services;
using VKR_Node.Configuration;

namespace VKR_Node.Services
{
    /// <summary>
    /// Background service that periodically checks and ensures proper replication
    /// levels for all locally stored chunks
    /// </summary>
    public class ReplicationHealthService : BackgroundService
    {
        private readonly ILogger<ReplicationHealthService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeSpan _checkInterval;
        private readonly int _maxParallelOperations;

        public ReplicationHealthService(
            ILogger<ReplicationHealthService> logger,
            IServiceProvider serviceProvider,
            IOptions<DhtOptions> dhtOptions)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            // Configuration values
            int intervalSeconds = dhtOptions.Value?.ReplicationCheckIntervalSeconds > 5 
                ? dhtOptions.Value.ReplicationCheckIntervalSeconds 
                : 60; // Default 60s
            
            _maxParallelOperations = dhtOptions.Value?.ReplicationMaxParallelism > 0 
                ? dhtOptions.Value.ReplicationMaxParallelism 
                : 5; // Default 5 parallel operations
            
            _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
            
            _logger.LogInformation("ReplicationHealthService initialized. Check interval: {CheckInterval}, Parallelism: {Parallelism}", 
                _checkInterval, _maxParallelOperations);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReplicationHealthService starting execution loop");

            // Add an initial delay to allow system to stabilize
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting replication health check cycle");

                try
                {
                    await PerformHealthCheckAsync(stoppingToken);
                    _logger.LogInformation("Replication health check cycle completed");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Replication health check cancelled due to service stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during replication health check cycle");
                }

                // Wait until the next interval
                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("ReplicationHealthService stopping during delay");
                    break;
                }
            }
            
            _logger.LogInformation("ReplicationHealthService execution stopped");
        }

        /// <summary>
        /// Performs a comprehensive health check on all locally stored chunks
        /// </summary>
        private async Task PerformHealthCheckAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var metadataManager = scope.ServiceProvider.GetRequiredService<IMetadataManager>();
            var replicationManager = scope.ServiceProvider.GetRequiredService<IReplicationManager>();

            // Get all chunks stored locally
            var localChunks = await metadataManager.GetChunksStoredLocallyAsync(cancellationToken);
            if (localChunks == null || !localChunks.Any())
            {
                _logger.LogInformation("No local chunks found to check for replication health");
                return;
            }

            _logger.LogInformation("Found {Count} local chunks to check for replication health", localChunks.Count());

            // Using SemaphoreSlim to limit parallelism
            using var semaphore = new SemaphoreSlim(_maxParallelOperations);
            var tasks = new List<Task>();

            foreach (var chunk in localChunks)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Wait for a slot to become available
                await semaphore.WaitAsync(cancellationToken);

                // Process each chunk in a separate task
                tasks.Add(Task.Run(async () => {
                    try
                    {
                        await replicationManager.EnsureChunkReplicationAsync(
                            chunk.FileId, 
                            chunk.ChunkId, 
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking replication for Chunk {ChunkId}", chunk.ChunkId);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ReplicationHealthService stopping");
            return base.StopAsync(cancellationToken);
        }
    }
}