using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using VKR_Node.Configuration;
using VRK_WPF.MVVM.ViewModel.AdminViewModels;

namespace VRK_WPF.MVVM.Services
{
    public class AdminDatabaseService
    {
        private readonly string _connectionString;

        public AdminDatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<ObservableCollection<object>> GetTableDataAsync(TableViewModel tableInfo)
        {
            var result = new ObservableCollection<object>();
            
            await using var dbContext = CreateDbContext();
            
            try
            {
                switch (tableInfo.TableName)
                {
                    case "Files":
                        var files = await dbContext.FilesMetadata.ToListAsync();
                        foreach (var file in files)
                        {
                            result.Add(new FileRowViewModel
                            {
                                FileId = file.FileId,
                                FileName = file.FileName,
                                FileSize = file.FileSize,
                                CreationTime = file.CreationTime,
                                ModificationTime = file.ModificationTime,
                                ContentType = file.ContentType,
                                ChunkSize = file.ChunkSize,
                                TotalChunks = file.TotalChunks,
                                State = file.State
                            });
                        }
                        break;
                        
                    case "Chunks":
                        var chunks = await dbContext.ChunksMetadata.ToListAsync();
                        foreach (var chunk in chunks)
                        {
                            result.Add(new ChunkRowViewModel
                            {
                                FileId = chunk.FileId,
                                ChunkId = chunk.ChunkId,
                                ChunkIndex = chunk.ChunkIndex,
                                Size = chunk.Size,
                                ChunkHash = chunk.ChunkHash
                            });
                        }
                        break;
                        
                    case "ChunkLocations":
                        var locations = await dbContext.ChunkLocations.ToListAsync();
                        foreach (var location in locations)
                        {
                            result.Add(new ChunkLocationRowViewModel
                            {
                                FileId = location.FileId,
                                ChunkId = location.ChunkId,
                                StoredNodeId = location.StoredNodeId,
                                ReplicationTime = location.ReplicationTime ?? DateTime.MinValue
                            });
                        }
                        break;
                        
                    case "Nodes":
                        var nodes = await dbContext.NodeStates.ToListAsync();
                        foreach (var node in nodes)
                        {
                            result.Add(new NodeRowViewModel
                            {
                                NodeId = node.NodeId,
                                Address = node.Address,
                                State = node.State,
                                LastSeen = node.LastSeen,
                                DiskSpaceAvailableBytes = node.DiskSpaceAvailableBytes,
                                DiskSpaceTotalBytes = node.DiskSpaceTotalBytes,
                                StoredChunkCount = node.StoredChunkCount
                            });
                        }
                        break;
                        
                    case "Users":
                        var users = await dbContext.Users.ToListAsync();
                        foreach (var user in users)
                        {
                            result.Add(new UserRowViewModel
                            {
                                UserId = user.UserId,
                                Username = user.Username,
                                Password = user.PasswordHash,
                                Role = (int)user.Role,
                                IsActive = user.IsActive,
                                CreationTime = user.CreationTime
                            });
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                
                Console.WriteLine($"Error fetching table data: {ex.Message}");
                throw;
            }
            
            return result;
        }

        public async Task<bool> UpdateTableRowsAsync(TableViewModel tableInfo, IEnumerable<object> modifiedRows)
        {
            using var dbContext = CreateDbContext();
            using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                switch (tableInfo.TableName)
                {
                    case "Files":
                        foreach (FileRowViewModel row in modifiedRows)
                        {
                            if (!row.IsModified) continue;
                            
                            var entity = await dbContext.FilesMetadata.FindAsync(row.FileId);
                            if (entity != null)
                            {
                                entity.FileName = row.FileName;
                                entity.FileSize = row.FileSize;
                                entity.ContentType = row.ContentType;
                                entity.ChunkSize = row.ChunkSize;
                                entity.TotalChunks = row.TotalChunks;
                                entity.State = row.State;
                                entity.ModificationTime = DateTime.UtcNow;
                            }
                        }
                        break;
                        
                    case "Users":
                        foreach (UserRowViewModel row in modifiedRows)
                        {
                            if (!row.IsModified) continue;
                            
                            var entity = await dbContext.Users.FindAsync(row.UserId);
                            if (entity != null)
                            {
                                entity.Username = row.Username;
                                entity.PasswordHash = row.Password;
                                entity.Role = (VKR_Core.Enums.UserRole)row.Role;
                                entity.IsActive = row.IsActive;
                            }
                        }
                        break;
                        
                    case "Nodes":
                        foreach (NodeRowViewModel row in modifiedRows)
                        {
                            if (!row.IsModified) continue;
                            
                            var entity = await dbContext.NodeStates.FindAsync(row.NodeId);
                            if (entity != null)
                            {
                                entity.Address = row.Address;
                                entity.State = row.State;
                            }
                        }
                        break;
                }
                
                await dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                
                Console.WriteLine($"Error updating table rows: {ex.Message}");
                throw;
            }
        }
        
        public async Task<bool> DeleteRowAsync(TableViewModel tableInfo, object row)
        {
            using var dbContext = CreateDbContext();
            
            try
            {
                switch (tableInfo.TableName)
                {
                    case "Files":
                        var fileRow = (FileRowViewModel)row;
                        var fileEntity = await dbContext.FilesMetadata.FindAsync(fileRow.FileId);
                        if (fileEntity != null)
                        {
                            dbContext.FilesMetadata.Remove(fileEntity);
                        }
                        break;
                        
                    case "Chunks":
                        var chunkRow = (ChunkRowViewModel)row;
                        var chunkEntity = await dbContext.ChunksMetadata.FindAsync(chunkRow.FileId, chunkRow.ChunkId);
                        if (chunkEntity != null)
                        {
                            dbContext.ChunksMetadata.Remove(chunkEntity);
                        }
                        break;
                        
                    case "ChunkLocations":
                        var locationRow = (ChunkLocationRowViewModel)row;
                        var locationEntity = await dbContext.ChunkLocations.FindAsync(locationRow.FileId, locationRow.ChunkId, locationRow.StoredNodeId);
                        if (locationEntity != null)
                        {
                            dbContext.ChunkLocations.Remove(locationEntity);
                        }
                        break;
                        
                    case "Nodes":
                        var nodeRow = (NodeRowViewModel)row;
                        var nodeEntity = await dbContext.NodeStates.FindAsync(nodeRow.NodeId);
                        if (nodeEntity != null)
                        {
                            dbContext.NodeStates.Remove(nodeEntity);
                        }
                        break;
                        
                    case "Users":
                        var userRow = (UserRowViewModel)row;
                        var userEntity = await dbContext.Users.FindAsync(userRow.UserId);
                        if (userEntity != null)
                        {
                            dbContext.Users.Remove(userEntity);
                        }
                        break;
                }
                
                await dbContext.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                
                Console.WriteLine($"Error deleting row: {ex.Message}");
                throw;
            }
        }
        
        public async Task<bool> BackupDatabaseAsync(string outputPath)
        {
            using var dbContext = CreateDbContext();
            
            try
            {
                
                var connection = dbContext.Database.GetDbConnection() as Microsoft.Data.Sqlite.SqliteConnection;
                if (connection == null)
                {
                    throw new InvalidOperationException("Database connection is not a SQLite connection");
                }

                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                
                using var backupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={outputPath}");
                await backupConnection.OpenAsync();
                
                
                connection.BackupDatabase(backupConnection);
                
                return true;
            }
            catch (Exception ex)
            {
                
                Console.WriteLine($"Error backing up database: {ex.Message}");
                throw;
            }
        }
        
        private VKR_Node.Persistance.NodeDbContext CreateDbContext()
        {
            var optionsBuilder = new DbContextOptionsBuilder<VKR_Node.Persistance.NodeDbContext>();
            optionsBuilder.UseSqlite(_connectionString);
            
            var dbOptions = Microsoft.Extensions.Options.Options.Create(new DatabaseOptions 
            { 
                ConnectionString = _connectionString,
            });
            
            return new VKR_Node.Persistance.NodeDbContext(optionsBuilder.Options, dbOptions);
        }
    }
}