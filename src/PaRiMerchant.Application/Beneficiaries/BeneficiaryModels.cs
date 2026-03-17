namespace PaRiMerchant.Application.Beneficiaries;

public sealed record ValidateBeneficiaryRequest(string ContactId, string BankName, string Ifsc, string AccountNumber, string AccountHolderName, string Branch, string AccountType);
public sealed record CreateBeneficiaryRequest(string ContactId, string BankName, string Ifsc, string AccountNumber, string AccountHolderName, string Branch, string AccountType);
public sealed record BeneficiaryResponse(string Id, string ContactId, string ContactName, string MaskedPhone, string AccountHolderName, string MaskedAccountNumber, string BankName, string Ifsc, string Branch, string AccountType, string Status);
