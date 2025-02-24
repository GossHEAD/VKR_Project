using VKR_Common.Models;

namespace VKR_Common.Interfaces;

public interface ITransactionManager
{
    Task CreateTransactionAsync(Transaction transaction);
    Task<Transaction> GetTransactionStatusAsync(string transactionId);
    Task UpdateTransactionAsync(string transactionId, string status);
}