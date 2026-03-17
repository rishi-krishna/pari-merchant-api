using PaRiMerchant.Domain.Common;

namespace PaRiMerchant.Domain.Entities;

public sealed class Tenant : AuditedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<MerchantProfile> MerchantProfiles { get; set; } = new List<MerchantProfile>();
    public ICollection<MerchantUser> MerchantUsers { get; set; } = new List<MerchantUser>();
}
