using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.File;
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
                logger.LogWarning("Configuration section {Section} is missing or empty", sectionName);
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
                
                if (rootConfig.Dht.AutoJoinNetwork && string.IsNullOrEmpty(rootConfig.Dht.BootstrapNodeAddress))
                {
                    throw new ValidationException("Bootstrap node address is required when AutoJoinNetwork is enabled");
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
            
            // Create a fixed log file path without rolling interval in the name
            string logFilePath = Path.Combine(logsDirectory, $"{nodeId}-log.txt");
            
            // Create date-based filename for historical logs
            string currentDate = DateTime.Now.ToString("yyyyMMdd");
            string dateLogFilePath = Path.Combine(logsDirectory, $"{nodeId}-log-{currentDate}.txt");
            
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("NodeId", nodeId)
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{NodeId}] {Message:lj}{NewLine}{Exception}")
                // Main log file with consistent name
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Infinite,  // Don't add date to main filename
                    retainedFileCountLimit: 1,  // Only keep the most recent version
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                // Date-based historical log
                .WriteTo.File(
                    dateLogFilePath,
                    rollingInterval: RollingInterval.Infinite,  // We've already included the date in the filename
                    retainedFileCountLimit: 31,  // Keep a month of logs
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
                
            Log.Logger = loggerConfig.CreateLogger();
            
            Log.Information("Logging initialized for Node {NodeId}. Log files will be saved to: {LogDirectory}", 
                nodeId, logsDirectory);
        }
        
        // Rest of methods remain the same
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