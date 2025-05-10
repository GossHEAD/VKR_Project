using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class DhtOptions : IValidatableConfiguration
    {
        public string? BootstrapNodeAddress { get; set; }
        
        [Range(5, 3600, ErrorMessage = "Stabilization interval must be between 5 and 3600 seconds")]
        public int StabilizationIntervalSeconds { get; set; } = 30;
        
        [Range(5, 3600, ErrorMessage = "Fix fingers interval must be between 5 and 3600 seconds")]
        public int FixFingersIntervalSeconds { get; set; } = 60;
        
        [Range(5, 3600, ErrorMessage = "Check predecessor interval must be between 5 and 3600 seconds")]
        public int CheckPredecessorIntervalSeconds { get; set; } = 45;
        
        [Range(5, 3600, ErrorMessage = "Replication check interval must be between 5 and 3600 seconds")]
        public int ReplicationCheckIntervalSeconds { get; set; } = 60;
        
        [Range(1, 100, ErrorMessage = "Replication parallelism must be between 1 and 100")]
        public int ReplicationMaxParallelism { get; set; } = 10;
        [Range(1, 10, ErrorMessage = "Replication factor must be between 1 and 10")]
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