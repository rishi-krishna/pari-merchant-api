using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class MerchantProfile : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public string MerchantCode { get; set; } = string.Empty;
    public string DisplayNameCiphertext { get; set; } = string.Empty;
    public string CompanyNameCiphertext { get; set; } = string.Empty;
    public KycStatus KycStatus { get; set; } = KycStatus.Draft;
    public string CountryCode { get; set; } = "IN";
}
