namespace VKR_Node.Configuration;

public class DhtOptions
{
    public string? BootstrapNodeAddress { get; set; }
    public int StabilizationIntervalSeconds { get; set; } = 30;
    public int FixFingersIntervalSeconds { get; set; } = 60;
    public int CheckPredecessorIntervalSeconds { get; set; } = 45;
    public int ReplicationCheckIntervalSeconds { get; set; } = 60;
    public int ReplicationMaxParallelism { get; set; } = 10;
    public int ReplicationFactor { get; set; } = 3;
}