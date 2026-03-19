namespace PaRiMerchant.Application.Payments;

public sealed class CashfreeOptions
{
    public const string SectionName = "Cashfree";

    public string BaseUrl { get; set; } = "https://sandbox.cashfree.com/pg";
    public string ApiVersion { get; set; } = "2025-01-01";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
}
