namespace PaRiMerchant.Application.Payments;

public sealed class CashfreeOptions
{
    public const string SectionName = "Cashfree";

    public string WebhookSecret { get; set; } = string.Empty;
}
