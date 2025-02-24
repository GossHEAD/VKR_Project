using Grpc.Core;
using VKR_Node.Utilities;

namespace VKR_Node.Services.Communication;

public class GRpcNodeServer : IDisposable
{
    private readonly Server _server;
    private readonly int _idleTimeoutMinutes;
    private DateTime _lastActive;

    public GRpcNodeServer(NodeServiceImpl service, string ip, int port, int idleTimeoutMinutes = 10)
    {
        _server = new Server
        {
            Services = { NodeService.BindService(service) },
            Ports = { new ServerPort(ip, port, ServerCredentials.Insecure) }
        };
        _idleTimeoutMinutes = idleTimeoutMinutes;
        _lastActive = DateTime.UtcNow;
    }

    public void Start()
    {
        try
        {
            _server.Start();
            Logger.Info($"gRPC server started at {_server.Ports.Last()}:{_server.Ports.Last()}");
            MonitorActivity();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to start gRPC server: {ex.Message}");
            throw;
        }
    }
    
    private void MonitorActivity()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));

                if ((DateTime.UtcNow - _lastActive).TotalMinutes >= _idleTimeoutMinutes)
                {
                    Logger.Warning("Server has been idle for too long. Shutting down...");
                    await StopAsync();
                    break;
                }
            }
        });
    }

    public void UpdateLastActive()
    {
        _lastActive = DateTime.UtcNow;
    }

    public async Task StopAsync()
    {
        try
        {
            Logger.Info("Stopping gRPC server...");
            await _server.ShutdownAsync();
            Logger.Info("gRPC server stopped successfully.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to stop gRPC server: {ex.Message}");
            throw;
        }
    }
    public void Dispose()
    {
        Logger.Info("Disposing gRPC server...");
        _server.KillAsync().Wait();
    }
}