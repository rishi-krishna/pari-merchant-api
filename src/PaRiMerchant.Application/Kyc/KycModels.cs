using Microsoft.AspNetCore.Http;

namespace PaRiMerchant.Application.Kyc;

public sealed record KycProfileRequest(
    string Status,
    string Name,
    string Pan,
    string DateOfBirth,
    string AadhaarNumber,
    string AccountHolderName,
    string BankName,
    string AccountNumber,
    string Ifsc,
    string Branch,
    string CompanyName,
    string CompanyType,
    string CompanyGst,
    string CompanyAddress,
    string CompanyCity,
    string CompanyState,
    string CompanyPincode,
    string CompanyCountry);

public sealed record KycProfileResponse(
    string Status,
    string Name,
    string MaskedPan,
    string DateOfBirth,
    string AadhaarMasked,
    string AccountHolderName,
    string BankName,
    string MaskedAccountNumber,
    string Ifsc,
    string Branch,
    string CompanyName,
    string CompanyType,
    string CompanyGst,
    string CompanyAddress,
    string CompanyCity,
    string CompanyState,
    string CompanyPincode,
    string CompanyCountry,
    IReadOnlyList<KycDocumentResponse> Documents);

public sealed record KycDocumentUploadRequest(string Kind, IFormFile File);
public sealed record KycDocumentResponse(string Id, string Kind, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedUtc);
