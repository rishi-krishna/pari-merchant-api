using PaRiMerchant.Domain.Common;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Domain.Entities;

public sealed class KycProfile : AuditedEntity
{
    public Guid TenantId { get; set; }
    public KycStatus Status { get; set; } = KycStatus.Draft;

    public string NameCiphertext { get; set; } = string.Empty;
    public string PanCiphertext { get; set; } = string.Empty;
    public string PanBlindIndex { get; set; } = string.Empty;
    public string DateOfBirthCiphertext { get; set; } = string.Empty;
    public string AadhaarMasked { get; set; } = string.Empty;
    public string AadhaarCiphertext { get; set; } = string.Empty;

    public string AccountHolderNameCiphertext { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumberCiphertext { get; set; } = string.Empty;
    public string AccountNumberBlindIndex { get; set; } = string.Empty;
    public string Ifsc { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;

    public string CompanyNameCiphertext { get; set; } = string.Empty;
    public string CompanyTypeCiphertext { get; set; } = string.Empty;
    public string CompanyGstCiphertext { get; set; } = string.Empty;
    public string CompanyAddressCiphertext { get; set; } = string.Empty;
    public string CompanyCityCiphertext { get; set; } = string.Empty;
    public string CompanyStateCiphertext { get; set; } = string.Empty;
    public string CompanyPincodeCiphertext { get; set; } = string.Empty;
    public string CompanyCountryCiphertext { get; set; } = string.Empty;

    public ICollection<KycDocument> Documents { get; set; } = new List<KycDocument>();
}
