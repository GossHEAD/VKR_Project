using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using VKR_Common.Interfaces;
using VKR_Common.Models;

namespace VKR_Core.Services;

public class NodeManager : INodeManager
{
    private readonly ConcurrentDictionary<string, Node> _nodes = new();
    private readonly string _connectionString;
    private string? _superNodeId;
    
    public event Action<Node>? NodeAdded;
    public event Action<string>? NodeRemoved;
    public event Action<Node>? NodeUpdated;

    public NodeManager(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        LoadNodesFromDatabase();
    }
    
    public async Task AddNodeAsync(Node node)
    {
        if (_nodes.TryAdd(node.Id, node))
        {
            await SaveNodeToDatabaseAsync(node);
            NodeAdded?.Invoke(node);
        }
    }
    public async Task RemoveNodeAsync(string nodeId)
    {
        if (_nodes.TryRemove(nodeId, out var removedNode))
        {
            await DeleteNodeFromDatabaseAsync(nodeId);
            NodeRemoved?.Invoke(nodeId);
        }
    }
    public async Task<List<Node>> GetAllNodesAsync()
    {
        return _nodes.Values.ToList();
    }
    public async Task<List<Node>> GetActiveNodesAsync()
    {
        return await Task.FromResult(_nodes.Values.Where(n => n.IsActive).ToList());
    }
    public async Task<bool> NodeExistsAsync(string nodeId)
    {
        return await Task.FromResult(_nodes.ContainsKey(nodeId));
    }
    public async Task<Node?> GetNodeByIdAsync(string nodeId)
    {
        _nodes.TryGetValue(nodeId, out var node);
        return await Task.FromResult(node);
    }
    public async Task UpdateNodeStatusAsync(string nodeId, float cpu, float memory, float network)
    {
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            node.UpdateStatus(cpu, memory, network);
            NodeUpdated?.Invoke(node);
        }
        await Task.CompletedTask;
    }
    public async Task AssignSuperNodeAsync()
    {
        var eligibleNodes = _nodes.Values.Where(n => n.IsActive).ToList();
        if (eligibleNodes.Any())
        {
            _superNodeId = eligibleNodes.OrderByDescending(n => n.GetNodeUtilization()).First().Id;
        }
    }
    private async Task SaveNodeToDatabaseAsync(Node node)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
            INSERT INTO Nodes (Id, Address, Port, CpuLoad, MemoryUsage, NetworkLoad, IsActive, LastUpdated)
            VALUES (@Id, @Address, @Port, @CpuLoad, @MemoryUsage, @NetworkLoad, @IsActive, @LastUpdated)
            ON CONFLICT(Id) DO UPDATE SET
                CpuLoad = @CpuLoad,
                MemoryUsage = @MemoryUsage,
                NetworkLoad = @NetworkLoad,
                IsActive = @IsActive,
                LastUpdated = @LastUpdated";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("@Id", node.Id);
        command.Parameters.AddWithValue("@Address", node.Address);
        command.Parameters.AddWithValue("@Port", node.Port);
        command.Parameters.AddWithValue("@CpuLoad", node.CpuLoad);
        command.Parameters.AddWithValue("@MemoryUsage", node.MemoryUsage);
        command.Parameters.AddWithValue("@NetworkLoad", node.NetworkLoad);
        command.Parameters.AddWithValue("@IsActive", node.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@LastUpdated", node.LastUpdated.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }
    private async Task DeleteNodeFromDatabaseAsync(string nodeId)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var query = "DELETE FROM Nodes WHERE Id = @Id";
        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("@Id", nodeId);
        await command.ExecuteNonQueryAsync();
    }
    private void LoadNodesFromDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var query = "SELECT * FROM Nodes";
        using var command = new SqliteCommand(query, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var node = new Node
            {
                Id = reader.GetString(0),
                Address = reader.GetString(1),
                Port = reader.GetInt32(2),
                CpuLoad = reader.GetFloat(3),
                MemoryUsage = reader.GetFloat(4),
                NetworkLoad = reader.GetFloat(5),
                IsActive = reader.GetInt32(6) == 1,
                LastUpdated = DateTime.Parse(reader.GetString(7))
            };
            _nodes.TryAdd(node.Id, node);
        }
    }
    public string? GetSuperNodeId()
    {
        return _superNodeId;
    }
}