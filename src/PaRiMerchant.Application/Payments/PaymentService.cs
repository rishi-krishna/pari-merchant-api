using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Payments;

public sealed class PaymentService(IAppDbContext dbContext, ISensitiveDataProtector protector)
{
    public async Task<PaymentTransactionResponse> InitiateCollectionAsync(Guid tenantId, InitiateCollectionRequest request, CancellationToken cancellationToken)
    {
        var fee = Math.Round(request.Amount * 0.025m, 2);
        var transaction = BuildBaseTransaction(tenantId, TransactionType.CardCollection, request.Amount, fee, request.Currency, request.Description);
        transaction.Status = TransactionStatus.PendingProvider;
        transaction.SettlementStatus = "AwaitingProvider";
        transaction.CardCollection = new CardCollectionDetail
        {
            TransactionId = transaction.Id,
            CardBrand = request.CardBrand,
            MaskedCardNumber = request.MaskedCardNumber,
            ProviderTokenReference = request.ProviderTokenReference,
            CustomerNameCiphertext = protector.Encrypt(request.CustomerName)
        };

        transaction.Events.Add(BuildEvent(transaction, "collection_initiated", transaction.Status, "Customer collection initiated."));
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(transaction);
    }

    public async Task<PaymentTransactionResponse> InitiateSelfTopupAsync(Guid tenantId, InitiateSelfTopupRequest request, CancellationToken cancellationToken)
    {
        var fee = Math.Round(request.Amount * 0.01m, 2);
        var transaction = BuildBaseTransaction(tenantId, TransactionType.SelfTopup, request.Amount, fee, request.Currency, request.Description);
        transaction.Status = TransactionStatus.PendingProvider;
        transaction.SettlementStatus = "AwaitingProvider";
        transaction.WalletTopup = new WalletTopupDetail
        {
            TransactionId = transaction.Id,
            CardBrand = request.CardBrand,
            MaskedCardNumber = request.MaskedCardNumber,
            ProviderTokenReference = request.ProviderTokenReference
        };

        transaction.Events.Add(BuildEvent(transaction, "self_topup_initiated", transaction.Status, "Merchant self-topup initiated."));
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(transaction);
    }

    public async Task<PaymentTransactionResponse> CreatePayoutAsync(Guid tenantId, CreatePayoutRequest request, CancellationToken cancellationToken)
    {
        var beneficiaryId = Guid.Parse(request.BeneficiaryId);
        var beneficiary = await dbContext.Beneficiaries
            .Include(item => item.Contact)
            .FirstOrDefaultAsync(item => item.Id == beneficiaryId && item.TenantId == tenantId && item.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Beneficiary not found.");

        if (beneficiary.Status != BeneficiaryStatus.Validated)
        {
            throw new InvalidOperationException("Beneficiary must be validated before payout.");
        }

        var summary = await GetWalletSnapshotAsync(tenantId, cancellationToken);
        if (summary.AvailableBalance < request.Amount)
        {
            throw new InvalidOperationException("Insufficient wallet balance.");
        }

        var transaction = BuildBaseTransaction(tenantId, TransactionType.Payout, request.Amount, 0m, request.Currency, request.Purpose);
        transaction.BeneficiaryId = beneficiary.Id;
        transaction.ContactId = beneficiary.ContactId;
        transaction.Status = TransactionStatus.Processing;
        transaction.SettlementStatus = "HoldPlaced";
        transaction.Payout = new PayoutDetail
        {
            TransactionId = transaction.Id,
            BeneficiaryId = beneficiary.Id,
            Purpose = request.Purpose,
            BankReference = string.Empty
        };

        transaction.Events.Add(BuildEvent(transaction, "payout_created", transaction.Status, "Payout created and funds held."));
        transaction.LedgerEntries.Add(new LedgerEntry
        {
            TenantId = tenantId,
            TransactionId = transaction.Id,
            EntryType = LedgerEntryType.Hold,
            Amount = request.Amount,
            Currency = request.Currency,
            Description = "Payout hold"
        });

        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(transaction);
    }

    private async Task<(decimal AvailableBalance, decimal HeldBalance)> GetWalletSnapshotAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.LedgerEntries.Where(item => item.TenantId == tenantId).ToListAsync(cancellationToken);
        decimal credits = entries.Where(item => item.EntryType is LedgerEntryType.Credit or LedgerEntryType.Reversal).Sum(item => item.Amount);
        decimal debits = entries.Where(item => item.EntryType is LedgerEntryType.Debit or LedgerEntryType.Fee).Sum(item => item.Amount);
        decimal holds = entries.Where(item => item.EntryType == LedgerEntryType.Hold).Sum(item => item.Amount);
        return (credits - debits - holds, holds);
    }

    private static Transaction BuildBaseTransaction(Guid tenantId, TransactionType type, decimal amount, decimal feeAmount, string currency, string description) =>
        new()
        {
            TenantId = tenantId,
            TransactionType = type,
            Amount = amount,
            FeeAmount = feeAmount,
            NetAmount = amount - feeAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "INR" : currency,
            Description = description,
            ExternalReference = $"TRX-{Guid.NewGuid():N}"[..16],
            ProviderReference = string.Empty
        };

    private static TransactionEvent BuildEvent(Transaction transaction, string eventType, TransactionStatus status, string notes) =>
        new()
        {
            TransactionId = transaction.Id,
            EventType = eventType,
            Status = status,
            Notes = notes
        };

    private static PaymentTransactionResponse Map(Transaction transaction) =>
        new(transaction.Id.ToString(), transaction.TransactionType.ToString(), transaction.Status.ToString(), transaction.Amount, transaction.FeeAmount, transaction.NetAmount, transaction.Currency, transaction.Description, transaction.ExternalReference, transaction.ProviderReference);
}
