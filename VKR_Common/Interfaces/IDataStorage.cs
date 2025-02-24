namespace VKR_Common.Interfaces;

public interface IDataStorage
{
    Task SaveFileAsync(string fileId, byte[] data);
    Task<byte[]> LoadFileAsync(string fileId);
    Task DeleteFileAsync(string fileId);
    Task<List<string>> GetReplicaNodesAsync(string fileId);
    Task<List<string>> ListFilesByNodeAsync(string nodeId);
}