using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IWalletRepository
{
    Task<CustomerWallet?> GetWalletAsync(string userId);
    Task<CustomerWallet> GetOrCreateWalletAsync(string userId);
    Task<WalletTransaction> CreditWalletAsync(string userId, decimal amount, string description, string source, string? referenceId = null, string? razorpayPaymentId = null);
    Task<WalletTransaction?> DebitWalletAsync(string userId, decimal amount, string description, string source, string? referenceId = null);
    Task<List<WalletTransaction>> GetWalletTransactionsAsync(string userId, int page = 1, int pageSize = 20);
}
