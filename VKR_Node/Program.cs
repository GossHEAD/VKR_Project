using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core; // Required for HttpProtocols
using Microsoft.EntityFrameworkCore; // Required for Migrate, IDbContextFactory
using Microsoft.Extensions.Configuration; // Required for ConfigurationBuilder
using Microsoft.Extensions.DependencyInjection; // Required for IServiceCollection, GetRequiredService
using Microsoft.Extensions.Hosting; // Required for IHost, Host
using Microsoft.Extensions.Logging; // Required for ILogger
using Microsoft.Extensions.Options; // Required for IOptions access
using System.Net; // Required for IPAddress
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Mapping;
using VKR_Node.Persistance;
using VKR_Node.Services;
using VKR_Node.Services.FileService;
using VKR_Node.Services.FileService.FileInterface;
using VKR_Node.Services.NodeServices;
using VKR_Node.Services.NodeServices.NodeInterfaces;
using VKR_Node.Services.Utilities;
using VKR.Node; // Required for Task

namespace VKR_Node
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
             Console.WriteLine($"[Main] Application started with args: {string.Join(" ", args)}");

            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                 var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                 var nodeOpts = scope.ServiceProvider.GetRequiredService<IOptions<NodeIdentityOptions>>().Value;
                 var networkOpts = scope.ServiceProvider.GetRequiredService<IOptions<NetworkOptions>>().Value;
                 logger.LogInformation("--- Verifying Loaded Configuration ---");
                 try
                 {
                     if (networkOpts?.KnownNodes != null)
                     {
                         logger.LogInformation("--- Checking KnownNodes Addresses After Binding ---");
                         foreach (var knownNode in networkOpts.KnownNodes)
                         {
                             logger.LogInformation(
                                 "NodeId: {NodeId}, Address: '{Address}' (Is Null or Empty: {IsNull})",
                                 knownNode.NodeId,
                                 knownNode.Address, 
                                 string.IsNullOrEmpty(knownNode.Address));
                         }

                         logger.LogInformation("--- Finished Checking KnownNodes ---");
                     }
                     
                     else
                     {
                         logger.LogError("KnownNodes list is NULL after configuration binding!");
                     }

                     var dbOpts = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                     var storageOpts = scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;

                     logger.LogInformation("[Config Check] Node.Id: {Value}", nodeOpts?.NodeId ?? "NULL");
                     logger.LogInformation("[Config Check] Node.IpAddress: {Value}", networkOpts?.ListenAddress ?? "NULL");
                     logger.LogInformation("[Config Check] Database.DatabasePath: {Value}", dbOpts?.DatabasePath ?? "NULL");
                     logger.LogInformation("[Config Check] Storage.BasePath: {Value}", storageOpts?.BasePath ?? "NULL");
                     
                     logger.LogInformation("[Config Check] Node.KnownNodes Count: {Value}", networkOpts?.KnownNodes?.Count ?? 0);

                 }
                 catch (Exception ex)
                 {
                      logger.LogError(ex, "[Config Check] Error resolving or logging options.");
                 }
                 logger.LogInformation("--- End Configuration Verification ---");
            }

            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Application starting. Performing asynchronous initializations...");

                try
                {
                    logger.LogInformation("Applying database migrations...");
                    var dbContextFactory = services.GetRequiredService<IDbContextFactory<NodeDbContext>>();
                    await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
                    {
                        await dbContext.Database.MigrateAsync();
                    }
                    logger.LogInformation("Database migrations applied successfully.");

                    var initializables = services.GetServices<IAsyncInitializable>();
                    if (initializables != null && initializables.Any())
                    {
                         logger.LogInformation("Found {Count} services implementing IAsyncInitializable.", initializables.Count());
                        foreach (var initializable in initializables)
                        {
                            logger.LogDebug("Initializing service: {ServiceType}", initializable.GetType().Name);
                            await initializable.InitializeAsync();
                            logger.LogDebug("Finished initializing service: {ServiceType}", initializable.GetType().Name);
                        }
                         logger.LogInformation("All IAsyncInitializable services initialized.");
                    } else {
                         logger.LogInformation("No services implementing IAsyncInitializable found.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "An error occurred during asynchronous initialization or database migration. Application will terminate. Exception Details: {ExceptionDetails}", ex.ToString());
                    return;
                }
            }

            var hostLogger = host.Services.GetRequiredService<ILogger<Program>>();
            hostLogger.LogInformation("Starting gRPC Node host...");
            await host.RunAsync();
             hostLogger.LogInformation("gRPC Node host stopped.");
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    Console.WriteLine($"[ConfigureAppConfiguration] Base Path: {hostingContext.HostingEnvironment.ContentRootPath}");
                    Console.WriteLine($"[ConfigureAppConfiguration] Environment: {hostingContext.HostingEnvironment.EnvironmentName}");

                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    Console.WriteLine($"[ConfigureAppConfiguration] Added appsettings.json (Optional: true)");
                    config.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    Console.WriteLine($"[ConfigureAppConfiguration] Added appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json (Optional: true)");

                    
                    string? configFile = null;
                    string? detectedArg = null; 
                    for (int i = 0; i < args.Length; i++)
                    {
                        string currentArg = args[i];
                        string argName = "";
                        string argValue = "";

                        if (currentArg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase)) { argName = "--config="; argValue = currentArg.Substring(argName.Length); }
                        else if (currentArg.StartsWith("--ConfigPath=", StringComparison.OrdinalIgnoreCase)) { argName = "--ConfigPath="; argValue = currentArg.Substring(argName.Length); }
                        else if (currentArg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i+1].StartsWith("--")) { argName = "--config"; argValue = args[i + 1]; i++; /* Skip next arg */ }
                        else if (currentArg.Equals("--ConfigPath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i+1].StartsWith("--")) { argName = "--ConfigPath"; argValue = args[i + 1]; i++; /* Skip next arg */ }

                        if (!string.IsNullOrEmpty(argName))
                        {
                            configFile = argValue;
                            detectedArg = $"{argName}{(argName.EndsWith("=") ? "" : " ")}{argValue}"; 
                            Console.WriteLine($"[ConfigureAppConfiguration] Detected config file argument: {detectedArg}");
                            break; 
                        }
                    }

                    if (!string.IsNullOrEmpty(configFile))
                    {
                        string configFilePath = Path.GetFullPath(configFile);
                        Console.WriteLine($"[ConfigureAppConfiguration] Attempting to load node-specific config from resolved path: {configFilePath}"); 
                        if (File.Exists(configFilePath))
                        {
                            config.AddJsonFile(configFilePath, optional: false, reloadOnChange: true); 
                            Console.WriteLine($"[ConfigureAppConfiguration] SUCCESS: Added node-specific config: {configFilePath}");
                        }
                        else
                        {
                            Console.WriteLine($"[ConfigureAppConfiguration] ERROR: Node-specific config file NOT FOUND at resolved path: {configFilePath}");
                        }
                    }
                    else
                    {
                         Console.WriteLine("[ConfigureAppConfiguration] Info: No node-specific config file argument (--config or --ConfigPath) detected.");
                    }

                    config.AddEnvironmentVariables();
                    Console.WriteLine($"[ConfigureAppConfiguration] Added Environment Variables.");
                    config.AddCommandLine(args);
                    Console.WriteLine($"[ConfigureAppConfiguration] Added Command Line Args.");
                })
                .ConfigureLogging(logging =>
                 {
                     logging.ClearProviders();
                     logging.AddConsole();
                     logging.AddDebug();
                 })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddAutoMapper(typeof(MappingProfile));
                    
                    services.Configure<DistributedStorageConfiguration>(
                        hostContext.Configuration.GetSection("DistributedStorage"));
            
                    services.Configure<NodeIdentityOptions>(
                        hostContext.Configuration.GetSection("DistributedStorage:Identity"));
                    services.Configure<NetworkOptions>(
                        hostContext.Configuration.GetSection("DistributedStorage:Network"));
                    services.Configure<StorageOptions>(
                        hostContext.Configuration.GetSection("DistributedStorage:Storage"));
                    services.Configure<DatabaseOptions>(
                        hostContext.Configuration.GetSection("DistributedStorage:Database"));
                    services.Configure<DhtOptions>(
                        hostContext.Configuration.GetSection("DistributedStorage:Dht"));
        
                    services.AddTransient<IConfigurationValidator, ConfigurationValidator>();
                    
                    services.AddHostedService<PeerDiscoveryService>();
                    services.AddSingleton<NodeStatusUpdaterService>();
                    
                    services.AddHealthChecks()
                        .AddCheck("NodeStatus", () =>
                        {
                            IServiceProvider provider = services.BuildServiceProvider();
                            var statusService = provider.GetRequiredService<NodeStatusUpdaterService>();
                            var (isHealthy, status, lastUpdate) = statusService.GetHealthStatus();
        
                            if (!isHealthy)
                            {
                                return HealthCheckResult.Unhealthy(status);
                            }
        
                            if (DateTime.UtcNow - lastUpdate > TimeSpan.FromMinutes(5))
                            {
                                return HealthCheckResult.Degraded($"No status update since {lastUpdate}");
                            }
        
                            return HealthCheckResult.Healthy();
                        });
                    
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<NodeStatusUpdaterService>());
                    services.AddDbContextFactory<NodeDbContext>();

                    services.AddSingleton<IReplicationManager, BackgroundReplicationManager>();
                    services.AddHostedService<ReplicationHealthService>();

                    services.AddSingleton<IFileStorageService, FileStorageService>();
                    services.AddSingleton<INodeStatusService, NodeStatusService>();
                    services.AddSingleton<INodeConfigService, NodeConfigService>();
                    services.AddSingleton<ChunkStreamingHelper>();
                    services.AddSingleton<IDataManager, FileSystemDataManager>();
                    services.AddSingleton<IAsyncInitializable>(sp => sp.GetRequiredService<IDataManager>() as FileSystemDataManager ?? throw new InvalidOperationException("IDataManager is not FileSystemDataManager"));

                    services.AddSingleton<IMetadataManager, SqliteMetadataManager>();
                    services.AddSingleton<IAsyncInitializable>(sp => sp.GetRequiredService<IMetadataManager>() as SqliteMetadataManager ?? throw new InvalidOperationException("IMetadataManager is not SqliteMetadataManager"));

                    services.AddSingleton<INodeClient, GrpcNodeClient>();

                    services.AddGrpc();

                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel((context, options) =>
                    {
                        var nodeOptions = context.Configuration.GetSection("Node").Get<NodeIdentityOptions>();
                        var networkOptionsOptions = context.Configuration.GetSection("Node").Get<NetworkOptions>();
                        var logger = options.ApplicationServices.GetRequiredService<ILogger<Program>>(); // Get logger for Kestrel config

                        logger.LogInformation("[Kestrel] Configuring endpoint. Raw ListenAddress from config: '{ListenAddress}'", networkOptionsOptions?.ListenAddress);

                        if (nodeOptions == null || string.IsNullOrEmpty(networkOptionsOptions.ListenAddress))
                        {
                             logger.LogWarning("[Kestrel] Node:ListenAddress not configured or empty. Using default Kestrel endpoints.");
                             options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
                             return;
                        }
                        try
                        {
                            var parts = networkOptionsOptions.ListenAddress.Split(':');
                            if (parts.Length != 2 || !int.TryParse(parts[1], out int port) || port <= 0 || port > 65535) throw new FormatException($"Invalid ListenAddress format or port: '{networkOptionsOptions.ListenAddress}'. Expected 'host:port' with valid port.");
                            var host = parts[0];
                            IPAddress ipAddress;
                            if (host == "0.0.0.0" || host == "*") ipAddress = IPAddress.Any;
                            else if (host == "[::]" || host.Equals("::", StringComparison.Ordinal)) ipAddress = IPAddress.IPv6Any;
                            else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) ipAddress = IPAddress.Loopback;
                            else if (!IPAddress.TryParse(host, out ipAddress!)) {
                                logger.LogWarning($"[Kestrel] Could not parse '{host}' as IP address. Attempting to listen on IPAddress.Any.");
                                ipAddress = IPAddress.Any;
                            }
                            logger.LogInformation($"[Kestrel] Configuring endpoint to listen on: {ipAddress}:{port} (HTTP/2)");
                            options.Listen(ipAddress, port, listenOptions => { listenOptions.Protocols = HttpProtocols.Http2; });
                        }
                        catch (Exception ex) {
                             logger.LogError(ex, "[Kestrel] Error configuring Kestrel endpoint from ListenAddress '{ListenAddress}'. Using Kestrel defaults.", networkOptionsOptions.ListenAddress);
                             options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
                        }
                    });
                    webBuilder.Configure(app => {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => {
                            endpoints.MapGrpcService<StorageServiceImpl>();
                            endpoints.MapGrpcService<NodeInternalServiceImpl>();
                            endpoints.MapGet("/", async context => {
                                var nodeOpts = context.RequestServices.GetRequiredService<IOptions<NodeIdentityOptions>>().Value;
                                await context.Response.WriteAsync($"VKR Node '{nodeOpts?.NodeId ?? "Unknown"}' running. Status: OK");
                            });
                        });
                    });
                });
    }
}
