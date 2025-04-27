using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core; // Required for HttpProtocols
using Microsoft.EntityFrameworkCore; // Required for Migrate, IDbContextFactory
using Microsoft.Extensions.Configuration; // Required for ConfigurationBuilder
using Microsoft.Extensions.DependencyInjection; // Required for IServiceCollection, GetRequiredService
using Microsoft.Extensions.Hosting; // Required for IHost, Host
using Microsoft.Extensions.Logging; // Required for ILogger
using Microsoft.Extensions.Options; // Required for IOptions access
using System;
using System.Collections.Generic; // Required for IEnumerable
using System.IO; // Required for Path
using System.Linq; // Required for Linq extensions like Any
using System.Net; // Required for IPAddress
using System.Text.Json; // Required for JsonSerializer
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Persistance;
using VKR_Node.Services; // Required for Task

namespace VKR.Node
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
             // --- Early logging for arguments ---
             Console.WriteLine($"[Main] Application started with args: {string.Join(" ", args)}");

            var host = CreateHostBuilder(args).Build();

            // --- Log Bound Options After Host Build ---
            using (var scope = host.Services.CreateScope())
            {
                 var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                 var nodeOpts = scope.ServiceProvider.GetRequiredService<IOptions<NodeOptions>>().Value;
                 logger.LogInformation("--- Verifying Loaded Configuration ---");
                 try
                 {
                     //var nodeOpts = scope.ServiceProvider.GetRequiredService<IOptions<NodeOptions>>().Value;

                     if (nodeOpts?.KnownNodes != null)
                     {
                         logger.LogInformation("--- Checking KnownNodes Addresses After Binding ---");
                         foreach (var knownNode in nodeOpts.KnownNodes)
                         {
                             // SET BREAKPOINT HERE
                             logger.LogInformation(
                                 "NodeId: {NodeId}, Address: '{Address}' (Is Null or Empty: {IsNull})",
                                 knownNode.NodeId,
                                 knownNode.Address, // Inspect this value
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

                     // Use System.Text.Json for safe serialization (handles nulls)
                     logger.LogInformation("[Config Check] Node.Id: {Value}", nodeOpts?.NodeId ?? "NULL");
                     logger.LogInformation("[Config Check] Node.IpAddress: {Value}", nodeOpts?.Address ?? "NULL");
                     logger.LogInformation("[Config Check] Database.DatabasePath: {Value}", dbOpts?.DatabasePath ?? "NULL");
                     logger.LogInformation("[Config Check] Storage.BasePath: {Value}", storageOpts?.BasePath ?? "NULL");
                     
                     logger.LogInformation("[Config Check] Node.KnownNodes Count: {Value}", nodeOpts?.KnownNodes?.Count ?? 0);

                 }
                 catch (Exception ex)
                 {
                      logger.LogError(ex, "[Config Check] Error resolving or logging options.");
                 }
                 logger.LogInformation("--- End Configuration Verification ---");
            }
             // --- End Log Bound Options ---


            // --- Initialize Services implementing IAsyncInitializable ---
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

            // --- Run the host ---
            var hostLogger = host.Services.GetRequiredService<ILogger<Program>>();
            hostLogger.LogInformation("Starting gRPC Node host...");
            await host.RunAsync();
             hostLogger.LogInformation("gRPC Node host stopped.");
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    // --- Configuration Loading ---
                    Console.WriteLine($"[ConfigureAppConfiguration] Base Path: {hostingContext.HostingEnvironment.ContentRootPath}");
                    Console.WriteLine($"[ConfigureAppConfiguration] Environment: {hostingContext.HostingEnvironment.EnvironmentName}");

                    // Base config files
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

                        // Check for variations like --config=value, --ConfigPath=value
                        if (currentArg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase)) { argName = "--config="; argValue = currentArg.Substring(argName.Length); }
                        else if (currentArg.StartsWith("--ConfigPath=", StringComparison.OrdinalIgnoreCase)) { argName = "--ConfigPath="; argValue = currentArg.Substring(argName.Length); }
                        // Check for variations like --config value, --ConfigPath value
                        else if (currentArg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i+1].StartsWith("--")) { argName = "--config"; argValue = args[i + 1]; i++; /* Skip next arg */ }
                        else if (currentArg.Equals("--ConfigPath", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i+1].StartsWith("--")) { argName = "--ConfigPath"; argValue = args[i + 1]; i++; /* Skip next arg */ }

                        if (!string.IsNullOrEmpty(argName))
                        {
                            configFile = argValue;
                            detectedArg = $"{argName}{(argName.EndsWith("=") ? "" : " ")}{argValue}"; // Reconstruct how it was likely passed
                            Console.WriteLine($"[ConfigureAppConfiguration] Detected config file argument: {detectedArg}");
                            break; // Found it
                        }
                    }

                    // Load Node-Specific Config File
                    if (!string.IsNullOrEmpty(configFile))
                    {
                        // Assume path might be relative to CWD where command was run
                        string configFilePath = Path.GetFullPath(configFile);
                        Console.WriteLine($"[ConfigureAppConfiguration] Attempting to load node-specific config from resolved path: {configFilePath}"); // Log resolved path
                        if (File.Exists(configFilePath))
                        {
                            config.AddJsonFile(configFilePath, optional: false, reloadOnChange: true); // Make it non-optional
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

                    // Environment variables and final command line args (for overrides)
                    config.AddEnvironmentVariables();
                    Console.WriteLine($"[ConfigureAppConfiguration] Added Environment Variables.");
                    config.AddCommandLine(args);
                    Console.WriteLine($"[ConfigureAppConfiguration] Added Command Line Args.");
                    // --- End Configuration Loading ---
                })
                .ConfigureLogging(logging =>
                 {
                     logging.ClearProviders();
                     logging.AddConsole();
                     logging.AddDebug();
                 })
                .ConfigureServices((hostContext, services) =>
                {
                    // Configuration Options Binding
                    services.Configure<NodeOptions>(hostContext.Configuration.GetSection("Node"));
                    services.Configure<StorageOptions>(hostContext.Configuration.GetSection("Storage"));
                    services.Configure<DatabaseOptions>(hostContext.Configuration.GetSection("Database"));
                    services.Configure<DhtOptions>(hostContext.Configuration.GetSection("Dht"));
                    services.AddHostedService<PeerDiscoveryService>();
                    services.AddHostedService<NodeStatusUpdaterService>(); 

                    // Database Context Factory
                    services.AddDbContextFactory<NodeDbContext>();

                    services.AddSingleton<IReplicationManager, BackgroundReplicationManager>();
                    services.AddHostedService<ReplicationHealthService>();
                    // Application Services
                    services.AddSingleton<IDataManager, FileSystemDataManager>();
                    services.AddSingleton<IAsyncInitializable>(sp => sp.GetRequiredService<IDataManager>() as FileSystemDataManager ?? throw new InvalidOperationException("IDataManager is not FileSystemDataManager"));

                    services.AddSingleton<IMetadataManager, SqliteMetadataManager>();
                    services.AddSingleton<IAsyncInitializable>(sp => sp.GetRequiredService<IMetadataManager>() as SqliteMetadataManager ?? throw new InvalidOperationException("IMetadataManager is not SqliteMetadataManager"));

                    services.AddSingleton<INodeClient, GrpcNodeClient>();

                    // gRPC Services
                    services.AddGrpc();

                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel((context, options) =>
                    {
                        // Resolve options *here* to ensure they are bound correctly
                        var nodeOptions = context.Configuration.GetSection("Node").Get<NodeOptions>();
                        var logger = options.ApplicationServices.GetRequiredService<ILogger<Program>>(); // Get logger for Kestrel config

                        logger.LogInformation("[Kestrel] Configuring endpoint. Raw ListenAddress from config: '{ListenAddress}'", nodeOptions?.Address);

                        if (nodeOptions == null || string.IsNullOrEmpty(nodeOptions.Address))
                        {
                             logger.LogWarning("[Kestrel] Node:ListenAddress not configured or empty. Using default Kestrel endpoints.");
                             options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
                             return;
                        }
                        try
                        {
                            var parts = nodeOptions.Address.Split(':');
                            if (parts.Length != 2 || !int.TryParse(parts[1], out int port) || port <= 0 || port > 65535) throw new FormatException($"Invalid ListenAddress format or port: '{nodeOptions.Address}'. Expected 'host:port' with valid port.");
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
                             logger.LogError(ex, "[Kestrel] Error configuring Kestrel endpoint from ListenAddress '{ListenAddress}'. Using Kestrel defaults.", nodeOptions.Address);
                             options.ListenLocalhost(5000, o => o.Protocols = HttpProtocols.Http2);
                        }
                    });
                    webBuilder.Configure(app => {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => {
                            endpoints.MapGrpcService<StorageServiceImpl>();
                            endpoints.MapGrpcService<NodeInternalServiceImpl>();
                            endpoints.MapGet("/", async context => {
                                var nodeOpts = context.RequestServices.GetRequiredService<IOptions<NodeOptions>>().Value;
                                await context.Response.WriteAsync($"VKR Node '{nodeOpts?.NodeId ?? "Unknown"}' running. Status: OK");
                            });
                        });
                    });
                });
    }
}
