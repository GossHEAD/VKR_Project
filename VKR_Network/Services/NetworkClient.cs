using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Network;
using VKR_Common.Interfaces;

namespace VKR_Network.Services;

/*
public class NetworkClient : INetworkClient
{
    private readonly string _address;
    private GrpcChannel? _channel;
    private NodeService.NodeServiceClient? _client;

    public NetworkClient(string address)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
    }

    public async Task ConnectAsync()
    {
        if (_channel != null)
        {
            throw new InvalidOperationException("Client is already connected.");
        }

        try
        {
            _channel = GrpcChannel.ForAddress(_address);
            _client = new NodeService.NodeServiceClient(_channel);

            // Проверка доступности сервера через gRPC Ping
            Console.WriteLine("Attempting to ping the server...");
            var response = await _client.PingAsync(new EmptyRequest());
            if (response.Status != "OK")
            {
                throw new Exception($"Ping to node at {_address} failed with status: {response.Status}");
            }

            Console.WriteLine("Connected successfully!");
        }
        catch (RpcException rpcEx)
        {
            Console.WriteLine($"gRPC error during connection: {rpcEx.Status.Detail}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error during connection: {ex.Message}");
            _channel = null;
            _client = null;
            throw;
        }
    }



    public async Task DisconnectAsync()
    {
        if (_channel == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        await _channel.ShutdownAsync();
        _channel = null;
        _client = null;
    }

    public async Task<bool> IsConnectedAsync()
    {
        if (_channel == null || _client == null)
        {
            return false;
        }

        try
        {
            var response = await PingAsync();
            return response.Status == "OK";
        }
        catch
        {
            return false;
        }
    }

    public async Task SendFileAsync(string nodeId, byte[] data)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        try
        {
            var request = new MessageRequest
            {
                SenderId = nodeId,
                Message = ByteString.CopyFrom(data)
            };
            await _client.SendMessageAsync(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending file to node {nodeId}: {ex.Message}");
            throw;
        }
    }

    public async Task<byte[]> ReceiveFileAsync(string nodeId)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        try
        {
            var request = new MessageRequest { SenderId = nodeId };
            var response = await _client.SendMessageAsync(request);

            if (response.Status.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return response.ToByteArray();
            }
            else
            {
                Console.WriteLine($"Node {nodeId} returned an unsuccessful status.");
                return Array.Empty<byte>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error receiving file from node {nodeId}: {ex.Message}");
            throw;
        }
    }
    
    public async Task<NodeListResponse> RegisterNode(NodeInfo nodeInfo)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        try
        {
            var response = await _client.RegisterNodeAsync(nodeInfo);
            foreach (var node in response.Nodes)
            {
                Console.WriteLine($"- {node.Id} at {node.Address}:{node.Port}");
            }
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error registering node {nodeInfo.Id}: {ex.Message}");
            throw;
        }
    }


    public async Task<NodeListResponse> GetNodeList()
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        try
        {
            return await _client.GetNodeListAsync(new EmptyRequest());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting node list: {ex.Message}");
            throw;
        }
    }
    
    public async Task<PingResponse> PingAsync()
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        try
        {
            return await _client.PingAsync(new EmptyRequest());
        }
        catch (RpcException rpcEx)
        {
            Console.WriteLine($"gRPC error during ping: {rpcEx.Status.Detail}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error during ping: {ex.Message}");
            throw;
        }
    }
    
    public async Task SendMessageAsync(MessageRequest request)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        try
        {
            var response = await _client.SendMessageAsync(request);
            Console.WriteLine($"Message response: {response.Status}");
        }
        catch (RpcException rpcEx)
        {
            Console.WriteLine($"gRPC error during SendMessage: {rpcEx.Status.Detail}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error during SendMessage: {ex.Message}");
            throw;
        }
    }
    
    public async Task NotifeNodeAsync(string superNodeId)
    {
        if (_client == null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }
        
        var message = new MessageRequest
        {
            SenderId = Environment.MachineName,
            Message = ByteString.CopyFromUtf8($"NewSuperNode:{superNodeId}")
        };
        try
        {
            await SendMessageAsync(message);
        }
        catch (RpcException rpcEx)
        {
            Console.WriteLine($"gRPC error during ping: {rpcEx.Status.Detail}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpected error during ping: {ex.Message}");
            throw;
        }
    }

}
*/