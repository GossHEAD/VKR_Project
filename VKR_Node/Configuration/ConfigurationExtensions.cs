using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    
    public interface IValidatableConfiguration
    {
        void Validate();
    }
}