using System.Security.Cryptography;
using System.Text;

namespace VKR_Common.Models;

public class DataBlock
{
    public string Id { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public byte[] Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<string> ReplicationNodes { get; set; } = new();
    public long Size { get; set; } // Size of the data block in bytes
    public string Hash { get; set; } // Hash for data integrity verification
    public DateTime CreatedAt { get; set; } // Timestamp of block creation

    public DataBlock(string id, string nodeId, byte[] data)
    {
        Id = id;
        NodeId = nodeId;
        Data = data;
    }
    
    public DataBlock(string id, string nodeId, byte[] data, DateTime timestamp)
    {
        Id = id;
        NodeId = nodeId;
        Data = data;
        Timestamp = timestamp;
    }

    public DataBlock() { }
    
    public string ToCsv()
    {
        return $"{Id},{Convert.ToBase64String(Data)},{string.Join(";", ReplicationNodes)},{Size},{Hash},{CreatedAt:o}";
    }

    // Deserialize from CSV
    public static DataBlock FromCsv(string csv)
    {
        var parts = csv.Split(',');
        return new DataBlock
        {
            Id = parts[0],
            Data = Convert.FromBase64String(parts[1]),
            ReplicationNodes = parts[2].Split(';').ToList(),
            Size = long.Parse(parts[3]),
            Hash = parts[4],
            CreatedAt = DateTime.Parse(parts[5])
        };
    }

    public byte[] ToByteArray(DataBlock data)
    {
        //var bytes = new byte[data.Size];
        return Convert.FromBase64String(data.ToCsv());
    }
    public bool ValidateHash(byte[] data, string expectedHash)
    {
        using var sha256 = SHA256.Create();
        var hash = Convert.ToBase64String(sha256.ComputeHash(data));
        return hash == expectedHash;
    }
}