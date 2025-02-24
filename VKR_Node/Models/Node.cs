using System.Security.Cryptography;
using System.Text;
using VKR_Node.Utilities;

namespace VKR_Node.Models;
/// <summary>
/// Represents a node, storing its identifier,
/// successor, predecessor, and finger table.
/// </summary>
public class Node
{
    public Guid Identifier { get; private set; }
    public string Ip { get; private set; }
    public int Port { get; private set; }
    public Node Successor { get; set; }
    public Node Predecessor { get; set; }
    public List<Finger> FingerTable { get; private set; }
    public List<Key> StoredKeys { get; private set; }
    public int Load { get; set; }
    public List<string> StoredChunks { get; private set; } // Tracks chunk availability

    public Node(string ip, int port)
    {
        Ip = ip;
        Port = port;
        Identifier = GenerateIdentifier(ip, port);
        FingerTable = new List<Finger>(); 
        StoredKeys = new List<Key>();
        StoredChunks = new List<string>();
        Load = 0;
    }

    private Guid GenerateIdentifier(string ip, int port)
    {
        string input = $"{ip}:{port}";
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
            return new Guid(hash.Take(16).ToArray());
        }
    }

    public void UpdateLoad()
    {
        Load = StoredChunks.Count; // Example: Load is proportional to stored chunks
    }
    public void AddFinger(Finger finger)
    {
        FingerTable.Add(finger);
        Logger.Info($"Added Finger to Node {Identifier}: {finger}");
    }
}

public class Finger
{
    public Guid Start { get; set; }
    public Guid Interval { get; set; }
    public Node Successor { get; set; }
}