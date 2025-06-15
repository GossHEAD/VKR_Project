using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class StorageOptions : IValidatableConfiguration
    {
        public string BasePath { get; set; } = "ChunkData";
        public long MaxSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024; 
        public int ChunkSize { get; set; } = 1 * 1024 * 1024;
        public int DefaultReplicationFactor { get; set; } = 3;
        public bool UseHashBasedDirectories { get; set; } = false;
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