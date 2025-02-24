namespace VKR_Core.Interfaces;

public interface ISerializationService
{
    byte[] SerializeToCsv<T>(T data); 
    T DeserializeFromCsv<T>(byte[] data); 
}
