using VKR_Core.Models;

namespace VKR_Core.Services;

/// <summary>
/// Определяет контракт для сервиса распределенной хеш-таблицы (DHT),
/// отвечающего за поиск узлов и поддержание структуры сети.
/// </summary>
public interface IDhtService
{
    /// <summary>
    /// Находит узел, ответственный за хранение ключа (ID файла, чанка или узла).
    /// </summary>
    /// <param name="keyId">Идентификатор ключа.</param>
    /// <returns>Информация об ответственном узле.</returns>
    Task<NodeInfoCore> FindSuccessorAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает информацию о текущем предшественнике данного узла в кольце DHT.
    /// </summary>
    /// <returns>Информация о предшественнике или null, если его нет.</returns>
    Task<NodeInfoCore?> GetPredecessorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Обрабатывает уведомление от узла, который считает себя предшественником данного.
    /// </summary>
    /// <param name="potentialPredecessor">Узел, отправивший уведомление.</param>
    Task NotifyAsync(NodeInfoCore potentialPredecessor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает периодическую задачу стабилизации для поддержания корректности ссылок
    /// (например, проверка successor'а и уведомление его).
    /// </summary>
    Task StabilizeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает периодическую задачу обновления таблицы пальцев (для ускорения поиска в Chord).
    /// </summary>
    Task FixFingersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Запускает периодическую проверку доступности предшественника.
    /// </summary>
    Task CheckPredecessorAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает информацию о текущем (локальном) узле.
    /// </summary>
    NodeInfoCore GetCurrentNodeInfo();

     /// <summary>
    /// Инициирует процесс присоединения к существующей DHT сети через известный узел.
    /// </summary>
    /// <param name="bootstrapNode">Известный узел для подключения.</param>
    Task JoinNetworkAsync(NodeInfoCore bootstrapNode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Инициирует процесс корректного выхода из сети (передача ключей и т.д.).
    /// </summary>
    Task LeaveNetworkAsync(CancellationToken cancellationToken = default);
}