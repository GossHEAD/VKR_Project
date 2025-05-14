using System.ComponentModel.DataAnnotations;

namespace VKR_Node.Configuration
{
    public class DatabaseOptions : IValidatableConfiguration
    {
        [Required(ErrorMessage = "Database path is required")]
        public string DatabasePath { get; set; } = "Data/node_storage.db";
        
        public string? ConnectionString { get; set; }
        
        public bool HasExplicitConnectionString
        {
            get => !string.IsNullOrWhiteSpace(ConnectionString);
            set
            {
                var hasExplicitConnectionString = HasExplicitConnectionString;
            }
        }

        public bool AutoMigrate { get; set; } = true;
        
        public bool BackupBeforeMigration { get; set; } = true;
        
        [Range(30, 3600, ErrorMessage = "Command timeout must be between 30 and 3600 seconds")]
        public int CommandTimeoutSeconds { get; set; } = 60;
        public bool EnableSqlLogging { get; set; } = false;
        
        public void Validate()
        {
            var context = new ValidationContext(this);
            Validator.ValidateObject(this, context, true);
        }
        
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