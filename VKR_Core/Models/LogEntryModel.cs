using VKR_Core.Enums;

namespace VKR_Core.Models;

public record LogEntryModel
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public LogLevelEnum LogLevel { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ExceptionDetails { get; set; }
}