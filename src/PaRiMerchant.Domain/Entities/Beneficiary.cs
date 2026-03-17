using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class Beneficiary : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Guid ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public string AccountHolderNameCiphertext { get; set; } = string.Empty;
    public string AccountHolderSearchToken { get; set; } = string.Empty;
    public string AccountNumberCiphertext { get; set; } = string.Empty;
    public string AccountNumberBlindIndex { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;
    public string Ifsc { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string AccountType { get; set; } = "Savings";
    public BeneficiaryStatus Status { get; set; } = BeneficiaryStatus.PendingValidation;
    public bool IsActive { get; set; } = true;
}
