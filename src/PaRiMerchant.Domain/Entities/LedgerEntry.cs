using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class LedgerEntry : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Guid? TransactionId { get; set; }
    public Transaction? Transaction { get; set; }

    public LedgerEntryType EntryType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Description { get; set; } = string.Empty;
}
