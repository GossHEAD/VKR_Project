using VKR_Core.Models;

namespace VKR_Core.Services;

/// <summary>
/// Управляет физическим хранением и извлечением чанков данных на локальном узле.
/// </summary>
public interface IDataManager
{
    /// <summary>
    /// Сохраняет данные чанка из потока в локальное хранилище.
    /// </summary>
    /// <param name="chunkInfo">Метаданные сохраняемого чанка.</param>
    /// <param name="dataStream">Поток с данными чанка.</param>
    /// <returns>Путь к сохраненному файлу или идентификатор хранилища.</returns>
    Task<string> StoreChunkAsync(ChunkModel chunkInfo, Stream dataStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает поток для чтения данных указанного чанка.
    /// </summary>
    /// <param name="chunkInfo">Метаданные чанка для чтения.</param>
    /// <returns>Поток с данными чанка или null, если чанк не найден.</returns>
    Task<Stream?> RetrieveChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет физические данные чанка из локального хранилища.
    /// </summary>
    /// <param name="chunkInfo">Метаданные удаляемого чанка.</param>
    Task<bool> DeleteChunkAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет наличие данных чанка в локальном хранилище.
    /// </summary>
    /// <param name="chunkInfo">Метаданные проверяемого чанка.</param>
    /// <returns>True, если данные существуют локально.</returns>
    Task<bool> ChunkExistsAsync(ChunkModel chunkInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает свободное дисковое пространство в байтах в хранилище данных.
    /// </summary>
    Task<long> GetFreeDiskSpaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает общий размер дискового пространства в байтах в хранилище данных.
    /// </summary>
    Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default);
}