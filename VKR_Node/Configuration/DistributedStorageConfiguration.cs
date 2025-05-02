using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Root configuration class for the distributed storage node.
    /// </summary>
    public class DistributedStorageConfiguration
    {
        /// <summary>
        /// Unique information about this node.
        /// </summary>
        public NodeIdentityOptions Identity { get; set; } = new();

        /// <summary>
        /// Network configuration for incoming and outgoing connections.
        /// </summary>
        public NetworkOptions Network { get; set; } = new();

        /// <summary>
        /// Data storage configuration.
        /// </summary>
        public StorageOptions Storage { get; set; } = new();

        /// <summary>
        /// Database configuration for metadata.
        /// </summary>
        public DatabaseOptions Database { get; set; } = new();

        /// <summary>
        /// DHT network configuration.
        /// </summary>
        public DhtOptions Dht { get; set; } = new();

        /// <summary>
        /// Validate the entire configuration.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
            
            // Validate nested objects
            Identity.Validate();
            Network.Validate();
            Storage.Validate();
            Database.Validate();
            Dht.Validate();
            
            // Cross-validate related settings
            ValidateCrossSettings();
        }
        
        /// <summary>
        /// Validate settings that have relationships across different configuration sections.
        /// </summary>
        private void ValidateCrossSettings()
        {
            // Ensure Database path is consistent with Storage path if relative paths are used
            if (!Database.HasExplicitConnectionString && 
                !Path.IsPathRooted(Database.DatabasePath) && 
                !string.IsNullOrEmpty(Storage.BasePath))
            {
                // Ensure Database path doesn't try to navigate outside Storage path
                if (Database.DatabasePath.StartsWith(".."))
                {
                    throw new ValidationException(
                        "Database path cannot navigate outside of storage path when using relative paths");
                }
            }
            
            // Add other cross-validation logic as needed
        }
    }
}