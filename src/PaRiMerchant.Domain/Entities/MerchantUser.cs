using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class MerchantUser : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Guid MerchantProfileId { get; set; }
    public MerchantProfile MerchantProfile { get; set; } = null!;

    public string DisplayNameCiphertext { get; set; } = string.Empty;
    public string EmailCiphertext { get; set; } = string.Empty;
    public string EmailBlindIndex { get; set; } = string.Empty;
    public string PhoneCiphertext { get; set; } = string.Empty;
    public string PhoneBlindIndex { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string MpinHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.MerchantAdmin;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginUtc { get; set; }

    public ICollection<RefreshSession> RefreshSessions { get; set; } = new List<RefreshSession>();
}
