using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VKR_Node.Persistance.Entities;

[Table("Logs")]
[Index(nameof(Timestamp))]
public class LogEntryEntity
{
    [Key]
    public long Id { get; set; }
    [Required]
    public DateTimeOffset Timestamp { get; set; }
    [Required]
    public int Level { get; set; } 
    [Required]
    public string Message { get; set; } = null!;
    
    public string? ExceptionDetails { get; set; }
}