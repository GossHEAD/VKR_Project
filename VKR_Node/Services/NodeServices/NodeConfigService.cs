using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VKR_Core.Services;
using VKR_Node.Configuration;
using VKR_Node.Services.NodeServices.NodeInterfaces;
using VKR.Protos;

namespace VKR_Node.Services.NodeServices
{
    // NodeConfigService.cs
public class NodeConfigService : INodeConfigService
{
    private readonly ILogger<NodeConfigService> _logger;
    private readonly NodeIdentityOptions _nodeIdentityOptions;
    private readonly NetworkOptions _networkOptions;
    private readonly StorageOptions _storageOptions;
    private readonly DhtOptions _dhtOptions;
    private readonly IDataManager _dataManager; 

    public NodeConfigService(
        ILogger<NodeConfigService> logger,
        IOptions<NodeIdentityOptions> nodeIdentityOptions,
        IOptions<NetworkOptions> networkOptions,
        IOptions<StorageOptions> storageOptions,
        IOptions<DhtOptions> dhtOptions,
        IDataManager dataManager)
    {
        _logger = logger;
        _nodeIdentityOptions = nodeIdentityOptions.Value;
        _networkOptions = networkOptions.Value;
        _storageOptions = storageOptions.Value;
        _dhtOptions = dhtOptions.Value;
        _dataManager = dataManager;
    }

    public async Task<GetNodeConfigurationReply> GetNodeConfiguration(
    GetNodeConfigurationRequest request, 
    ServerCallContext context)
    {
        _logger.LogInformation("GetNodeConfiguration request received from {Peer}", context.Peer);
        
        try
        {
            var cpuUsage = await GetCpuUsageAsync();
            var (memoryUsed, memoryTotal) = await GetMemoryInfoAsync();
            var (diskAvailable, diskTotal) = await GetDiskSpaceInfoAsync();
            
            var reply = new GetNodeConfigurationReply
            {
                NodeId = _nodeIdentityOptions.NodeId ?? "N/A",
                ListenAddress = _networkOptions.ListenAddress ?? "N/A",
                StorageBasePath = _storageOptions.BasePath ?? "N/A",
                ReplicationFactor = _dhtOptions.ReplicationFactor,
                DefaultChunkSize = _storageOptions.ChunkSize,
                CpuUsagePercent = cpuUsage,
                MemoryUsedBytes = memoryUsed,
                MemoryTotalBytes = memoryTotal,
                DiskSpaceAvailableBytes = diskAvailable,
                DiskSpaceTotalBytes = diskTotal,
                Success = true
            };
            
            _logger.LogInformation(
                "Returning Node Config: Id={Id}, Addr={Addr}, Storage={Storage}, RepFactor={RepFactor}, ChunkSize={ChunkSize}, CPU={Cpu}%, Memory={MemUsed}/{MemTotal}",
                reply.NodeId, reply.ListenAddress, reply.StorageBasePath, 
                reply.ReplicationFactor, reply.DefaultChunkSize,
                reply.CpuUsagePercent, FormatBytes(reply.MemoryUsedBytes), FormatBytes(reply.MemoryTotalBytes));
                
            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving node configuration");
            return new GetNodeConfigurationReply
            {
                Success = false,
                ErrorMessage = $"Internal error: {ex.Message}"
            };
        }
    }

    private async Task<double> GetCpuUsageAsync()
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var startCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            
            await Task.Delay(500);
            
            var endTime = DateTime.UtcNow;
            var endCpuUsage = Process.GetCurrentProcess().TotalProcessorTime;
            
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalPeriodMs = (endTime - startTime).TotalMilliseconds * Environment.ProcessorCount;
            
            return Math.Round(cpuUsedMs / totalPeriodMs * 100, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error measuring CPU usage");
            return -1;
        }
    }

    private Task<(long Used, long Total)> GetMemoryInfoAsync()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var memoryUsed = process.WorkingSet64;
            
            long totalMemory;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memoryStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memoryStatus))
                {
                    totalMemory = (long)memoryStatus.ullTotalPhys;
                }
                else
                {
                    totalMemory = -1;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var memInfo = File.ReadAllText("/proc/meminfo");
                var match = Regex.Match(memInfo, @"MemTotal:\s+(\d+) kB");
                if (match.Success && long.TryParse(match.Groups[1].Value, out var kbValue))
                {
                    totalMemory = kbValue * 1024; // Convert KB to bytes
                }
                else
                {
                    totalMemory = -1;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                totalMemory = -1;
            }
            else
            {
                totalMemory = -1;
            }
            
            return Task.FromResult((memoryUsed, totalMemory));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting memory information");
            return Task.FromResult((-1L, -1L));
        }
    }

    private async Task<(long Available, long Total)> GetDiskSpaceInfoAsync()
    {
        try
        {
            var available = await _dataManager.GetFreeDiskSpaceAsync();
            var total = await _dataManager.GetTotalDiskSpaceAsync();
            return (available, total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting disk space information");
            return (-1, -1);
        }
    }

    private string FormatBytes(long? bytes)
    {
        if (bytes == null || bytes < 0) return "Unknown";
        
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var suffixIndex = 0;
        double displaySize = bytes.Value;
        
        while (displaySize >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            displaySize /= 1024;
            suffixIndex++;
        }
        
        return $"{displaySize:F2} {suffixes[suffixIndex]}";
    }
    
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}

    [StructLayout(LayoutKind.Sequential)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        
        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

}