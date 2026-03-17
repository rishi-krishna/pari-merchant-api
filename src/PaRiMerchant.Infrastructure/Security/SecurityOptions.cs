namespace PaRiMerchant.Infrastructure.Security;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public string EncryptionKey { get; set; } = string.Empty;
    public string BlindIndexKey { get; set; } = string.Empty;
    public string PasswordPepper { get; set; } = string.Empty;
    public string JwtSecret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "PaRiMerchant.Api";
    public string Audience { get; set; } = "PaRiMerchant.Client";
}
