using System;
using System.Threading.Tasks;
using Grpc.Core;
using VKR_Network_lib;
using Network;
using VKR_Network_lib.Services;
using VKR_Common.Interfaces;
using VKR_Core.Services;

class Program
{
    private static NetworkClient? _connectedClient;
    private static ISuperNodeManager _superNodeManager;
    private static INodeManager _nodeManager;

    static async Task Main(string[] args)
    {
        const string Host = "127.0.0.1";
        const int DefaultPort = 5001;

        Console.WriteLine("Enter the port for this node (default: 5001): ");
        var inputPort = Console.ReadLine();
        var port = int.TryParse(inputPort, out var parsedPort) ? parsedPort : DefaultPort;

        _nodeManager = new NodeManager();
        _superNodeManager = new SuperNodeManager(_nodeManager);

        var server = new Server
        {
            Services = { NodeService.BindService(new NodeServiceImplementation(_nodeManager, _superNodeManager)) },
            Ports = { new ServerPort(Host, port, ServerCredentials.Insecure) }
        };

        try
        {
            server.Start();
            Console.WriteLine($"Node server running on {Host}:{port}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Failed to start server on port {port}: {ex.Message}");
            return;
        }

        Console.WriteLine("Waiting for incoming requests...");

        if (await _superNodeManager.IsSuperNodeAliveAsync())
        {
            Console.WriteLine($"Connecting to supernode: {_superNodeManager.SuperNodeId}");
            await ConnectToSuperNode();
        }
        else
        {
            await _superNodeManager.AssignNewSuperNodeAsync();
            if (_superNodeManager.IsSuperNode)
            {
                Console.WriteLine($"This node is now the supernode.");
                await _connectedClient.NotifeNodeAsync("node1");
            }
        }


        bool exit = false;
        while (!exit)
        {
            Console.WriteLine("\nMenu:");
            Console.WriteLine("1. Connect to another node");
            Console.WriteLine("2. Register this node");
            Console.WriteLine("3. Get list of nodes");
            Console.WriteLine("4. Ping connected node");
            Console.WriteLine("5. Show supernode status");
            Console.WriteLine("6. Exit");

            Console.Write("Choose an option: ");
            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    Console.Write("Enter the address of the node to connect (e.g., http://127.0.0.1:5002): ");
                    var  address = Console.ReadLine();
                    await ConnectToNode(address);
                    break;
                case "2":
                    await RegisterNode(Host, port);
                    break;
                case "3":
                    await GetNodeList();
                    break;
                case "4":
                    await TestPing();
                    break;
                case "5":
                    ShowSuperNodeStatus();
                    break;
                case "6":
                    exit = true;
                    break;
                default:
                    Console.WriteLine("Invalid option. Try again.");
                    break;
            }
        }

        Console.WriteLine("Shutting down the server...");
        await server.ShutdownAsync();
        Console.WriteLine("Server stopped.");
    }

    static bool IsClientConnected()
    {
        if (_connectedClient == null)
        {
            Console.WriteLine("No node is connected. Please connect first.");
            return false;
        }
        return true;
    }

    static async Task ConnectToNode(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            Console.WriteLine("Invalid address.");
            return;
        }

        try
        {
            if (_connectedClient != null)
            {
                Console.WriteLine("Disconnecting from the current node...");
                await _connectedClient.DisconnectAsync();
            }

            _connectedClient = new NetworkClient(address);
            await _connectedClient.ConnectAsync();
            Console.WriteLine("Connection successful!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to the node: {ex.Message}");
            _connectedClient = null;
        }
    }
    
    

    static async Task ConnectToSuperNode()
    {
        if (string.IsNullOrWhiteSpace(_superNodeManager.SuperNodeId))
        {
            Console.WriteLine("Supernode is not defined.");
            return;
        }

        Console.WriteLine($"Connecting to supernode {_superNodeManager.SuperNodeId}...");
        var superNodeAddress = $"http://{_superNodeManager.SuperNodeId}";
        await ConnectToNode(superNodeAddress);
    }

    static async Task RegisterNode(string host, int port)
    {
        if (!IsClientConnected())
            return;

        var nodeInfo = new NodeInfo { Id = $"Node-{port}", Address = host, Port = port };
        try
        {
            await _connectedClient!.RegisterNode(nodeInfo);
            Console.WriteLine($"Node registered successfully: {nodeInfo.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error registering node: {ex.Message}");
        }
    }

    static async Task GetNodeList()
    {
        if (!IsClientConnected())
            return;

        try
        {
            var response = await _connectedClient!.GetNodeList();
            if (response.Nodes.Count == 0)
            {
                Console.WriteLine("No registered nodes found.");
                return;
            }

            Console.WriteLine("Registered Nodes:");
            foreach (var node in response.Nodes)
            {
                Console.WriteLine($"- {node.Id} at {node.Address}:{node.Port}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving node list: {ex.Message}");
        }
    }

    static async Task TestPing()
    {
        if (!IsClientConnected())
            return;

        try
        {
            var response = await _connectedClient!.PingAsync();
            Console.WriteLine($"Ping response: {response.Status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pinging node: {ex.Message}");
        }
    }

    static void ShowSuperNodeStatus()
    {
        if (_superNodeManager.IsSuperNode)
        {
            Console.WriteLine("This node is the supernode.");
        }
        else
        {
            Console.WriteLine($"Current supernode: {_superNodeManager.SuperNodeId}");
        }
    }
}
