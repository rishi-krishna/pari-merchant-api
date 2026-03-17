using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class TransactionEvent : AuditedEntity
{
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    public string EventType { get; set; } = string.Empty;
    public TransactionStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}
