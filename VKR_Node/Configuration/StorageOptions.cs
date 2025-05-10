using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Configuration for data storage.
    /// </summary>
    public class StorageOptions : IValidatableConfiguration
    {
        /// <summary>
        /// Base directory for all storage.
        /// </summary>
        [Required(ErrorMessage = "Base storage path is required")]
        public string BasePath { get; set; } = "ChunkData";
        
        /// <summary>
        /// Maximum storage space to use in bytes.
        /// </summary>
        [Range(1024 * 1024, long.MaxValue, ErrorMessage = "Max size must be at least 1MB")]
        public long MaxSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB default
        
        /// <summary>
        /// Default size of each chunk in bytes.
        /// </summary>
        [Range(1024, 100 * 1024 * 1024, ErrorMessage = "Chunk size must be between 1KB and 100MB")]
        public int ChunkSize { get; set; } = 1 * 1024 * 1024; // 1MB default
        
        /// <summary>
        /// Default number of replicas to maintain for each chunk.
        /// </summary>
        [Range(1, 10, ErrorMessage = "Replication factor must be between 1 and 10")]
        public int DefaultReplicationFactor { get; set; } = 3;
        
        /// <summary>
        /// Whether to use hash-based file organization (subdirectories).
        /// </summary>
        public bool UseHashBasedDirectories { get; set; } = true;
        
        /// <summary>
        /// Depth of hash-based directory structure (1-3).
        /// </summary>
        [Range(1, 3, ErrorMessage = "Hash directory depth must be between 1 and 3")]
        public int HashDirectoryDepth { get; set; } = 2;
        
        /// <summary>
        /// Whether to perform integrity checks on startup.
        /// </summary>
        public bool PerformIntegrityCheckOnStartup { get; set; } = true;
        
        /// <summary>
        /// Validate this configuration section.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
            
            // Additional validations
            if (UseHashBasedDirectories && (HashDirectoryDepth < 1 || HashDirectoryDepth > 3))
            {
                throw new ValidationException("Hash directory depth must be between 1 and 3 when using hash-based directories");
            }
        }
    }
}