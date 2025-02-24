using VKR_Common.Models;

public class Transaction
{
    public string Id { get; set; }
    public TransactionType Type { get; set; }
    public string InitiatorNode { get; set; }
    public List<string> ParticipantNodes { get; set; } = new();
    public TransactionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public TimeSpan GetDuration() => CompletedAt.HasValue ? CompletedAt.Value - CreatedAt : TimeSpan.Zero;

    public string ToCsv()
    {
        return $"{Id},{Type},{InitiatorNode},{string.Join(";", ParticipantNodes)},{Status},{CreatedAt:o},{CompletedAt:o}";
    }

    public static Transaction FromCsv(string csv)
    {
        var parts = csv.Split(',');
        return new Transaction
        {
            Id = parts[0],
            Type = Enum.Parse<TransactionType>(parts[1]),
            InitiatorNode = parts[2],
            ParticipantNodes = parts[3].Split(';').ToList(),
            Status = Enum.Parse<TransactionStatus>(parts[4]),
            CreatedAt = DateTime.Parse(parts[5]),
            CompletedAt = string.IsNullOrWhiteSpace(parts[6]) ? (DateTime?)null : DateTime.Parse(parts[6])
        };
    }
}