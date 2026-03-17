using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class Transaction : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? BeneficiaryId { get; set; }

    public TransactionType TransactionType { get; set; }
    public TransactionStatus Status { get; set; } = TransactionStatus.Initiated;
    public decimal Amount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string Currency { get; set; } = "INR";
    public string ExternalReference { get; set; } = string.Empty;
    public string ProviderReference { get; set; } = string.Empty;
    public string FailureCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SettlementStatus { get; set; } = string.Empty;

    public ICollection<TransactionEvent> Events { get; set; } = new List<TransactionEvent>();
    public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
    public CardCollectionDetail? CardCollection { get; set; }
    public WalletTopupDetail? WalletTopup { get; set; }
    public PayoutDetail? Payout { get; set; }
}
