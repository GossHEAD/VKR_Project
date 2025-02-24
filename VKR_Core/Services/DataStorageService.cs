using Microsoft.Data.Sqlite;
using VKR_Common.Interfaces;
using VKR_Common.Models;

namespace VKR_Core.Services;

public class DataStorageService : IDataStorage
{
    private readonly string _connectionString;

    public DataStorageService(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var createFilesTable = @"
            CREATE TABLE IF NOT EXISTS Files (
                FileId TEXT PRIMARY KEY,
                Data BLOB NOT NULL
            )";

        var createReplicaNodesTable = @"
            CREATE TABLE IF NOT EXISTS ReplicaNodes (
                FileId TEXT NOT NULL,
                NodeId TEXT NOT NULL,
                FOREIGN KEY (FileId) REFERENCES Files(FileId)
            )";

        using var cmd1 = new SqliteCommand(createFilesTable, connection);
        cmd1.ExecuteNonQuery();

        using var cmd2 = new SqliteCommand(createReplicaNodesTable, connection);
        cmd2.ExecuteNonQuery();
    }

    public async Task SaveFileAsync(string fileId, byte[] data)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var insertQuery = @"
            INSERT INTO Files (FileId, Data)
            VALUES (@FileId, @Data)
            ON CONFLICT(FileId) DO UPDATE SET Data = @Data";

        using var command = new SqliteCommand(insertQuery, connection);
        command.Parameters.AddWithValue("@FileId", fileId);
        command.Parameters.AddWithValue("@Data", data);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<byte[]> LoadFileAsync(string fileId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var selectQuery = "SELECT Data FROM Files WHERE FileId = @FileId";

        using var command = new SqliteCommand(selectQuery, connection);
        command.Parameters.AddWithValue("@FileId", fileId);

        var result = await command.ExecuteScalarAsync();
        return result is byte[] data ? data : null;
    }

    public async Task DeleteFileAsync(string fileId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var deleteFileQuery = "DELETE FROM Files WHERE FileId = @FileId";
        using var command1 = new SqliteCommand(deleteFileQuery, connection);
        command1.Parameters.AddWithValue("@FileId", fileId);
        await command1.ExecuteNonQueryAsync();

        var deleteReplicasQuery = "DELETE FROM ReplicaNode WHERE FileId = @FileId";
        using var command2 = new SqliteCommand(deleteReplicasQuery, connection);
        command2.Parameters.AddWithValue("@FileId", fileId);
        await command2.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetReplicaNodesAsync(string fileId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var selectQuery = "SELECT NodeId FROM ReplicaNode WHERE FileId = @FileId";

        using var command = new SqliteCommand(selectQuery, connection);
        command.Parameters.AddWithValue("@FileId", fileId);

        using var reader = await command.ExecuteReaderAsync();
        var nodes = new List<string>();
        while (await reader.ReadAsync())
        {
            nodes.Add(reader.GetString(0));
        }
        return nodes;
    }
    public async Task AddReplicaNodeAsync(string fileId, string nodeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var insertQuery = @"
            INSERT INTO ReplicaNode (FileId, NodeId)
            VALUES (@FileId, @NodeId)
            ON CONFLICT DO NOTHING";

        using var command = new SqliteCommand(insertQuery, connection);
        command.Parameters.AddWithValue("@FileId", fileId);
        command.Parameters.AddWithValue("@NodeId", nodeId);
        await command.ExecuteNonQueryAsync();
    }
    public async Task<List<string>> ListFilesByNodeAsync(string nodeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var selectQuery = "SELECT FileId FROM ReplicaNode WHERE NodeId = @NodeId";

        using var command = new SqliteCommand(selectQuery, connection);
        command.Parameters.AddWithValue("@NodeId", nodeId);

        using var reader = await command.ExecuteReaderAsync();
        var files = new List<string>();
        while (await reader.ReadAsync())
        {
            files.Add(reader.GetString(0));
        }
        return files;
    }
}
