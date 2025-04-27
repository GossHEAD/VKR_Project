using VKR_Core.Models;

namespace VKR_Core.Services;

/// <summary>
/// Отвечает за логику репликации чанков данных между узлами.
/// </summary>
public interface IReplicationManager
{
    /// <summary>
    /// Инициирует процесс репликации указанного чанка на другие узлы
    /// согласно текущим правилам (ReplicationFactor, стратегия выбора узлов).
    /// </summary>
    /// <param name="chunkInfo">Метаданные чанка, который нужно реплицировать.</param>
    /// <param name="sourceDataStream">Поток с данными чанка для отправки репликам.</param>
    /// <param name="replicationFactor">Требуемое количество реплик (включая первичную копию).</param>
    Task ReplicateChunkAsync(ChunkInfoCore chunkInfo, Func<Task<Stream>> sourceDataStreamFactory, int replicationFactor, CancellationToken cancellationToken = default);
    // Используем Func<Task<Stream>> чтобы не читать поток зря, если репликация не нужна или невозможна

    /// <summary>
    /// Проверяет и при необходимости восстанавливает требуемый уровень репликации
    /// для всех чанков указанного файла. Вызывается периодически или после сбоев.
    /// </summary>
    /// <param name="fileId">Идентификатор файла для проверки.</param>
    Task EnsureReplicationLevelAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет и при необходимости восстанавливает требуемый уровень репликации
    /// для одного чанка.
    /// </summary>
    /// <param name="chunkId">Идентификатор чанка для проверки.</param>
    Task EnsureChunkReplicationAsync(string fileId, string chunkId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Обрабатывает входящий запрос на сохранение реплики чанка на текущем узле.
    /// (Вызывается из gRPC сервиса).
    /// </summary>
    /// <param name="chunkInfo">Метаданные чанка.</param>
    /// <param name="dataStream">Поток с данными чанка.</param>
    Task HandleIncomingReplicaAsync(ChunkInfoCore chunkInfo, Stream dataStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Обрабатывает уведомление об удалении чанка (или файла), чтобы удалить локальную реплику.
    /// </summary>
    /// <param name="chunkId">Идентификатор удаляемого чанка.</param>
    Task HandleDeleteNotificationAsync(string chunkId, CancellationToken cancellationToken = default);
}