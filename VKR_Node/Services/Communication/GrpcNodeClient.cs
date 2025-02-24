using VKR_Node.Models;
using Grpc.Net.Client;
using VKR_Node.Utilities;

namespace VKR_Node.Services.Communication;

public class GRpcNodeClient : IDisposable
    {
        private readonly string _baseAddress;
        private GrpcChannel _channel;
        private NodeService.NodeServiceClient _client;
        public GRpcNodeClient(string ip, int port)
        {
            _baseAddress = $"http://{ip}:{port}";
            InitializeClient();
        }
        
        private void InitializeClient()
        {
            _channel = GrpcChannel.ForAddress(_baseAddress);
            _client = new NodeService.NodeServiceClient(_channel);
            Logger.Info($"gRPC client initialized for {_baseAddress}");
        }

        private NodeService.NodeServiceClient CreateClient()
        {
            var channel = GrpcChannel.ForAddress(_baseAddress);
            return new NodeService.NodeServiceClient(channel);
        }

        public async Task<JoinResponse> SendJoinRequest(string nodeId, string ip, int port)
        {
            try
            {
                var request = new JoinRequest
                {
                    NodeId = nodeId,
                    Ip = ip,
                    Port = port
                };

                Logger.Info($"Sending Join request to {_baseAddress}");
                return await _client.JoinAsync(request);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send Join request to {_baseAddress}: {ex.Message}");
                throw;
            }
        }

        public async Task<LookupResponse> SendLookupRequest(string key, string requesterId)
        {
            try
            {
                var request = new LookupRequest
                {
                    Key = key,
                    RequesterId = requesterId
                };

                Logger.Info($"Sending Lookup request for Key: {key} to {_baseAddress}");
                return await _client.LookupAsync(request);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send Lookup request to {_baseAddress}: {ex.Message}");
                throw;
            }
        }

        public async Task<NodeInfo> GetSuccessor()
        {
            try
            {
                var client = CreateClient();
                Logger.Info($"Requesting successor information from {_baseAddress}");
                return await client.GetSuccessorAsync(new EmptyRequest());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get successor from {_baseAddress}: {ex.Message}");
                throw;
            }
        }

        public async Task<NodeInfo> GetPredecessor()
        {
            try
            {
                var client = CreateClient();
                Logger.Info($"Requesting predecessor information from {_baseAddress}");
                return await client.GetPredecessorAsync(new EmptyRequest());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get predecessor from {_baseAddress}: {ex.Message}");
                throw;
            }
        }

        public async Task<PingResponse> PingNode()
        {
            try
            {
                var client = CreateClient();
                Logger.Info($"Pinging {_baseAddress}");
                return await client.PingAsync(new EmptyRequest());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to ping {_baseAddress}: {ex.Message}");
                throw;
            }
        }
        public void Dispose()
        {
            Logger.Info($"Disposing gRPC client for {_baseAddress}");
            _channel?.Dispose();
        }
    }

