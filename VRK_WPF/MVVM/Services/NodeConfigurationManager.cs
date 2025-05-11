using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace VRK_WPF.MVVM.Services
{
    public class NodeConfigurationManager
    {
        private const string DefaultConfigFolder = "configs";
        private readonly Dictionary<string, int> _nodePorts = new Dictionary<string, int>();
        private int _lastAssignedPort = 5000;
        
        public string ConfigDirectory { get; private set; }
        public string CurrentNodeId { get; private set; }
        public string CurrentConfigPath { get; private set; }
        
        public NodeConfigurationManager()
        {
            ConfigDirectory = DefaultConfigFolder;
            CurrentNodeId = GenerateNodeId();
            EnsureConfigDirectoryExists();
        }
        
        private void EnsureConfigDirectoryExists()
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }
            
            if (Directory.GetFiles(ConfigDirectory, "*.json").Length == 0)
            {
                CreateDefaultConfig();
            }
        }
        
        private string GenerateNodeId()
        {
            string machineName = Environment.MachineName;
            string macId = GetMacAddress();
            
            string nodeId = $"Node-{machineName}-{macId.Substring(0, 4)}";
            
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                nodeId = nodeId.Replace(c, '_');
            }
            
            return nodeId;
        }
        
        private string GetMacAddress()
        {
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up && 
                       (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                        nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    {
                        return nic.GetPhysicalAddress().ToString();
                    }
                }
            }
            catch
            {
            }
            
            return DateTime.Now.Ticks.ToString("X");
        }
        
        private int FindAvailablePort()
        {
            int port = _lastAssignedPort + 1;
            while (true)
            {
                bool isAvailable = !_nodePorts.ContainsValue(port);
                
                if (isAvailable)
                {
                    try
                    {
                        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
                        listener.Start();
                        listener.Stop();
                    }
                    catch
                    {
                        isAvailable = false;
                    }
                }
                
                if (isAvailable)
                {
                    _lastAssignedPort = port;
                    return port;
                }
                
                port++;
                if (port > 65000)
                    port = 5001; 
            }
        }
        
        public string CreateDefaultConfig()
        {
            var config = new
            {
                DistributedStorage = new
                {
                    Identity = new
                    {
                        NodeId = CurrentNodeId,
                        DisplayName = $"Node on {Environment.MachineName}"
                    },
                    Network = new
                    {
                        ListenAddress = "localhost",
                        ListenPort = FindAvailablePort(),
                        MaxConnections = 100,
                        ConnectionTimeoutSeconds = 30,
                        KnownNodes = new object[] 
                        { 
                        }
                    },
                    Storage = new
                    {
                        BasePath = "C:/VKR_Network/ChunkData",
                        MaxSizeBytes = 10L * 1024 * 1024 * 1024, // 10GB
                        ChunkSize = 1 * 1024 * 1024, // 1MB
                        DefaultReplicationFactor = 3,
                        UseHashBasedDirectories = true,
                        HashDirectoryDepth = 2,
                        PerformIntegrityCheckOnStartup = true
                    },
                    Database = new
                    {
                        DatabasePath = "C:/VKR_Network/Data/node_storage.db",
                        AutoMigrate = true,
                        BackupBeforeMigration = true,
                        CommandTimeoutSeconds = 60,
                        EnableSqlLogging = false
                    },
                    Dht = new
                    {
                        StabilizationIntervalSeconds = 30,
                        FixFingersIntervalSeconds = 60,
                        CheckPredecessorIntervalSeconds = 45,
                        ReplicationCheckIntervalSeconds = 60,
                        ReplicationMaxParallelism = 10,
                        ReplicationFactor = 3,
                        AutoJoinNetwork = true,
                        BootstrapNodeAddress = "" 
                    }
                }
            };
            
            string configPath = Path.Combine(ConfigDirectory, $"{CurrentNodeId}-config.json");
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
            
            CurrentConfigPath = configPath;
            return configPath;
        }
        
        public Dictionary<string, string> GetNodeStartupParameters()
        {
            return new Dictionary<string, string>
            {
                ["--config"] = CurrentConfigPath,
                ["--NodeId"] = CurrentNodeId
            };
        }
        
        public List<NodeConfig> GetAvailableConfigs()
        {
            var configs = new List<NodeConfig>();
            
            foreach (string file in Directory.GetFiles(ConfigDirectory, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    string nodeId = ExtractNodeIdFromConfig(json);
                    
                    configs.Add(new NodeConfig
                    {
                        ConfigPath = file,
                        NodeId = nodeId,
                        IsCurrentNode = nodeId == CurrentNodeId
                    });
                }
                catch
                {
                }
            }
            
            return configs;
        }
        
        private string ExtractNodeIdFromConfig(string jsonConfig)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonConfig);
                if (doc.RootElement.TryGetProperty("DistributedStorage", out JsonElement dsElement) &&
                   dsElement.TryGetProperty("Identity", out JsonElement identityElement) &&
                   identityElement.TryGetProperty("NodeId", out JsonElement nodeIdElement))
                {
                    return nodeIdElement.GetString() ?? Path.GetFileNameWithoutExtension(CurrentConfigPath);
                }
            }
            catch
            {
            }
            
            return Path.GetFileNameWithoutExtension(CurrentConfigPath);
        }
        
        public void SetCurrentConfig(string configPath)
        {
            if (File.Exists(configPath))
            {
                CurrentConfigPath = configPath;
                
                try
                {
                    string json = File.ReadAllText(configPath);
                    CurrentNodeId = ExtractNodeIdFromConfig(json);
                }
                catch
                {
                }
            }
        }
    }
    
    public class NodeConfig
    {
        public string NodeId { get; set; }
        public string ConfigPath { get; set; }
        public bool IsCurrentNode { get; set; }
        
        public override string ToString()
        {
            return IsCurrentNode ? $"{NodeId} (Current)" : NodeId;
        }
    }
}