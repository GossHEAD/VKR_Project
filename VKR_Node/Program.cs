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
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Mapping;
using VKR_Node.Persistance;
using VKR_Node.Services;
using VKR_Node.Services.FileService;
using VKR_Node.Services.FileService.FileInterface;
using VKR_Node.Services.NodeServices;
using VKR_Node.Services.NodeServices.NodeInterfaces;

namespace VKR_Node
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var host = CreateHostBuilder(args).Build();
                await InitializeAndValidateAsync(host.Services);
                Log.Information("Starting application host...");
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
                Environment.ExitCode = 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    Log.Information("[Config] Base Path: {ContentRootPath}, Environment: {EnvironmentName}", env.ContentRootPath, env.EnvironmentName);

                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                    
                    var builtConfig = config.Build();
                    var configFile = builtConfig.GetValue<string>("config") ?? builtConfig.GetValue<string>("ConfigPath");
                    if (!string.IsNullOrEmpty(configFile))
                    {
                        string fullPath = Path.GetFullPath(configFile);
                        if (File.Exists(fullPath))
                        {
                             Log.Information("[Config] Using node-specific config from command line: {ConfigFile}", fullPath);
                             config.AddJsonFile(fullPath, optional: false, reloadOnChange: true);
                        }
                        else
                        {
                            Log.Warning("[Config] WARNING: Config file specified by command line not found at {ConfigFile}", fullPath);
                        }
                    }
                })
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    var nodeOptions = context.Configuration.GetSection("DistributedStorage:Identity").Get<NodeIdentityOptions>();
                    var nodeId = nodeOptions?.NodeId;

                    string logsDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
                    Directory.CreateDirectory(logsDirectory);
                    string logFilePath = Path.Combine(logsDirectory, $"{nodeId}-log.txt");

                    loggerConfiguration
                        .ReadFrom.Configuration(context.Configuration) 
                        .MinimumLevel.Debug()
                        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
                        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
                        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning) 
                        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Error) 
                        .MinimumLevel.Override("Grpc", Serilog.Events.LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .Enrich.WithProperty("NodeId", nodeId) 
                        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}")
                        .WriteTo.File(
                            logFilePath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 31,
                            fileSizeLimitBytes: 10 * 1024 * 1024,
                            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}");
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddAutoMapper(typeof(MappingProfile));
                    RegisterConfigurationOptions(hostContext, services);
                    RegisterCoreServices(services);
                    RegisterBackgroundServices(services);
                    ConfigureHealthChecks(services);

                    services.AddDbContextFactory<NodeDbContext>((provider, options) =>
                    {
                        var dbOptions = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                        var nodeOptions = provider.GetRequiredService<IOptions<NodeIdentityOptions>>().Value;
                        var connectionString = dbOptions.GetEffectiveConnectionString(nodeOptions.NodeId);

                        var optionsBuilder = options.UseSqlite(connectionString);

                        if (dbOptions.EnableSqlLogging)
                        {
                            optionsBuilder.LogTo(
                                message => { }, 
                                new[] { DbLoggerCategory.Database.Command.Name },
                                LogLevel.Warning); 
                        }
                    });

                    RegisterStorageServices(services);

                    services.AddGrpc(options =>
                    {
                        options.MaxReceiveMessageSize = 100 * 1024 * 1024; 
                        options.MaxSendMessageSize = 100 * 1024 * 1024;    
                    });
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
                                
                                endpoints.MapHealthChecks("/health");
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
        
        
        
        
        private static async Task InitializeAndValidateAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;
            var logger = provider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("--- Application Initialization and Validation ---");
            
            try
            {
                await LogConfigurationDetails(provider, logger);
                await InitializeDatabaseAndServices(provider, logger);
                logger.LogInformation("--- Initialization and Validation Complete ---");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Fatal error during application initialization phase.");
                throw;
            }
        }
        
        private static Task LogConfigurationDetails(IServiceProvider services, ILogger<Program> logger)
        {
            logger.LogInformation("--- Verifying Loaded Configuration ---");
            try
            {
                var nodeOptions = services.GetRequiredService<IOptions<NodeIdentityOptions>>().Value;
                var networkOptions = services.GetRequiredService<IOptions<NetworkOptions>>().Value;

                logger.LogInformation("[Config] Node ID: {NodeId}, Display Name: {DisplayName}", nodeOptions.NodeId, nodeOptions.DisplayName);
                logger.LogInformation("[Config] Network Address: {Address}:{Port}", networkOptions.ListenAddress, networkOptions.ListenPort);
            }
            catch (OptionsValidationException ex)
            {
                 logger.LogCritical(ex, "Configuration validation failed. See validation errors.");
                 throw;
            }
            logger.LogInformation("--- Configuration Verification Complete ---");
            return Task.CompletedTask;
        }

        private static async Task InitializeDatabaseAndServices(IServiceProvider services, ILogger<Program> logger)
        {
            logger.LogInformation("Initializing database and async services...");
            try
            {
                var dbOptions = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                if (dbOptions.AutoMigrate)
                {
                    logger.LogInformation("Applying database migrations...");
                    var dbContextFactory = services.GetRequiredService<IDbContextFactory<NodeDbContext>>();
                    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
                    await dbContext.Database.MigrateAsync();
                    logger.LogInformation("Database migrations applied successfully.");
                }
                
                var initializables = services.GetServices<IAsyncInitializable>();
                if (initializables.Any())
                {
                    logger.LogInformation("Initializing {Count} services implementing IAsyncInitializable.", initializables.Count());
                    foreach (var initializable in initializables)
                    {
                        logger.LogDebug("Initializing service: {ServiceType}", initializable.GetType().Name);
                        await initializable.InitializeAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Failed to initialize database or services.");
                throw;
            }
        }

        private static void RegisterConfigurationOptions(HostBuilderContext hostContext, IServiceCollection services)
        {
            var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<Program>();
            var configuration = hostContext.Configuration;

            services.Configure<DistributedStorageConfiguration>(configuration.GetSection("DistributedStorage"));
            
            services.AddValidatedOptions<NodeIdentityOptions>(configuration, "DistributedStorage:Identity", logger);
            services.AddValidatedOptions<NetworkOptions>(configuration, "DistributedStorage:Network", logger);
            services.AddValidatedOptions<StorageOptions>(configuration, "DistributedStorage:Storage", logger);
            services.AddValidatedOptions<DatabaseOptions>(configuration, "DistributedStorage:Database", logger);
            
            services.AddCrossValidatedConfiguration(configuration, logger);
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.AddSingleton<INodeClient, GrpcNodeClient>();
            services.AddSingleton<INodeStatusService, NodeStatusService>();
            services.AddSingleton<INodeConfigService, NodeConfigService>();

            
            services.AddSingleton<FileSystemDataManager>();
            services.AddSingleton<IDataManager>(sp => sp.GetRequiredService<FileSystemDataManager>());
            services.AddSingleton<IAsyncInitializable>(sp => sp.GetRequiredService<FileSystemDataManager>());

            services.AddSingleton<SqliteMetadataManager>();
            services.AddSingleton<IMetadataManager>(sp => sp.GetRequiredService<SqliteMetadataManager>());
            services.AddSingleton<IAsyncInitializable>(sp => sp.GetRequiredService<SqliteMetadataManager>());

            services.AddSingleton<IReplicationManager, BackgroundReplicationManager>();
        }

        private static void RegisterStorageServices(IServiceCollection services)
        {
            services.AddSingleton<IFileStorageService, FileStorageService>();
            services.AddSingleton<StorageServiceImpl>();
            services.AddSingleton<NodeInternalServiceImpl>();
        }

        private static void RegisterBackgroundServices(IServiceCollection services)
        {
            services.AddHostedService<PeerDiscoveryService>();
            services.AddHostedService<ReplicationHealthService>();
            services.AddSingleton<NodeStatusUpdaterService>();
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<NodeStatusUpdaterService>());
        }

        private static void ConfigureHealthChecks(IServiceCollection services)
        {
            
            services.AddHealthChecks().AddCheck<NodeStatusHealthCheck>("NodeStatus");
            services.AddSingleton<NodeStatusHealthCheck>(); 
        }

        private static void ConfigureKestrelServer(WebHostBuilderContext context, KestrelServerOptions options)
        {
            var logger = options.ApplicationServices.GetRequiredService<ILogger<Program>>();
            var networkOptions = context.Configuration.GetSection("DistributedStorage:Network").Get<NetworkOptions>() 
                                 ?? new NetworkOptions();

            options.Limits.MaxRequestBodySize = 1024 * 1024 * 1024; 
            
            logger.LogInformation("[Kestrel] Configuring endpoint. Address from config: {Address}:{Port}", networkOptions.ListenAddress, networkOptions.ListenPort);

            if (!IPAddress.TryParse(networkOptions.ListenAddress, out var ipAddress))
            {
                
                try
                {
                    var addresses = Dns.GetHostAddresses(networkOptions.ListenAddress);
                    ipAddress = addresses.FirstOrDefault(addr => addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Kestrel] Could not resolve '{Host}'. Defaulting to IPAddress.Any.", networkOptions.ListenAddress);
                    ipAddress = IPAddress.Any;
                }
            }

            if (ipAddress != null)
            {
                logger.LogInformation("[Kestrel] Configuring endpoint to listen on: {IpAddress}:{Port} (HTTP/2)", ipAddress, networkOptions.ListenPort);
                options.Listen(ipAddress, networkOptions.ListenPort, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            }
            else
            {
                logger.LogCritical("[Kestrel] Failed to determine IP address to listen on. Kestrel not configured.");
            }
        }
    }

    
    
    
    
    public class NodeStatusHealthCheck : IHealthCheck
    {
        private readonly NodeStatusUpdaterService _statusService;

        public NodeStatusHealthCheck(NodeStatusUpdaterService statusService)
        {
            _statusService = statusService;
        }

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var (isHealthy, status, lastUpdate) = _statusService.GetHealthStatus();

            var data = new Dictionary<string, object>
            {
                { "status", status },
                { "lastUpdateUtc", lastUpdate.ToString("O") }
            };

            if (!isHealthy)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(status, data: data));
            }

            if (DateTime.UtcNow - lastUpdate > TimeSpan.FromMinutes(5))
            {
                return Task.FromResult(HealthCheckResult.Degraded($"No status update since {lastUpdate:O}", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(status, data));
        }
    }
}