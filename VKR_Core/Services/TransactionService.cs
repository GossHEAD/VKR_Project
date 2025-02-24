using VKR_Common.Interfaces;
using VKR_Common.Models;


namespace VKR_Core.Services;

public class TransactionService : ITransactionManager
{
    private readonly Dictionary<string, Transaction> _transactions = new();

    // Создание новой транзакции
    public async Task CreateTransactionAsync(Transaction transaction)
    {
        if (!_transactions.ContainsKey(transaction.Id))
        {
            _transactions[transaction.Id] = transaction;
        }
    }

    // Получение статуса транзакции
    public async Task<Transaction> GetTransactionStatusAsync(string transactionId)
    {
        return _transactions.TryGetValue(transactionId, out var transaction) ? transaction : null;
    }

    // Обновление транзакции
    public async Task UpdateTransactionAsync(string transactionId, string status)
    {
        if (_transactions.TryGetValue(transactionId, out var transaction))
        {
            transaction.Status = status;
            transaction.CompletedAt = status == "Completed" ? DateTime.UtcNow : null;
        }
    }

    // Получение всех транзакций
    public async Task<List<Transaction>> GetAllTransactionsAsync()
    {
        return _transactions.Values.ToList();
    }

    // Удаление завершенных транзакций
    public async Task CleanupCompletedTransactionsAsync()
    {
        var completedTransactionIds = _transactions
            .Where(t => t.Value.Status == "Completed")
            .Select(t => t.Key)
            .ToList();

        foreach (var id in completedTransactionIds)
        {
            _transactions.Remove(id);
        }
    }
}

