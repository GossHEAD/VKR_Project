using System.Threading.Tasks;
using Xunit;
using VKR_Common.Models;
using VKR_Core;
using VKR_Core.Services;
using Assert = Xunit.Assert;

public class TransactionServiceTests
{
    private readonly TransactionService _transactionService;

    public TransactionServiceTests()
    {
        _transactionService = new TransactionService();
    }

    [Fact]
    public async Task CreateTransaction_ShouldAddTransactionSuccessfully()
    {
        var transaction = new Transaction { Id = "tx1", Type = "Create", Status = "Pending" };
        await _transactionService.CreateTransactionAsync(transaction);

        var result = await _transactionService.GetTransactionStatusAsync("tx1");
        Assert.NotNull(result);
        Assert.Equal("tx1", result.Id);
    }

    [Fact]
    public async Task UpdateTransaction_ShouldUpdateStatusSuccessfully()
    {
        var transaction = new Transaction { Id = "tx1", Type = "Create", Status = "Pending" };
        await _transactionService.CreateTransactionAsync(transaction);
        await _transactionService.UpdateTransactionAsync("tx1", "Completed");

        var result = await _transactionService.GetTransactionStatusAsync("tx1");
        Assert.Equal("Completed", result.Status);
    }
}