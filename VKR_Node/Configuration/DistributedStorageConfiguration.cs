using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class DistributedStorageConfiguration : IValidatableConfiguration
    {
        public NodeIdentityOptions Identity { get; set; } = new();
        public NetworkOptions Network { get; set; } = new();
        public StorageOptions Storage { get; set; } = new();
        public DatabaseOptions Database { get; set; } = new();
        public DhtOptions Dht { get; set; } = new();

        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
            
            Identity.Validate();
            Network.Validate();
            Storage.Validate();
            Database.Validate();
            Dht.Validate();
            
            ValidateCrossSettings();
        }
        
        private void ValidateCrossSettings()
        {
            if (!Database.HasExplicitConnectionString && 
                !Path.IsPathRooted(Database.DatabasePath) && 
                !string.IsNullOrEmpty(Storage.BasePath))
            {
                if (Database.DatabasePath.StartsWith(".."))
                {
                    throw new ValidationException(
                        "Database path cannot navigate outside of storage path when using relative paths");
                }
            }
        }
    }
}