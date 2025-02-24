using VKR_Node.Utilities;

namespace VKR_Node.Models;
/// <summary>
/// Represents the metadata for a key (e.g., hash, location).
/// </summary>
public class Key
{
    public Guid Identifier { get; private set; } // Unique identifier for the key
    public string Data { get; private set; }     // Example data associated with the key
    public DateTime CreatedAt { get; private set; }

    public Key(string data)
    {
        Data = data;
        Identifier = Hashing.ComputeSha1Guid(data);
        CreatedAt = DateTime.UtcNow;
    }

    public override string ToString()
    {
        return $"Key: {Identifier}, Data: {Data}, CreatedAt: {CreatedAt}";
    }
}