using Microsoft.Data.Sqlite;
using VKR_Common.Interfaces;
using VKR_Common.Models;

namespace VKR_Core.Services;

public class ConsensusService
{
    private readonly string _connectionString;
    private readonly INodeManager _nodeManager;
    private string? _currentSuperNode;

    public ConsensusService(string databasePath, INodeManager nodeManager)
    {
        _connectionString = $"Data Source={databasePath}";
        _nodeManager = nodeManager;
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var createStakingTable = @"
            CREATE TABLE IF NOT EXISTS Staking (
                NodeId TEXT PRIMARY KEY,
                Stake REAL NOT NULL,
                LastUpdated TEXT NOT NULL
            )";

        using var command = new SqliteCommand(createStakingTable, connection);
        command.ExecuteNonQuery();
    }

    public async Task ElectSuperNodeAsync()
    {
        var nodes = await _nodeManager.GetAllNodesAsync();
        if (!nodes.Any())
        {
            _currentSuperNode = null;
            return;
        }

        // Fetch staking information from the database
        var stakingData = await GetStakingDataAsync();

        // Find the node with the highest stake
        var eligibleNodes = nodes
            .Where(n => n.IsActive)
            .Select(n => new
            {
                Node = n,
                Stake = stakingData.TryGetValue(n.Id, out var stake) ? stake : 0.0
            })
            .OrderByDescending(n => n.Stake)
            .FirstOrDefault();

        if (eligibleNodes != null)
        {
            _currentSuperNode = eligibleNodes.Node.Id;
        }
    }

    public string? GetCurrentSuperNode()
    {
        return _currentSuperNode;
    }

    public async Task UpdateStakeAsync(string nodeId, double stake)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var query = @"
            INSERT INTO Staking (NodeId, Stake, LastUpdated)
            VALUES (@NodeId, @Stake, @LastUpdated)
            ON CONFLICT(NodeId) DO UPDATE SET
                Stake = @Stake,
                LastUpdated = @LastUpdated";

        using var command = new SqliteCommand(query, connection);
        command.Parameters.AddWithValue("@NodeId", nodeId);
        command.Parameters.AddWithValue("@Stake", stake);
        command.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Dictionary<string, double>> GetStakingDataAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var query = "SELECT NodeId, Stake FROM Staking";

        using var command = new SqliteCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();

        var stakingData = new Dictionary<string, double>();
        while (await reader.ReadAsync())
        {
            stakingData[reader.GetString(0)] = reader.GetDouble(1);
        }

        return stakingData;
    }

    public async Task ValidateConsensusAsync()
    {
        var currentSuperNode = GetCurrentSuperNode();
        if (currentSuperNode == null)
        {
            throw new Exception("No supernode elected.");
        }

        var nodes = await _nodeManager.GetAllNodesAsync();
        foreach (var node in nodes)
        {
            if (!node.IsActive)
            {
                continue;
            }

            // Hypothetical method to communicate with the node
            // Ensure the node agrees on the current supernode
            // await _nodeManager.NotifyNodeOfSuperNodeAsync(node.Id, currentSuperNode);
        }
    }
}
