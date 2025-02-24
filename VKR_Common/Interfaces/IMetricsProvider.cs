namespace VKR_Common.Interfaces;

public interface IMetricsProvider
{
    Task<Dictionary<string, float>> GetNodeMetricsAsync(string nodeId);
    Task UpdateMetricsAsync(string nodeId, float cpu, float memory, float network);
    //TODO: Включите метод для массового обновления метрик, чтобы уменьшить накладные расходы на gRPC в больших сетях.
}