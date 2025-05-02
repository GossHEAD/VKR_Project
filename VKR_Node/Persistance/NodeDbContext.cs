using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VKR_Node.Configuration; // Required for DatabaseOptions
using VKR_Node.Persistance.Entities;

namespace VKR_Node.Persistance
{
    /// <summary>
    /// Entity Framework Core DbContext for the node's SQLite database.
    /// </summary>
    public class NodeDbContext : DbContext
    {
        private readonly string? _dbPath;
        public DbSet<FileEntity> FilesMetadata { get; set; } = null!;
        public DbSet<ChunkEntity> ChunksMetadata { get; set; } = null!;
        public DbSet<ChunkLocationEntity> ChunkLocations { get; set; } = null!;
        public DbSet<NodeEntity> NodeStates { get; set; } = null!;
        public DbSet<UserEntity> Users { get; set; } = null!;
        
        /// <summary>
        /// The primary constructor intended for use by IDbContextFactory and potentially design-time tools.
        /// It requires both DbContextOptions and DatabaseOptions (for the path).
        /// </summary>
        /// <param name="options">Core DbContext options (like provider type).</param>
        /// <param name="dbOptions">Options containing the database path.</param>
        public NodeDbContext(DbContextOptions<NodeDbContext> options, IOptions<DatabaseOptions> dbOptions) : base(options)
        {
             var relativePath = dbOptions?.Value?.DatabasePath ?? "Data/node4_storage.db"; // Use fallback if options missing
             var basePath = AppContext.BaseDirectory;
             _dbPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
             _ensureDatabaseDirectoryExists(_dbPath); 
             //Console.WriteLine($"[NodeDbContext] Instance created. Using database path: {_dbPath}");
        }

         private void _ensureDatabaseDirectoryExists(string? dbPath)
         {
             if (!string.IsNullOrEmpty(dbPath))
             {
                 var directory = Path.GetDirectoryName(dbPath);
                 if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                 {
                     try {
                         Directory.CreateDirectory(directory);
                         Console.WriteLine($"[NodeDbContext] Created database directory: {directory}");
                     } catch (Exception ex) {
                         Console.WriteLine($"[NodeDbContext] Error creating database directory '{directory}': {ex.Message}");
                     }
                 }
             }
         }


        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Configure the database provider (SQLite) and connection string (_dbPath)
            // ONLY if it hasn't already been configured externally (e.g., by AddDbContextFactory).
            if (!optionsBuilder.IsConfigured)
            {
                if (!string.IsNullOrEmpty(_dbPath))
                {
                     optionsBuilder.UseSqlite($"Data Source={_dbPath}");
                     Console.WriteLine($"[NodeDbContext - OnConfiguring] (Not externally configured) Configuring SQLite with path: {_dbPath}");
                } else {
                     // This case should ideally not happen if the constructor logic is sound
                     var fallbackPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Data/fallback_onconfiguring.db"));
                     _ensureDatabaseDirectoryExists(fallbackPath);
                     Console.WriteLine($"[NodeDbContext - OnConfiguring] Warning: DB path not set and options not configured externally. Using fallback: {fallbackPath}");
                     optionsBuilder.UseSqlite($"Data Source={fallbackPath}");
                }
            } else {
                 Console.WriteLine($"[NodeDbContext - OnConfiguring] Options already configured externally.");
            }
            base.OnConfiguring(optionsBuilder);
        }


        /// <summary>
        /// Configures the database model using Fluent API.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- FileEntity Configuration ---
            modelBuilder.Entity<FileEntity>(entity =>
            {
                entity.ToTable("FilesMetadata");
                entity.HasKey(e => e.FileId);
                entity.Property(e => e.FileName).IsRequired();
                entity.Property(e => e.State).IsRequired();
                
                // No value conversion needed if State is already int in the entity
                
                entity.HasMany(e => e.Chunks)
                      .WithOne(c => c.File)
                      .HasForeignKey(c => c.FileId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // --- ChunkEntity Configuration ---
            modelBuilder.Entity<ChunkEntity>(entity =>
            {
                entity.ToTable("ChunksMetadata");
                entity.HasKey(e => new { e.FileId, e.ChunkId });
                entity.HasMany(e => e.Locations)
                      .WithOne(l => l.ChunkEntity)
                      .HasForeignKey(l => new { l.FileId, l.ChunkId })
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // --- ChunkLocationEntity Configuration ---
            modelBuilder.Entity<ChunkLocationEntity>(entity =>
            {
                entity.ToTable("ChunkLocations");
                entity.HasKey(e => new { e.FileId, e.ChunkId, e.StoredNodeId });
                entity.HasIndex(e => e.StoredNodeId);
            });

            // --- NodeEntity Configuration ---
            modelBuilder.Entity<NodeEntity>(entity =>
            {
                entity.ToTable("NodeStates");
                entity.HasKey(e => e.NodeId);
                entity.Property(e => e.Address).IsRequired();
                entity.Property(e => e.State).IsRequired();
                
                // No value conversion needed if State is already int in the entity
            });

            // --- UserEntity Configuration ---
            modelBuilder.Entity<UserEntity>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.UserId);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.CreationTime).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.HasIndex(e => e.Username).IsUnique();
            });
        }
    }
}

