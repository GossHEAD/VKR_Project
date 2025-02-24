using System.Text;
using System.Transactions;
using DBreeze.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VKR_Common.Interfaces;
using VKR_Common.Models;
using VKR_Core.Interfaces;

namespace VKR_Core.Services;
public class DataDistributionService
{
    private readonly INodeManager _nodeManager;
    private readonly IDataStorage _dataStorage;
    private readonly int _replicaCount;
    private readonly ILogger<DataDistributionService> _logger;
    /*
     * TODO Ввести общую систему оценки, например, cpuWeight * CpuLoad + memoryWeight * MemoryUsage
     * TODO Все операции нужно обернуть в TransactionScope
     * TODO Сделать количество репликций настраиваемым через DJ
     * TODO Добавить повторные попытки или возвраты при неудачах
     * TODO
     */

    /*
     * Такие операции, как GetAllNodesAsync и последующий OrderBy,
     * не являются потокобезопасными,
     * что приводит к потенциальным несоответствиям.
     * Предлагаемое решение: Используйте механизмы синхронизации или
     * неизменяемые структуры данных для предотвращения условий гонки
     */
    
    /*
     * Многократные вызовы GetAllNodesAsync и LoadFileAsync в циклах
     * могут привести к избыточному доступу к сети/базе данных.
     * Предлагаемое решение: Временно кэшировать результаты
     * этих вызовов во время выполнения мето
     */
    
    /*
     * Использование только загрузки процессора (OrderBy(n => n.CpuLoad))
     * может привести к принятию неоптимальных решений.
     * Рассмотрите возможность сочетания нескольких метрик
     * (например, нагрузка на процессор, память и сеть).
     */
    
    public DataDistributionService(INodeManager nodeManager, IDataStorage? dataStorage,IConfiguration config )
    {
        _nodeManager = nodeManager;
        _dataStorage = dataStorage;
        _replicaCount = config.GetValue<int>("ReplicaCount", 3); 
    }

