using PaRiMerchant.Domain.Common;

namespace PaRiMerchant.Domain.Entities;

public sealed class KycDocument : AuditedEntity
{
    public Guid TenantId { get; set; }
    public Guid KycProfileId { get; set; }
    public KycProfile KycProfile { get; set; } = null!;

    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string Sha256Hash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
