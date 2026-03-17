using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Wallet;

public sealed class WalletService(IAppDbContext dbContext)
{
    public async Task<WalletSummaryResponse> GetSummaryAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.LedgerEntries.Where(item => item.TenantId == tenantId).ToListAsync(cancellationToken);
        decimal credits = entries.Where(item => item.EntryType is LedgerEntryType.Credit or LedgerEntryType.Reversal).Sum(item => item.Amount);
        decimal debits = entries.Where(item => item.EntryType is LedgerEntryType.Debit or LedgerEntryType.Fee).Sum(item => item.Amount);
        decimal holds = entries.Where(item => item.EntryType == LedgerEntryType.Hold).Sum(item => item.Amount);

        return new WalletSummaryResponse(credits - debits - holds, holds, "INR");
    }

    public async Task<IReadOnlyList<LedgerEntryResponse>> GetLedgerAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.LedgerEntries
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.CreatedUtc)
            .ToListAsync(cancellationToken);

        return entries.Select(item => new LedgerEntryResponse(item.Id.ToString(), item.EntryType.ToString(), item.Amount, item.Currency, item.Description, item.CreatedUtc, item.TransactionId?.ToString())).ToList();
    }
}
