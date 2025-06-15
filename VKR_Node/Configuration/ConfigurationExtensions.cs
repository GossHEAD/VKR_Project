using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace VKR_Node.Configuration
{
    public static class ConfigurationExtensions
    {
        public static IServiceCollection AddValidatedOptions<T>(
            this IServiceCollection services,
            IConfiguration configuration,
            string sectionName,
            ILogger logger) where T : class, new()
        {
            services.Configure<T>(configuration.GetSection(sectionName));
            
            var options = configuration.GetSection(sectionName).Get<T>();
            if (options == null)
            {
                return services;
            }
            
            var validationContext = new ValidationContext(options);
            var validationResults = new List<ValidationResult>();
            
            if (!Validator.TryValidateObject(options, validationContext, validationResults, true))
            {
                foreach (var error in validationResults)
                {
                    logger.LogError("Configuration validation error in {Section}: {ErrorMessage} (Members: {Members})",
                        sectionName, error.ErrorMessage, string.Join(", ", error.MemberNames));
                }
                
                throw new InvalidOperationException($"Configuration validation failed for section {sectionName}");
            }
            
            if (options is IValidatableConfiguration validatable)
            {
                try
                {
                    validatable.Validate();
                }
                catch (ValidationException ex)
                {
                    logger.LogError("Custom validation error in {Section}: {ErrorMessage}",
                        sectionName, ex.Message);
                    throw new InvalidOperationException($"Custom validation failed for section {sectionName}: {ex.Message}");
                }
            }
            
            logger.LogInformation("Configuration section {Section} validated successfully", sectionName);
            return services;
        }
        
        public static IServiceCollection AddCrossValidatedConfiguration(
            this IServiceCollection services,
            IConfiguration configuration,
            ILogger logger)
        {
            var rootConfig = configuration.GetSection("DistributedStorage").Get<DistributedStorageConfiguration>();
            if (rootConfig == null)
            {
                logger.LogWarning("Root configuration section DistributedStorage is missing");
                return services;
            }
            
            try
            {
                if (!rootConfig.Database.HasExplicitConnectionString && 
                    !Path.IsPathRooted(rootConfig.Database.DatabasePath) && 
                    !string.IsNullOrEmpty(rootConfig.Storage.BasePath))
                {
                    if (rootConfig.Database.DatabasePath.StartsWith(".."))
                    {
                        throw new ValidationException(
                            "Database path cannot navigate outside of storage path when using relative paths");
                    }
                }
                if (string.IsNullOrEmpty(rootConfig.Identity.NodeId))
                {
                    throw new ValidationException("NodeId is required");
                }
                
                logger.LogInformation("Cross-validation of configuration sections completed successfully");
            }
            catch (ValidationException ex)
            {
                logger.LogError("Cross-validation error: {ErrorMessage}", ex.Message);
                throw new InvalidOperationException($"Cross-validation failed: {ex.Message}");
            }
            
            return services;
        }
    }
    
    public static class LoggingConfiguration
    {
        public static void ConfigureLogging(string[] args, IConfiguration configuration)
        {
            string logsDirectory = GetLogsDirectory();
            Directory.CreateDirectory(logsDirectory);
            
            string nodeId = ExtractNodeIdFromArgs(args, configuration);
            
            string logFilePath = Path.Combine(logsDirectory, $"{nodeId}-log.txt");
            
            string currentDate = DateTime.Now.ToString("yyyyMMdd");
            string dateLogFilePath = Path.Combine(logsDirectory, $"{nodeId}-log-{currentDate}.txt");
            
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Information() 
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning) 
                .MinimumLevel.Override("Grpc", LogEventLevel.Warning) 
                .MinimumLevel.Override("VRK_WPF.MVVM.Services.LogManager", LogEventLevel.Warning) 
                .Enrich.FromLogContext()
                .Enrich.WithProperty("NodeId", nodeId)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information) 
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Infinite,  
                    retainedFileCountLimit: 1,  
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug) 
                .WriteTo.File(
                    dateLogFilePath,
                    rollingInterval: RollingInterval.Infinite,  
                    retainedFileCountLimit: 31,  
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information); 
            
            
            loggerConfig = loggerConfig.Filter.ByExcluding(logEvent => 
            {
                
                if (logEvent.MessageTemplate.Text.Contains("Ping request") ||
                    logEvent.MessageTemplate.Text.Contains("Pinging node") ||
                    logEvent.MessageTemplate.Text.Contains("Node {NodeId} ({Address}) - {Status}"))
                {
                    return logEvent.Level < LogEventLevel.Warning; 
                }
                
                
                if (logEvent.MessageTemplate.Text.Contains("Chunk {ChunkId}: Desired=") ||
                    logEvent.MessageTemplate.Text.Contains("has sufficient online replicas"))
                {
                    return logEvent.Level < LogEventLevel.Information;
                }
                
                
                if (logEvent.Properties.ContainsKey("SourceContext") &&
                    logEvent.Properties["SourceContext"].ToString().Contains("EntityFrameworkCore"))
                {
                    return logEvent.Level < LogEventLevel.Warning;
                }
                
                return false;
            });
                
            Log.Logger = loggerConfig.CreateLogger();
            
            Log.Information("Logging initialized for Node {NodeId}. Log files: {LogDirectory}", 
                nodeId, logsDirectory);
        }
        
        private static string GetLogsDirectory()
        {
            string baseDir = AppContext.BaseDirectory;
            string logsDirectory = Path.Combine(baseDir, "Logs");
            
            return logsDirectory;
        }
        
        private static string ExtractNodeIdFromArgs(string[] args, IConfiguration configuration)
        {
            string nodeId = "unknown-node";
            
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--NodeId", StringComparison.OrdinalIgnoreCase) || 
                    args[i].Equals("--Identity:NodeId", StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals("--DistributedStorage:Identity:NodeId", StringComparison.OrdinalIgnoreCase))
                {
                    nodeId = args[i + 1];
                    return nodeId;
                }
                
                if (args[i].StartsWith("--NodeId=", StringComparison.OrdinalIgnoreCase) ||
                    args[i].StartsWith("--Identity:NodeId=", StringComparison.OrdinalIgnoreCase) ||
                    args[i].StartsWith("--DistributedStorage:Identity:NodeId=", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = args[i].Split('=', 2);
                    if (parts.Length == 2 && !string.IsNullOrEmpty(parts[1]))
                    {
                        nodeId = parts[1];
                        return nodeId;
                    }
                }
            }
            
            if (configuration != null)
            {
                string? configNodeId = configuration["DistributedStorage:Identity:NodeId"];
                if (!string.IsNullOrEmpty(configNodeId))
                {
                    nodeId = configNodeId;
                    return nodeId;
                }
            }
            
            if (nodeId == "unknown-node")
            {
                nodeId = $"node-{Guid.NewGuid().ToString()[..8]}";
            }
            
            return nodeId;
        }
        
        public static void UpdateNodeIdInLogger(string nodeId)
        {
            Log.ForContext("NodeId", nodeId);
            
            Log.Information("Logger NodeId updated to {NodeId}", nodeId);
        }
    }
    
    public interface IValidatableConfiguration
    {
        void Validate();
    }
}