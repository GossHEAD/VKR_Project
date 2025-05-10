using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Node.Configuration;
using VKR_Node.Persistance.Entities;

namespace VKR_Node.Persistance
{

    public class NodeDbContext : DbContext
    {
        private readonly string? _dbPath;
        public DbSet<FileEntity> FilesMetadata { get; set; } = null!;
        public DbSet<ChunkEntity> ChunksMetadata { get; set; } = null!;
        public DbSet<ChunkLocationEntity> ChunkLocations { get; set; } = null!;
        public DbSet<NodeEntity> NodeStates { get; set; } = null!;
        public DbSet<UserEntity> Users { get; set; } = null!;
        
        public NodeDbContext(DbContextOptions<NodeDbContext> options, IOptions<DatabaseOptions> dbOptions) : base(options)
        {
             var relativePath = dbOptions?.Value?.DatabasePath ?? "Data/node4_storage.db"; 
             var basePath = AppContext.BaseDirectory;
             _dbPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
             _ensureDatabaseDirectoryExists(_dbPath); 
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
            var dbOptions = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
                ?.ApplicationServiceProvider
                ?.GetService<IOptions<DatabaseOptions>>();
            
            bool enableLogging = dbOptions?.Value?.EnableSqlLogging ?? false;
            // Configure the database provider (SQLite) and connection string (_dbPath)
            // ONLY if it hasn't already been configured externally (e.g., by AddDbContextFactory).
            if (!optionsBuilder.IsConfigured)
            {
                if (!string.IsNullOrEmpty(_dbPath))
                {
                     var connectionString = $"Data Source={_dbPath}";
                     if (enableLogging)
                     {
                         optionsBuilder.UseSqlite(connectionString)
                             .EnableSensitiveDataLogging()
                             .LogTo(Console.WriteLine, LogLevel.Information);
                         Console.WriteLine($"[NodeDbContext - OnConfiguring] SQL logging is enabled");
                     }
                     else
                     {
                         optionsBuilder.UseSqlite(connectionString);
                         Console.WriteLine($"[NodeDbContext - OnConfiguring] Configuring SQLite with path: {_dbPath}");
                     }
                     Console.WriteLine($"[NodeDbContext - OnConfiguring] (Not externally configured) Configuring SQLite with path: {_dbPath}");
                } else {
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
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<FileEntity>(entity =>
            {
                entity.ToTable("FilesMetadata");
                entity.HasKey(e => e.FileId);
                entity.Property(e => e.FileName).IsRequired();
                entity.Property(e => e.State).IsRequired();
                
                entity.HasMany(e => e.Chunks)
                      .WithOne(c => c.File)
                      .HasForeignKey(c => c.FileId)
                      .IsRequired()
                      .OnDelete(DeleteBehavior.Cascade);
            });

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

            modelBuilder.Entity<ChunkLocationEntity>(entity =>
            {
                entity.ToTable("ChunkLocations");
                entity.HasKey(e => new { e.FileId, e.ChunkId, e.StoredNodeId });
                entity.HasIndex(e => e.StoredNodeId);
            });

            modelBuilder.Entity<NodeEntity>(entity =>
            {
                entity.ToTable("NodeStates");
                entity.HasKey(e => e.NodeId);
                entity.Property(e => e.Address).IsRequired();
                entity.Property(e => e.State).IsRequired();
                
                // No value conversion needed if State is already int in the entity
            });

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

