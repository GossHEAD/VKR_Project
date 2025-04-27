namespace VKR_Node.Configuration;

/// <summary>
/// Опции конфигурации для подключения и настройки базы данных метаданных.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// Строка подключения к базе данных SQLite.
    /// Если не задана, будет сконструирована на основе StorageOptions.DataPath и NodeOptions.Id.
    /// </summary>
    public string? ConnectionString { get; set; } // Оставляем возможность задать явно

    public string DatabasePath { get; set; } = "Data/node_storage.db"; // Default relative path
    /// <summary>
    /// Имя файла базы данных (по умолчанию будет 'node_{NodeId}.db').
    /// Используется, если ConnectionString не задан явно.
    /// </summary>
    public string? FileName { get; set; }
}