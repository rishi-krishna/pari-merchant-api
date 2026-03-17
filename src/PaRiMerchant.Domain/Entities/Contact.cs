using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class Contact : AuditedEntity
{
    public Guid TenantId { get; set; }

    public string NameCiphertext { get; set; } = string.Empty;
    public string NameSearchToken { get; set; } = string.Empty;
    public string EmailCiphertext { get; set; } = string.Empty;
    public string EmailBlindIndex { get; set; } = string.Empty;
    public string PhoneCiphertext { get; set; } = string.Empty;
    public string PhoneBlindIndex { get; set; } = string.Empty;
    public string CityCiphertext { get; set; } = string.Empty;

    public ContactStatus Status { get; set; } = ContactStatus.Active;

    public ICollection<Beneficiary> Beneficiaries { get; set; } = new List<Beneficiary>();
}
