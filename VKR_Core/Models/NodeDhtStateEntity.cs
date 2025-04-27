using System.ComponentModel.DataAnnotations;

namespace VKR_Core.Models;

public record NodeDhtStateEntity
{
    [Key]
    public required string NodeId { get; init; }
    public string? SuccessorId { get; init; }
    public string? PredecessorId { get; init; }
    // Здесь можно хранить сериализованную Finger Table или другие данные DHT
    public string? DhtSpecificDataJson { get; init; }
    public DateTimeOffset LastUpdated { get; set; }
}