using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Extension methods for configuration validation and registration.
    /// </summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Adds and validates a configuration section.
        /// </summary>
        /// <typeparam name="T">The type of the options being configured.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration instance.</param>
        /// <param name="sectionName">The name of the configuration section.</param>
        /// <param name="logger">The logger to use for validation messages.</param>
        /// <returns>The same service collection for chaining.</returns>
        public static IServiceCollection AddValidatedOptions<T>(
            this IServiceCollection services,
            IConfiguration configuration,
            string sectionName,
            ILogger logger) where T : class, new()
        {
            // Configure the options
            services.Configure<T>(configuration.GetSection(sectionName));
            
            // Get the configuration section for validation
            var options = configuration.GetSection(sectionName).Get<T>();
            if (options == null)
            {
                logger.LogWarning("Configuration section {Section} is missing or empty", sectionName);
                return services;
            }
            
            // Validate using data annotations
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
            
            // If the configuration has its own Validate method, call it
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
        
        /// <summary>
        /// Adds cross-validation for configuration sections.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The configuration instance.</param>
        /// <param name="logger">The logger to use for validation messages.</param>
        /// <returns>The same service collection for chaining.</returns>
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
                // Cross-validation between Database and Storage
                if (!rootConfig.Database.HasExplicitConnectionString && 
                    !Path.IsPathRooted(rootConfig.Database.DatabasePath) && 
                    !string.IsNullOrEmpty(rootConfig.Storage.BasePath))
                {
                    // Ensure Database path doesn't try to navigate outside Storage path
                    if (rootConfig.Database.DatabasePath.StartsWith(".."))
                    {
                        throw new ValidationException(
                            "Database path cannot navigate outside of storage path when using relative paths");
                    }
                }
                
                // DHT and Network validation
                if (rootConfig.Dht.AutoJoinNetwork && string.IsNullOrEmpty(rootConfig.Dht.BootstrapNodeAddress))
                {
                    throw new ValidationException("Bootstrap node address is required when AutoJoinNetwork is enabled");
                }
                
                // Validate node identity is configured
                if (string.IsNullOrEmpty(rootConfig.Identity.NodeId))
                {
                    throw new ValidationException("NodeId is required");
                }

                // Additional cross-validations as needed
                
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
    
    /// <summary>
    /// Interface for configuration classes that need custom validation.
    /// </summary>
    public interface IValidatableConfiguration
    {
        /// <summary>
        /// Validates this configuration section.
        /// </summary>
        void Validate();
    }
}