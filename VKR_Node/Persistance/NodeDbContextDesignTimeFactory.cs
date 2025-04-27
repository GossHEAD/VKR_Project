using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using VKR_Node.Configuration;

namespace VKR_Node.Persistance;

/// <summary>
/// Фабрика для создания NodeDbContext во время работы инструментов EF Core (например, для миграций).
/// Использует простую строку подключения, не зависящую от конфигурации конкретного узла.
/// </summary>


public class NodeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NodeDbContext>
    {
        /// <summary>
        /// Creates a new instance of NodeDbContext for design-time purposes.
        /// </summary>
        /// <param name="args">Arguments passed by design-time tools (not typically used here).</param>
        /// <returns>A new NodeDbContext instance.</returns>
        public NodeDbContext CreateDbContext(string[] args)
        {
            // --- Configuration Setup (Minimal for Design Time) ---

            string projectDirectory = Path.Combine(Directory.GetCurrentDirectory(), "VKR_Node"); // Adjust if VKR_Node is nested differently
            if (!Directory.Exists(projectDirectory))
            {
                 // Fallback if not found relative to CWD (e.g., if CWD is already VKR_Node)
                 projectDirectory = Directory.GetCurrentDirectory();
                 // Add more sophisticated searching logic if needed (e.g., searching upwards for .csproj)
            }

            Console.WriteLine($"[DesignTimeFactory] Using base path for configuration: {projectDirectory}");

            if (!File.Exists(Path.Combine(projectDirectory, "appsettings.json")))
            {
                 Console.WriteLine($"[DesignTimeFactory] Warning: appsettings.json not found in detected base path: {projectDirectory}. Configuration might be incomplete.");
                 // Attempt to find it relative to AppContext.BaseDirectory as a last resort? Risky.
            }


            var configuration = new ConfigurationBuilder()
                .SetBasePath(projectDirectory) // Use the determined project directory path
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .AddCommandLine(args)
                .Build();

            // Get DatabaseOptions from configuration
            var databaseOptions = new DatabaseOptions();
            configuration.GetSection("Database").Bind(databaseOptions);

            if (string.IsNullOrWhiteSpace(databaseOptions.DatabasePath))
            {
                 databaseOptions.DatabasePath = "Data/design_time_migrations.db"; // Default for migrations
                 Console.WriteLine($"[DesignTimeFactory] Warning: Database path not found in config, using default: {databaseOptions.DatabasePath}");
            }

            var optionsWrapper = Options.Create(databaseOptions);

            // --- DbContextOptions Setup ---
            var optionsBuilder = new DbContextOptionsBuilder<NodeDbContext>();

            // Construct the full DB path relative to the project directory
            var dbPath = Path.GetFullPath(Path.Combine(projectDirectory, databaseOptions.DatabasePath));
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory); // Ensure directory exists
                    Console.WriteLine($"[DesignTimeFactory] Created database directory: {directory}");
                }
                catch (Exception ex)
                {
                     // Throw a more informative exception if directory creation fails
                     throw new InvalidOperationException($"[DesignTimeFactory] Failed to create database directory '{directory}'. Error: {ex.Message}", ex);
                }
            }

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
            Console.WriteLine($"[DesignTimeFactory] Configuring DbContext with SQLite path: {dbPath}");

            // --- Create DbContext Instance ---
            try
            {
                // Call the constructor that now requires both DbContextOptions and IOptions<DatabaseOptions>
                return new NodeDbContext(optionsBuilder.Options, optionsWrapper);
            }
            catch (Exception ex)
            {
                // Catch exceptions during DbContext creation and provide more context
                Console.WriteLine($"[DesignTimeFactory] Error creating NodeDbContext instance: {ex}");
                throw new InvalidOperationException($"[DesignTimeFactory] Failed to create NodeDbContext instance. Check constructor and path '{dbPath}'. Inner Exception: {ex.Message}", ex);
            }
        }
    }