using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class DhtOptions : IValidatableConfiguration
    {
        public string? BootstrapNodeAddress { get; set; }
        
        public int StabilizationIntervalSeconds { get; set; } = 30;
        
        public int ReplicationCheckIntervalSeconds { get; set; } = 60;
        
        public int ReplicationMaxParallelism { get; set; } = 10;
        public int ReplicationFactor { get; set; } = 3;
        public bool AutoJoinNetwork { get; set; } = true;
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
            
            if (AutoJoinNetwork && string.IsNullOrWhiteSpace(BootstrapNodeAddress))
            {
                throw new ValidationException("Bootstrap node address is required when AutoJoinNetwork is enabled");
            }
        }
    }
}