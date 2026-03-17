using PaRiMerchant.Domain.Common;

namespace PaRiMerchant.Domain.Entities;

public sealed class AuditEvent : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
}
