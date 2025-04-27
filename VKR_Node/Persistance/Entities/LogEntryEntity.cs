using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace VKR_Node.Persistance.Entities;

/// <summary>
/// Запись в журнале событий узла.
/// </summary>
[Table("Logs")]
// Индекс по Timestamp для быстрой сортировки/фильтрации по времени
[Index(nameof(Timestamp))]
public class LogEntryEntity
{
    /// <summary>
    /// Primary Key (автоинкремент).
    /// </summary>
    [Key]
    public long Id { get; set; }

    /// <summary>
    /// Время события.
    /// </summary>
    [Required]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Уровень логирования (Info, Warning, Error).
    /// </summary>
    [Required]
    // Используем int или string в зависимости от того, как хотим хранить enum
    public int Level { get; set; } // или public string Level { get; set; }

    /// <summary>
    /// Идентификатор узла, на котором произошло событие (если логи централизованные).
    /// Для локальных логов можно не хранить.
    /// </summary>
    // [MaxLength(128)]
    // public string? NodeId { get; set; }

    /// <summary>
    /// Сообщение лога.
    /// </summary>
    [Required]
    public string Message { get; set; } = null!;

    /// <summary>
    /// Детали исключения (если есть).
    /// </summary>
    public string? ExceptionDetails { get; set; }
}