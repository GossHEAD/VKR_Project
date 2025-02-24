using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using VKR_Core.Interfaces;

namespace VKR_Core.Services;

public class SerializationService : ISerializationService
{
    public byte[] SerializeToCsv<T>(T data)
    {
        using var memoryStream = new MemoryStream();
        using var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);
        using var csvWriter = new CsvWriter(streamWriter, new CsvConfiguration(CultureInfo.InvariantCulture));

        csvWriter.WriteRecord(data);
        streamWriter.Flush();

        return memoryStream.ToArray();
    }

    public T DeserializeFromCsv<T>(byte[] data)
    {
        using var memoryStream = new MemoryStream(data);
        using var streamReader = new StreamReader(memoryStream, Encoding.UTF8);
        using var csvReader = new CsvReader(streamReader, new CsvConfiguration(CultureInfo.InvariantCulture));

        return csvReader.GetRecord<T>();
    }
}