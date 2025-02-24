using VKR_Node.Models;
using VKR_Node.Services;
using VKR_Node.Services.Communication;
using VKR_Node.Utilities;

class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: dotnet run <ip> <port> <bootstrapIp>:<bootstrapPort> (optional)");
                return;
            }

            string ip = args[0];
            int port = int.Parse(args[1]);
            string bootstrapAddress = args.Length > 2 ? args[2] : null;

            var localNode = new Node(ip, port);
            Logger.Info($"Starting Node: {localNode.Identifier} at {ip}:{port}");

            var chordService = new ChordService(localNode);
            var nodeServiceImpl = new NodeServiceImpl(localNode, chordService);

            using var grpcServer = new GRpcNodeServer(nodeServiceImpl, ip, port);
            grpcServer.Start();

            if (!string.IsNullOrEmpty(bootstrapAddress))
            {
                Logger.Info($"Attempting to join network via {bootstrapAddress}");
                var bootstrapParts = bootstrapAddress.Split(':');
                var grpcClient = new GRpcNodeClient(bootstrapParts[0], int.Parse(bootstrapParts[1]));

                try
                {
                    var joinResponse = await grpcClient.SendJoinRequest(localNode.Identifier.ToString(), ip, port);
                    Logger.Info($"Joined network: Successor={joinResponse.SuccessorId}, Predecessor={joinResponse.PredecessorId}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to join network: {ex.Message}");
                }
                finally
                {
                    grpcClient.Dispose();
                }
            }
            else
            {
                Logger.Info("No bootstrap node provided. Starting as the first node in the network.");
                localNode.Successor = localNode;
                localNode.Predecessor = localNode;
            }

            Logger.Info("Node is running. Press Ctrl+C to exit.");
            AppDomain.CurrentDomain.ProcessExit += async (sender, e) => await grpcServer.StopAsync();
            await Task.Delay(-1);
        }
    }