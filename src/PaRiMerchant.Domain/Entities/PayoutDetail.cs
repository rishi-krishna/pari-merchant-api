namespace PaRiMerchant.Domain.Entities;

public sealed class PayoutDetail
{
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    public Guid BeneficiaryId { get; set; }
    public Beneficiary Beneficiary { get; set; } = null!;

    public string BankReference { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
}
