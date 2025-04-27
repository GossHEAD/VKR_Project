namespace VKR_Node.Configuration;

public class NodeOptions
{
    //public string? Id { get; set; } = "Node4";
    public string? NodeId { get; set; } = "Node4";
    public string? Address { get; set; } = "localhost:5004";
    //public string? IpAddress { get; set; } = "localhost:5004";
    public string DataPath { get; set; } = "Data/node4_storage.db";
    public List<string>? BootstrapPeers { get; set; } // заглушка на время 
    public List<ChunkStorageNode> KnownNodes { get; set; } = new List<ChunkStorageNode>(); 
}

