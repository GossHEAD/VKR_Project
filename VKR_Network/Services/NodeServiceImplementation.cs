using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Network;
using VKR_Common.Interfaces;
using VKR_Common.Models;

namespace VKR_Network.Services;

/*
public class NodeServiceImplementation : NodeService.NodeServiceBase
{
    private readonly INodeManager _nodeManager;
    private readonly ISuperNodeManager _superNodeManager;
    /*
     * TODO: Внедрить логику репликации
     * TODO: Добавить тесты RemoveNode, SendMessage, Ping
     
    private readonly HashSet<string> _processedNodes = new(); // Учет обработанных узлов
    private readonly HashSet<Guid> _processedSyncRequests = new();
    private const int MaxSyncDepth = 3;
    //private readonly ILogger<NodeServiceImplementation> _logger;
    public NodeServiceImplementation(INodeManager nodeManager)
    {
        _nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
        //_nodeManager.NodeAdded += async node => await SynchronizeNodesAsync(node);
    }
    
    public NodeServiceImplementation(INodeManager nodeManager, ISuperNodeManager superNodeManager)
    {
        _nodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
        _superNodeManager = superNodeManager ?? throw new ArgumentNullException(nameof(superNodeManager));
    
        _ = MonitorSuperNodeAsync();
    }

    
    public override Task<PingResponse> Ping(EmptyRequest request, ServerCallContext context)
    {
        //_logger.LogInformation("Ping request received.");
        return Task.FromResult(new PingResponse { Status = "OK" });
    }
    
    public override Task<MessageResponse> SendMessage(MessageRequest request, ServerCallContext context)
    {
        var message = request.Message.ToStringUtf8();
        if (message.StartsWith("NewSuperNode:"))
        {
            var newSuperNodeId = message.Split(':')[1];
            _superNodeManager.RegisterSuperNodeAsync(new Node
            {
                Id = newSuperNodeId,
                Address = "127.0.0.1", // Замените на актуальный адрес
                Port = 5001           // Замените на актуальный порт
            }).Wait();

            Console.WriteLine($"Updated supernode to: {newSuperNodeId}");
        }

        return Task.FromResult(new MessageResponse { Status = "OK" });
    }

    
    public override async Task<NodeListResponse> GetNodeList(EmptyRequest request, ServerCallContext context)
    {
        try
        {
            Console.WriteLine("Fetching node list...");
            var nodes = await _nodeManager.GetAllNodesAsync();
            Console.WriteLine($"Total nodes found: {nodes.Count}");

            var response = new NodeListResponse();
            response.Nodes.AddRange(nodes.Select(n => new NodeInfo
            {
                Id = n.Id,
                Address = n.Address,
                Port = n.Port
            }));

            Console.WriteLine("Node list response built successfully.");
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetNodeList: {ex.Message}\n{ex.StackTrace}");
            throw new RpcException(new Status(StatusCode.Internal, "Error in GetNodeList"), ex.Message);
        }
    }
    /*
    public override async Task<NodeListResponse> RegisterNode(NodeInfo request, ServerCallContext context)
    {
        try
        {
            var node = new Node(request.Id, request.Address, request.Port);

            if (!await _nodeManager.NodeExistsAsync(node.Id))
            {
                await _nodeManager.AddNodeAsync(node);
                Console.WriteLine($"Node added: {node.Id}");
            }
            else
            {
                Console.WriteLine($"Node already exists: {node.Id}");
            }

            // Отправляем полный список узлов новому узлу
            var nodes = await _nodeManager.GetAllNodesAsync();
            var response = new NodeListResponse();
            response.Nodes.AddRange(nodes.Select(n => new NodeInfo
            {
                Id = n.Id,
                Address = n.Address,
                Port = n.Port
            }));

            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in RegisterNode: {ex.Message}\n{ex.StackTrace}");
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"), ex.Message);
        }
    }

    public override async Task<NodeListResponse> RegisterNode(NodeInfo request, ServerCallContext context)
    {
        var newNode = new Node(request.Id, request.Address, request.Port);

        // Добавляем узел в локальный список
        if (!await _nodeManager.NodeExistsAsync(newNode.Id))
        {
            await _nodeManager.AddNodeAsync(newNode);
            Console.WriteLine($"Node registered: {newNode.Id}");
        }

        // Если текущий узел суперузел, обновляем список
        if (_superNodeManager.IsSuperNode)
        {
            Console.WriteLine("Supernode updating its registry.");
            // Логика обновления списка
        }

        // Возвращаем полный список узлов новому узлу
        var allNodes = await _nodeManager.GetAllNodesAsync();
        var response = new NodeListResponse();
        response.Nodes.AddRange(allNodes.Select(n => new NodeInfo
        {
            Id = n.Id,
            Address = n.Address,
            Port = n.Port
        }));
        return response;
    }

    
    public override async Task<MessageResponse> RemoveNode(RemoveNodeRequest request, ServerCallContext context)
    {
        await _nodeManager.RemoveNodeAsync(request.NodeId);
        Console.WriteLine($"Node removed: {request.NodeId}");
        return new MessageResponse { Status = "TRUE" };
    }
    
    private async Task SynchronizeNodesAsync(Node newNode)
    {
        var nodes = await _nodeManager.GetAllNodesAsync();
        foreach (var node in nodes.Where(n => n.Id != newNode.Id))
        {
            try
            {
                var client = new NetworkClient($"http://{node.Address}:{node.Port}");
                await client.ConnectAsync();

                await client.RegisterNode(new NodeInfo
                {
                    Id = newNode.Id,
                    Address = newNode.Address,
                    Port = newNode.Port
                });

                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error notifying node {node.Id}: {ex.Message}");
            }
        }
    }
    

    //TODO: Внедрить логику репликации
    public override Task<MessageResponse> ReplicateData(NodeInfo request, ServerCallContext context)
    {
        Console.WriteLine($"Replicate data request received from Node {request.Id}");
        return Task.FromResult(new MessageResponse { Status = "TRUE" });
    }
    
    private async Task MonitorSuperNodeAsync()
    {
        while (true)
        {
            if (!await _superNodeManager.IsSuperNodeAliveAsync())
            {
                Console.WriteLine("Supernode is down. Assigning a new supernode...");
                await _superNodeManager.AssignNewSuperNodeAsync();
            }

            await Task.Delay(TimeSpan.FromSeconds(30)); // Проверка каждые 30 секунд
        }
    }
    
    private async Task NotifySuperNodeChangeAsync(string newSuperNodeId)
    {
        var nodes = await _nodeManager.GetAllNodesAsync();
        foreach (var node in nodes.Where(n => n.Id != Environment.MachineName))
        {
            try
            {
                var client = new NetworkClient($"http://{node.Address}:{node.Port}");
                await client.ConnectAsync();

                var message = new MessageRequest
                {
                    SenderId = Environment.MachineName,
                    Message = ByteString.CopyFromUtf8($"NewSuperNode:{newSuperNodeId}")
                };

                await client.SendFileAsync(newSuperNodeId,message.ToByteArray());
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error notifying node {node.Id} about supernode change: {ex.Message}");
            }
        }
    }

    

}
*/