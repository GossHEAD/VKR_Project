using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Configuration for the Distributed Hash Table (DHT).
    /// </summary>
    public class DhtOptions
    {
        /// <summary>
        /// Address of a bootstrap node for joining the network.
        /// </summary>
        public string? BootstrapNodeAddress { get; set; }
        
        /// <summary>
        /// Interval in seconds for stabilizing the DHT.
        /// </summary>
        [Range(5, 3600, ErrorMessage = "Stabilization interval must be between 5 and 3600 seconds")]
        public int StabilizationIntervalSeconds { get; set; } = 30;
        
        /// <summary>
        /// Interval in seconds for fixing finger tables.
        /// </summary>
        [Range(5, 3600, ErrorMessage = "Fix fingers interval must be between 5 and 3600 seconds")]
        public int FixFingersIntervalSeconds { get; set; } = 60;
        
        /// <summary>
        /// Interval in seconds for checking predecessors.
        /// </summary>
        [Range(5, 3600, ErrorMessage = "Check predecessor interval must be between 5 and 3600 seconds")]
        public int CheckPredecessorIntervalSeconds { get; set; } = 45;
        
        /// <summary>
        /// Interval in seconds for checking replication status.
        /// </summary>
        [Range(5, 3600, ErrorMessage = "Replication check interval must be between 5 and 3600 seconds")]
        public int ReplicationCheckIntervalSeconds { get; set; } = 60;
        
        /// <summary>
        /// Maximum number of parallel replication operations.
        /// </summary>
        [Range(1, 100, ErrorMessage = "Replication parallelism must be between 1 and 100")]
        public int ReplicationMaxParallelism { get; set; } = 10;
        
        /// <summary>
        /// Number of replicas to maintain for each chunk.
        /// </summary>
        [Range(1, 10, ErrorMessage = "Replication factor must be between 1 and 10")]
        public int ReplicationFactor { get; set; } = 3;
        
        /// <summary>
        /// Whether to automatically join the network on startup.
        /// </summary>
        public bool AutoJoinNetwork { get; set; } = true;
        
        /// <summary>
        /// Validate this configuration section.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
            
            // Additional validations
            if (AutoJoinNetwork && string.IsNullOrWhiteSpace(BootstrapNodeAddress))
            {
                throw new ValidationException("Bootstrap node address is required when AutoJoinNetwork is enabled");
            }
        }
    }
}