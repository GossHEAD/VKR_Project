using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;
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
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace VKR_Node
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            ConfigureSerilog(args);
            Console.WriteLine($"[Main] Application started with args: {string.Join(" ", args)}");

            try
            {
                var host = CreateHostBuilder(args).Build();
                await RunWithConfigurationValidation(host, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Main] Fatal error during application startup: {ex.Message}");
                Console.WriteLine(ex.ToString());
                Log.Fatal(ex, "Fatal error during application startup");
                Environment.ExitCode = 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
        
        private static void ConfigureSerilog(string[] args)
        {
            string logsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(logsDirectory); 
        
            string nodeId = "node";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--NodeId", StringComparison.OrdinalIgnoreCase) || 
                    args[i].Equals("--Identity:NodeId", StringComparison.OrdinalIgnoreCase))
                {
                    nodeId = args[i + 1];
                    break;
                }
            }
        
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("NodeId", nodeId)
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(logsDirectory, $"{nodeId}-log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    fileSizeLimitBytes: 10 * 1024 * 1024, 
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        
            Log.Information("Logging initialized. Log files will be saved to: {LogDirectory}", logsDirectory);
        }

        private static async Task RunWithConfigurationValidation(IHost host, string[] args)
        {
            using (var scope = host.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Starting application with configuration validation...");

                try
                {
                    await LogConfigurationDetails(scope.ServiceProvider, logger);

                    await InitializeDatabaseAndServices(scope.ServiceProvider, logger);

                    logger.LogInformation("Starting application host...");
                    await host.RunAsync();
                    logger.LogInformation("Application host stopped.");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Fatal error during application initialization.");
                    throw; 
                }
            }
        }

        private static async Task LogConfigurationDetails(IServiceProvider services, ILogger logger)
        {
            logger.LogInformation("--- Verifying Loaded Configuration ---");

            try
            {
                var nodeOptions = services.GetRequiredService<IOptions<NodeIdentityOptions>>().Value;
                var networkOptions = services.GetRequiredService<IOptions<NetworkOptions>>().Value;
                var dbOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                var storageOptions = services.GetRequiredService<IOptions<StorageOptions>>().Value;
                var dhtOptions = services.GetRequiredService<IOptions<DhtOptions>>().Value;

                logger.LogInformation("[Config] Node ID: {NodeId}", nodeOptions.NodeId ?? "NULL");
                logger.LogInformation("[Config] Display Name: {DisplayName}", 
                    nodeOptions.DisplayName ?? "Not specified");

                logger.LogInformation("[Config] Network Address: {Address}:{Port}", 
                    networkOptions.ListenAddress ?? "NULL", networkOptions.ListenPort);
                logger.LogInformation("[Config] Max Connections: {MaxConn}", 
                    networkOptions.MaxConnections);

                if (networkOptions.KnownNodes != null && networkOptions.KnownNodes.Any())
                {
                    logger.LogInformation("--- Known Nodes ({Count}) ---", 
                        networkOptions.KnownNodes.Count);
                    
                    foreach (var node in networkOptions.KnownNodes)
                    {
                        logger.LogInformation("  - {NodeId}: {Address} (Empty: {IsEmpty})", 
                            node.NodeId, node.Address, string.IsNullOrEmpty(node.Address));
                    }
                }
                else
                {
                    logger.LogWarning("[Config] No known nodes configured!");
                }

                logger.LogInformation("[Config] Database Path: {Path}", 
                    dbOptions.DatabasePath ?? "NULL");
                logger.LogInformation("[Config] Has Explicit Connection String: {HasConnStr}", 
                    dbOptions.HasExplicitConnectionString);
                logger.LogInformation("[Config] Auto Migrate: {AutoMigrate}", 
                    dbOptions.AutoMigrate);

                logger.LogInformation("[Config] Storage Base Path: {Path}", 
                    storageOptions.BasePath ?? "NULL");
                logger.LogInformation("[Config] Chunk Size: {Size} bytes", 
                    storageOptions.ChunkSize);
                logger.LogInformation("[Config] Max Storage Size: {Size} bytes", 
                    storageOptions.MaxSizeBytes);

                logger.LogInformation("[Config] DHT Replication Factor: {Factor}", 
                    dhtOptions.ReplicationFactor);
                logger.LogInformation("[Config] DHT Bootstrap Node: {Node}", 
                    dhtOptions.BootstrapNodeAddress ?? "None");

                var effectiveConnString = dbOptions.GetEffectiveConnectionString(nodeOptions.NodeId);
                logger.LogInformation("[Config] Effective DB Connection String: {ConnStr}", 
                    effectiveConnString);

                var actualBasePath = storageOptions.BasePath?.Replace("{nodeId}", nodeOptions.NodeId);
                logger.LogInformation("[Config] Actual Storage Base Path: {Path}", 
                    actualBasePath ?? "NULL");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating configuration.");
                throw new InvalidOperationException("Configuration validation failed. See the logs for details.", ex);
            }

            logger.LogInformation("--- Configuration Verification Complete ---");
        }

        private static async Task InitializeDatabaseAndServices(IServiceProvider services, ILogger logger)
        {
            logger.LogInformation("Initializing database and services...");

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
                    logger.LogInformation("Initializing {Count} services implementing IAsyncInitializable.",
                        initializables.Count());
                        
                    foreach (var initializable in initializables)
                    {
                        logger.LogDebug("Initializing service: {ServiceType}", 
                            initializable.GetType().Name);
                        await initializable.InitializeAsync();
                        logger.LogDebug("Finished initializing service: {ServiceType}", 
                            initializable.GetType().Name);
                    }
                    
                    logger.LogInformation("All IAsyncInitializable services initialized.");
                }
                else
                {
                    logger.LogInformation("No services implementing IAsyncInitializable found.");
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Failed to initialize database or services.");
                throw; 
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    Console.WriteLine($"[ConfigureAppConfiguration] Base Path: {env.ContentRootPath}");
                    Console.WriteLine($"[ConfigureAppConfiguration] Environment: {env.EnvironmentName}");

                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

                    var configFile = GetConfigFilePath(args);
                    if (!string.IsNullOrEmpty(configFile))
                    {
                        Console.WriteLine($"[Config] Using node-specific config: {configFile}");
                        config.AddJsonFile(configFile, optional: false, reloadOnChange: true);
                    }

                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                })
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddAutoMapper(typeof(MappingProfile));
                    RegisterConfigurationOptions(hostContext, services);
                    RegisterCoreServices(services);
                    RegisterBackgroundServices(services);                    
                    ConfigureHealthChecks(services);
                    
                    services.AddDbContextFactory<NodeDbContext>(options =>
                    {
                        var dbOptions = services.BuildServiceProvider().GetRequiredService<IOptions<DatabaseOptions>>().Value;
    
                        var connectionString = dbOptions.HasExplicitConnectionString
                            ? dbOptions.ConnectionString
                            : $"Data Source={dbOptions.DatabasePath}";
        
                        var optionsBuilder = options.UseSqlite(connectionString);
    
                        if (dbOptions.EnableSqlLogging)
                        {
                            optionsBuilder.EnableSensitiveDataLogging()
                                .LogTo(Console.WriteLine, LogLevel.Information);
                        }
    
                        if (dbOptions.CommandTimeoutSeconds > 0)
                        {
                            var timeout = TimeSpan.FromSeconds(dbOptions.CommandTimeoutSeconds);
                            optionsBuilder.ConfigureLoggingCacheTime(timeout);
                        }
                    });
                    
                    RegisterStorageServices(services);
                    
                    services.AddGrpc();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .ConfigureKestrel(ConfigureKestrelServer)
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapGrpcService<StorageServiceImpl>();
                                endpoints.MapGrpcService<NodeInternalServiceImpl>();
                                endpoints.MapGet("/", async context =>
                                {
                                    var nodeOpts = context.RequestServices
                                        .GetRequiredService<IOptions<NodeIdentityOptions>>().Value;
                                    await context.Response.WriteAsync(
                                        $"VKR Node '{nodeOpts?.NodeId ?? "Unknown"}' running. Status: OK");
                                });
                            });
                        });
                });

        private static string GetConfigFilePath(string[] args)
        {
            string configFile = null;
            
            for (int i = 0; i < args.Length; i++)
            {
                string currentArg = args[i];
                string argValue = null;

                if (currentArg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                {
                    argValue = currentArg.Substring("--config=".Length);
                }
                else if (currentArg.StartsWith("--ConfigPath=", StringComparison.OrdinalIgnoreCase))
                {
                    argValue = currentArg.Substring("--ConfigPath=".Length);
                }
                else if ((currentArg.Equals("--config", StringComparison.OrdinalIgnoreCase) || 
                          currentArg.Equals("--ConfigPath", StringComparison.OrdinalIgnoreCase)) && 
                         i + 1 < args.Length && !args[i+1].StartsWith("--"))
                {
                    argValue = args[i + 1];
                    i++; 
                }

                if (argValue != null)
                {
                    configFile = argValue;
                    break;
                }
            }

            if (string.IsNullOrEmpty(configFile))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(configFile);
            
            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"[Config] WARNING: Config file not found at {fullPath}");
                return null;
            }
            
            return fullPath;
        }

        private static void RegisterConfigurationOptions(HostBuilderContext hostContext, IServiceCollection services)
        {
            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });
            var logger = loggerFactory.CreateLogger<Program>();
            var configuration = hostContext.Configuration;
            
            services.Configure<DistributedStorageConfiguration>(
                hostContext.Configuration.GetSection("DistributedStorage"));
            
            services.AddValidatedOptions<NodeIdentityOptions>(
                configuration, "DistributedStorage:Identity", logger);
                    
            services.AddValidatedOptions<NetworkOptions>(
                configuration, "DistributedStorage:Network", logger);
                    
            services.AddValidatedOptions<StorageOptions>(
                configuration, "DistributedStorage:Storage", logger);
                    
            services.AddValidatedOptions<DatabaseOptions>(
                configuration, "DistributedStorage:Database", logger);
                    
            services.AddValidatedOptions<DhtOptions>(
                configuration, "DistributedStorage:Dht", logger);
                
            services.AddCrossValidatedConfiguration(configuration, logger);
            
            services.AddSingleton(provider => 
                provider.GetRequiredService<IOptions<DhtOptions>>().Value);
                
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.AddSingleton<INodeClient, GrpcNodeClient>();
            services.AddSingleton<INodeStatusService, NodeStatusService>();
            services.AddSingleton<INodeConfigService, NodeConfigService>();
            
            services.AddSingleton<IDataManager, FileSystemDataManager>();
            services.AddSingleton<IMetadataManager, SqliteMetadataManager>();
            services.AddSingleton<IReplicationManager, BackgroundReplicationManager>();
            
            services.AddSingleton<IAsyncInitializable>(sp => 
                sp.GetRequiredService<IDataManager>() as FileSystemDataManager ?? 
                throw new InvalidOperationException("IDataManager is not FileSystemDataManager"));
                
            services.AddSingleton<IAsyncInitializable>(sp => 
                sp.GetRequiredService<IMetadataManager>() as SqliteMetadataManager ?? 
                throw new InvalidOperationException("IMetadataManager is not SqliteMetadataManager"));
        }

        private static void RegisterStorageServices(IServiceCollection services)
        {
            services.AddSingleton<IFileStorageService, FileStorageService>();
            services.AddSingleton<ChunkStreamingHelper>();
            
            services.AddSingleton<StorageServiceImpl>();
            services.AddSingleton<NodeInternalServiceImpl>();
        }

        private static void RegisterBackgroundServices(IServiceCollection services)
        {
            services.AddHostedService<PeerDiscoveryService>();
            services.AddHostedService<ReplicationHealthService>();
            
            services.AddSingleton<NodeStatusUpdaterService>();
            services.AddSingleton<IHostedService>(provider => 
                provider.GetRequiredService<NodeStatusUpdaterService>());
        }

        private static void ConfigureHealthChecks(IServiceCollection services)
        {
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
        }

        private static void ConfigureKestrelServer(WebHostBuilderContext context, KestrelServerOptions options)
        {
            try
            {
                var logger = options.ApplicationServices.GetRequiredService<ILogger<Program>>();
                
                var networkOptions = context.Configuration
                    .GetSection("DistributedStorage:Network")
                    .Get<NetworkOptions>();
                    
                if (networkOptions == null || string.IsNullOrEmpty(networkOptions.ListenAddress))
                {
                    logger.LogWarning("Network configuration missing or invalid. Using default localhost:5000");
                    options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
                    return;
                }
                
                IPAddress ipAddress;
                string host = networkOptions.ListenAddress;
                int port = networkOptions.ListenPort;
                
                logger.LogInformation("[Kestrel] Configuring endpoint. Address from config: {Address}:{Port}", 
                    host, port);
                
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    ipAddress = IPAddress.Loopback;
                }
                else if (host.Equals("0.0.0.0") || host.Equals("*"))
                {
                    ipAddress = IPAddress.Any;
                }
                else if (host.Equals("[::]"))
                {
                    ipAddress = IPAddress.IPv6Any;
                }
                else if (!IPAddress.TryParse(host, out ipAddress))
                {
                    logger.LogWarning("[Kestrel] Could not parse '{Host}' as IP address. Using Any.", host);
                    ipAddress = IPAddress.Any;
                }
                
                logger.LogInformation("[Kestrel] Configuring endpoint to listen on: {IpAddress}:{Port} (HTTP/2)",
                    ipAddress, port);
                    
                options.Listen(ipAddress, port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            }
            catch (Exception ex)
            {
                var logger = options.ApplicationServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "[Kestrel] Error configuring server. Using default configuration.");
                options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
            }
        }
    }
}