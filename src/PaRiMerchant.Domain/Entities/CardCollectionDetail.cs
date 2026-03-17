namespace PaRiMerchant.Domain.Entities;

public sealed class CardCollectionDetail
{
    public Guid TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    public string CardBrand { get; set; } = string.Empty;
    public string MaskedCardNumber { get; set; } = string.Empty;
    public string ProviderTokenReference { get; set; } = string.Empty;
    public string CustomerNameCiphertext { get; set; } = string.Empty;
}
