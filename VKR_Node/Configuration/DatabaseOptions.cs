using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    /// <summary>
    /// Configuration for metadata database.
    /// </summary>
    public class DatabaseOptions
    {
        /// <summary>
        /// Path to the database file.
        /// </summary>
        [Required(ErrorMessage = "Database path is required")]
        public string DatabasePath { get; set; } = "Data/node_storage.db";
        
        /// <summary>
        /// Custom connection string (if specified, takes precedence over DatabasePath).
        /// </summary>
        public string? ConnectionString { get; set; }
        
        /// <summary>
        /// Whether a custom connection string has been specified.
        /// </summary>
        public bool HasExplicitConnectionString => !string.IsNullOrWhiteSpace(ConnectionString);
        
        /// <summary>
        /// Whether to auto-migrate database on startup.
        /// </summary>
        public bool AutoMigrate { get; set; } = true;
        
        /// <summary>
        /// Whether to back up the database before migrations.
        /// </summary>
        public bool BackupBeforeMigration { get; set; } = true;
        
        /// <summary>
        /// Command timeout in seconds.
        /// </summary>
        [Range(30, 3600, ErrorMessage = "Command timeout must be between 30 and 3600 seconds")]
        public int CommandTimeoutSeconds { get; set; } = 60;
        
        /// <summary>
        /// Whether to enable detailed SQL logging.
        /// </summary>
        public bool EnableSqlLogging { get; set; } = false;
        
        /// <summary>
        /// Validate this configuration section.
        /// </summary>
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        }
        
        /// <summary>
        /// Gets the effective connection string.
        /// </summary>
        public string GetEffectiveConnectionString(string nodeId)
        {
            if (HasExplicitConnectionString)
            {
                return ConnectionString!;
            }
            
            var dbPath = DatabasePath;
            if (dbPath.Contains("{nodeId}"))
            {
                dbPath = dbPath.Replace("{nodeId}", nodeId);
            }
            
            return $"Data Source={dbPath}";
        }
    }
}