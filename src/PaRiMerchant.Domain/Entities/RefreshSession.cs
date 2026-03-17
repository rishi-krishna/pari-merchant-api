using PaRiMerchant.Domain.Common;

namespace PaRiMerchant.Domain.Entities;

public sealed class RefreshSession : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Guid MerchantUserId { get; set; }
    public MerchantUser MerchantUser { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public string CreatedByIp { get; set; } = string.Empty;
}
