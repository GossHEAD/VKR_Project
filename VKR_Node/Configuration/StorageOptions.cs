using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class StorageOptions : IValidatableConfiguration
    {
        [Required(ErrorMessage = "Base storage path is required")]
        public string BasePath { get; set; } = "ChunkData";
        
        [Range(1024 * 1024, long.MaxValue, ErrorMessage = "Max size must be at least 1MB")]
        public long MaxSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024; // 10GB default
        
        [Range(1024, 100 * 1024 * 1024, ErrorMessage = "Chunk size must be between 1KB and 100MB")]
        public int ChunkSize { get; set; } = 1 * 1024 * 1024; // 1MB default
        
        [Range(1, 10, ErrorMessage = "Replication factor must be between 1 and 10")]
        public int DefaultReplicationFactor { get; set; } = 3;
        
        public bool UseHashBasedDirectories { get; set; } = true;
        
        [Range(1, 3, ErrorMessage = "Hash directory depth must be between 1 and 3")]
        public int HashDirectoryDepth { get; set; } = 2;
        
        public bool PerformIntegrityCheckOnStartup { get; set; } = true;
        
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
            
            if (UseHashBasedDirectories && (HashDirectoryDepth < 1 || HashDirectoryDepth > 3))
            {
                throw new ValidationException("Hash directory depth must be between 1 and 3 when using hash-based directories");
            }
        }
    }
}