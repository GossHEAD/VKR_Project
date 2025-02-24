using System.Threading.Tasks;

namespace VKR_Common.Interfaces;

public interface INetworkClient
{
    Task SendFileAsync(string nodeId, byte[] data); 
    Task<byte[]> ReceiveFileAsync(string nodeId); 
    Task DisconnectAsync(); 
    Task ConnectAsync(); 
    Task<bool> IsConnectedAsync(); 
}