namespace VKR_Common.Models;

public class Node
{
    public string Id { get; set; } = Environment.MachineName;
    public string Address { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5000;
    public DateTime LastPingTime { get; set; } = DateTime.UtcNow;
    public double CpuLoad { get; set; }
    public double MemoryUsage { get; set; }
    public double NetworkLoad { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastUpdated { get; set; }

    public Node() { }

    public Node(string id, string address, int port)
    {
        Id = id;
        Address = address;
        Port = port;
    }

    public void UpdateStatus(float cpu, float memory, float network)
    {
        CpuLoad = cpu;
        MemoryUsage = memory;
        NetworkLoad = network;
        LastUpdated = DateTime.UtcNow;
    }

    public float GetNodeUtilization()
    {
        return (float)((CpuLoad + MemoryUsage + NetworkLoad) / 3.0);
    }

    public bool IsHealthy(TimeSpan timeout)
    {
        return IsActive && DateTime.UtcNow - LastPingTime <= timeout;
    }
}