using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;

namespace PaRiMerchant.Application.Transactions;

public sealed class TransactionService(IAppDbContext dbContext)
{
    public async Task<IReadOnlyList<TransactionResponse>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var items = await dbContext.Transactions
            .Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.CreatedUtc)
            .ToListAsync(cancellationToken);

        return items.Select(Map).ToList();
    }

    public async Task<TransactionResponse> GetByIdAsync(Guid tenantId, Guid transactionId, CancellationToken cancellationToken)
    {
        var item = await dbContext.Transactions.FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.Id == transactionId, cancellationToken)
            ?? throw new KeyNotFoundException("Transaction not found.");

        return Map(item);
    }

    public async Task<IReadOnlyList<TransactionEventResponse>> GetEventsAsync(Guid tenantId, Guid transactionId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Transactions.AnyAsync(candidate => candidate.TenantId == tenantId && candidate.Id == transactionId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Transaction not found.");
        }

        var events = await dbContext.TransactionEvents
            .Where(item => item.TransactionId == transactionId)
            .OrderBy(item => item.CreatedUtc)
            .ToListAsync(cancellationToken);

        return events.Select(item => new TransactionEventResponse(item.Id.ToString(), item.EventType, item.Status.ToString(), item.Notes, item.CreatedUtc)).ToList();
    }

    private static TransactionResponse Map(Domain.Entities.Transaction item) =>
        new(item.Id.ToString(), item.TransactionType.ToString(), item.Status.ToString(), item.Amount, item.FeeAmount, item.NetAmount, item.Currency, item.ExternalReference, item.ProviderReference, item.Description, item.SettlementStatus, item.CreatedUtc);
}