    public async Task DistributeFileAsync(string fileId, byte[] data)
    {
        try
        {
            _logger.LogInformation($"Distributing file {fileId} to nodes...");
            var bestNodes = await GetBestNodesAsync(n => n.CpuLoad);
            if (!bestNodes.Any())
            {
                _logger.LogWarning("No suitable nodes found for file distribution.");
                return;
            }

            foreach (var node in bestNodes)
            {
                await RetryAsync(async () =>
                {
                    var dataBlock = new DataBlock
                    {
                        Id = fileId,
                        NodeId = node.Id,
                        Data = data,
                        ReplicationNodes = bestNodes.Select(n => n.Id).ToList(),
                        Size = data.Length,
                        CreatedAt = DateTime.UtcNow
                    };

                    var serializedData = dataBlock.ToByteArray(dataBlock); // Protobuf serialization
                    await _dataStorage.SaveFileAsync(fileId, serializedData);
                    _logger.LogInformation($"File {fileId} successfully saved to node {node.Id}");
                }, 3, TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error distributing file {fileId}: {ex.Message}");
            throw;
        }
    }
    public async Task RedistributeDataAsync(string overloadedNodeId)
    {
        try
        {
            _logger.LogInformation($"Redistributing data from overloaded node {overloadedNodeId}...");
            var activeNodes = await _nodeManager.GetAllNodesAsync();
            var overloadedNode = activeNodes.FirstOrDefault(n => n.Id == overloadedNodeId);

            if (overloadedNode == null)
            {
                _logger.LogWarning($"Overloaded node {overloadedNodeId} not found.");
                return;
            }

            var bestNode = activeNodes
                .Where(n => n.Id != overloadedNodeId)
                .OrderBy(n => n.CpuLoad)
                .FirstOrDefault();

            if (bestNode == null)
            {
                _logger.LogWarning("No suitable node found for redistribution.");
                return;
            }

            var filesToMove = await _dataStorage.ListFilesByNodeAsync(overloadedNodeId);

            foreach (var fileId in filesToMove)
            {
                await RetryAsync(async () =>
                {
                    var data = await _dataStorage.LoadFileAsync(fileId);
                    if (data == null) return;

                    var dataBlock = new DataBlock
                    {
                        Id = fileId,
                        NodeId = bestNode.Id,
                        Data = data,
                        ReplicationNodes = new List<string> { bestNode.Id },
                        Size = data.Length,
                        CreatedAt = DateTime.UtcNow
                    };

                    var serializedData = dataBlock.ToByteArray(dataBlock);
                    await _dataStorage.SaveFileAsync(fileId, serializedData);
                    await _dataStorage.DeleteFileAsync(fileId);
                    _logger.LogInformation($"File {fileId} moved from {overloadedNodeId} to {bestNode.Id}");
                }, 3, TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error redistributing data from node {overloadedNodeId}: {ex.Message}");
            throw;
        }
    }
    public async Task AutoReplicateDataAsync()
    {
        try
        {
            _logger.LogInformation("Starting automatic data replication...");
            var activeNodes = await _nodeManager.GetAllNodesAsync();

            if (activeNodes.Count < 2)
            {
                _logger.LogWarning("Not enough active nodes for replication.");
                return;
            }
            List<string> fileIds = new List<string>();
            foreach (var node in activeNodes)
            {
                fileIds.Add(node.Id);
            }
            List<List<string>> allFiles = new ();
            foreach (var node in fileIds)
            {
                allFiles.Add(await _dataStorage.ListFilesByNodeAsync(node));
            }
            //allFiles = await _dataStorage.ListFilesAsync();
            foreach (var fileId in allFiles)
            {
                foreach (var id in fileId)
                {
                    await ReplicateFileAsync(id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during automatic data replication: {ex.Message}");
            throw;
        }
    }
    public async Task ReplicateFileAsync(string fileId)
    {
        try
        {
            _logger.LogInformation($"Replicating file {fileId}...");
            var bestNodes = await GetBestNodesAsync(n => n.MemoryUsage);

            var data = await _dataStorage.LoadFileAsync(fileId);
            if (data == null)
            {
                _logger.LogWarning($"File {fileId} not found for replication.");
                return;
            }

            foreach (var node in bestNodes)
            {
                await RetryAsync(async () =>
                {
                    var dataBlock = new DataBlock
                    {
                        Id = fileId,
                        NodeId = node.Id,
                        Data = data,
                        ReplicationNodes = bestNodes.Select(n => n.Id).ToList(),
                        Size = data.Length,
                        CreatedAt = DateTime.UtcNow
                    };

                    var serializedData = dataBlock.ToByteArray(dataBlock);
                    await _dataStorage.SaveFileAsync(fileId, serializedData);
                    _logger.LogInformation($"File {fileId} replicated to node {node.Id}");
                }, 3, TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error replicating file {fileId}: {ex.Message}");
            throw;
        }
    }
    private async Task<List<Node>> GetBestNodesAsync(Func<Node, double> orderByMetric)
    {
        var nodes = await _nodeManager.GetAllNodesAsync();
        return nodes.OrderBy(orderByMetric).Take(_replicaCount).ToList();
    }
    private async Task RetryAsync(Func<Task> action, int maxRetries, TimeSpan delay)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning($"Attempt {attempt + 1} failed: {ex.Message}. Retrying...");
                await Task.Delay(delay);
            }
        }
        throw new Exception("Operation failed after all retry attempts.");
    }
    public async Task BalanceLoadAsync()
    {
        try
        {
            _logger.LogInformation("Starting load balancing...");
            var activeNodes = await _nodeManager.GetAllNodesAsync();
            if (!activeNodes.Any())
            {
                _logger.LogWarning("No active nodes available for load balancing.");
                return;
            }

            var averageLoad = activeNodes.Average(n => n.CpuLoad);
            foreach (var node in activeNodes)
            {
                if (node.CpuLoad > averageLoad)
                {
                    _logger.LogInformation($"Node {node.Id} is overloaded. Redistributing data...");
                    await RedistributeDataAsync(node.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error during load balancing: {ex.Message}");
            throw;
        }
    }

}
/*
public class DataDistributionService : IDataDistributionService
{
    private readonly INodeManager _nodeManager;
    private readonly INetworkClient _networkClient;
    private readonly ILogger<DataDistributionService> _logger;
    private readonly IDataStorage _dataStorage;
    private readonly ISerializationService _serializationService;

    public DataDistributionService(
        INodeManager nodeManager,
        INetworkClient networkClient,
        ILogger<DataDistributionService> logger,
        IDataStorage dataStorage,
        ISerializationService serializationService)
    {
        _nodeManager = nodeManager;
        _networkClient = networkClient;
        _logger = logger;
        _dataStorage = dataStorage;
        _serializationService = serializationService;
    }
    private async Task<List<Node>> GetBestNodesAsync(int count)
    {
        var activeNodes = await _nodeManager.GetActiveNodesAsync();
        return activeNodes.OrderBy(n => n.CpuLoad).Take(count).ToList();
    }


    public string GetNodeForData(string dataId)
    {
        var hash = dataId.GetHashCode();
        var activeNodes = _nodeManager.GetActiveNodesAsync().Result;
        int nodeIndex = Math.Abs(hash) % activeNodes.Count;
        return activeNodes[nodeIndex].Id;
    }

    public List<string> GetReplicationNodes(string dataId)
    {
        var mainNode = GetNodeForData(dataId);
        var activeNodes = _nodeManager.GetActiveNodesAsync().Result
            .Where(n => n.Id != mainNode)
            .Take(2)
            .Select(n => n.Id)
            .ToList();

        activeNodes.Add(mainNode);
        return activeNodes;
    }

    public async Task DistributeFileAsync(string filePath, byte[] data)
    {
        var activeNodes = await _nodeManager.GetActiveNodesAsync();
        if (activeNodes.Count == 0)
        {
            _logger.LogWarning("No active nodes available for distribution.");
            return;
        }

        var bestNode = activeNodes.OrderBy(n => n.CpuLoad).First();
        var dataBlock = new DataBlock(filePath, bestNode.Id, data);

        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        try
        {
            await _dataStorage.SaveFileAsync(dataBlock); 
            await _networkClient.SendFileAsync(bestNode.Id, Encoding.UTF8.GetBytes(dataBlock.ToCsv())); // Отправляем файл на узел

            transaction.Complete(); 
            _logger.LogInformation($"File distributed to node: {bestNode.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error distributing file: {ex.Message}");
            throw;
        }
    }
    
    public async Task ReplicateFileAsync(string filePath, byte[] data, int replicaCount)
    {
        var bestNodes = await GetBestNodesAsync(replicaCount);
        var replicationNodes = new List<string>();

        //using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        foreach (var node in bestNodes)
        {
            try
            {
                await _networkClient.SendFileAsync(node.Id, Encoding.UTF8.GetBytes(new DataBlock(filePath, node.Id, data).ToCsv()));
                replicationNodes.Add(node.Id);
                _logger.LogInformation($"File replicated to node: {node.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to replicate file to node {node.Id}: {ex.Message}");
            }
        }

        if (replicationNodes.Count > 0)
        {
            var dataBlock = new DataBlock(filePath, bestNodes.First().Id, data)
            {
                ReplicationNodes = replicationNodes
            };
            await _dataStorage.SaveFileAsync(dataBlock);
        }
        else
        {
            _logger.LogError("Replication failed for all nodes.");
            throw new Exception("Replication failed for all nodes.");
        }
    }
    
    public async Task RedistributeDataAsync(string overloadedNodeId)
    {
        var activeNodes = await _nodeManager.GetActiveNodesAsync();
        if (activeNodes.Count == 0)
        {
            _logger.LogWarning("No active nodes available for redistribution.");
            return;
        }

        var overloadedNode = activeNodes.FirstOrDefault(n => n.Id == overloadedNodeId);
        if (overloadedNode == null)
        {
            _logger.LogWarning($"Overloaded node not found: {overloadedNodeId}");
            return;
        }

        var bestNode = activeNodes
            .Where(n => n.Id != overloadedNodeId)
            .OrderBy(n => n.CpuLoad)
            .FirstOrDefault();

        if (bestNode == null)
        {
            _logger.LogWarning("No suitable node found for redistribution.");
            return;
        }

        var filesToMove = await _dataStorage.ListFilesByNodeAsync(overloadedNodeId);

        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        try
        {
            foreach (var file in filesToMove)
            {
                var dataBlock = await _dataStorage.LoadFileAsync(file);
                await _networkClient.SendFileAsync(bestNode.Id, Encoding.UTF8.GetBytes(dataBlock.ToCsv()));
                await _dataStorage.DeleteFileAsync(file); // Удаляем файл с перегруженного узла
                _logger.LogInformation($"File {file} moved to node {bestNode.Id}");
            }

            transaction.Complete(); 
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error redistributing data from node {overloadedNodeId}: {ex.Message}");
            throw;
        }
    }
    
    public async Task AutoReplicateDataAsync()
    {
        var activeNodes = await _nodeManager.GetActiveNodesAsync();
        if (activeNodes.Count < 2)
        {
            _logger.LogWarning("Not enough active nodes for replication.");
            return;
        }

        var changedFiles = await _dataStorage.ListFilesAsync();
        foreach (var file in changedFiles)
        {
            var data = await _dataStorage.LoadFileAsync(file);
            await ReplicateFileAsync(file, data.ToBytes(), 2);
        }
    }

    public async Task BalanceLoadAsync()
    {
        var activeNodes = await _nodeManager.GetActiveNodesAsync();
        if (activeNodes.Count == 0)
        {
            _logger.LogWarning("No active nodes available for load balancing.");
            return;
        }

        var averageLoad = activeNodes.Average(n => n.CpuLoad);
        _logger.LogInformation($"Average CPU load: {averageLoad}%");

        foreach (var node in activeNodes)
        {
            if (node.CpuLoad > averageLoad)
            {
                _logger.LogInformation($"Node {node.Id} is overloaded (CPU Load: {node.CpuLoad}%). Redistributing data...");
            
                using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
                try
                {
                    await RedistributeDataAsync(node.Id);
                    transaction.Complete(); 
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error balancing load for node {node.Id}: {ex.Message}");
                    throw;
                }
            }
        }
    }

}
*/