using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Kyc;

public sealed class KycService(IAppDbContext dbContext, ISensitiveDataProtector protector, IKycDocumentStorage storage)
{
    public async Task<KycProfileResponse?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.KycProfiles
            .Include(item => item.Documents)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        return profile is null ? null : Map(profile);
    }

    public async Task<KycProfileResponse> UpsertAsync(Guid tenantId, KycProfileRequest request, CancellationToken cancellationToken)
    {
        var profile = await dbContext.KycProfiles
            .Include(item => item.Documents)
            .FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken);

        if (profile is null)
        {
            profile = new KycProfile { TenantId = tenantId };
            dbContext.KycProfiles.Add(profile);
        }

        profile.Status = Enum.TryParse<KycStatus>(request.Status, true, out var status) ? status : KycStatus.Draft;
        profile.NameCiphertext = protector.Encrypt(request.Name);
        profile.PanCiphertext = protector.Encrypt(request.Pan);
        profile.PanBlindIndex = protector.ComputeBlindIndex(request.Pan);
        profile.DateOfBirthCiphertext = protector.Encrypt(request.DateOfBirth);
        profile.AadhaarCiphertext = protector.Encrypt(request.AadhaarNumber);
        profile.AadhaarMasked = request.AadhaarNumber.Length >= 4 ? $"********{request.AadhaarNumber[^4..]}" : "****";
        profile.AccountHolderNameCiphertext = protector.Encrypt(request.AccountHolderName);
        profile.BankName = request.BankName;
        profile.AccountNumberCiphertext = protector.Encrypt(request.AccountNumber);
        profile.AccountNumberBlindIndex = protector.ComputeBlindIndex(request.AccountNumber);
        profile.Ifsc = request.Ifsc;
        profile.Branch = request.Branch;
        profile.CompanyNameCiphertext = protector.Encrypt(request.CompanyName);
        profile.CompanyTypeCiphertext = protector.Encrypt(request.CompanyType);
        profile.CompanyGstCiphertext = protector.Encrypt(request.CompanyGst);
        profile.CompanyAddressCiphertext = protector.Encrypt(request.CompanyAddress);
        profile.CompanyCityCiphertext = protector.Encrypt(request.CompanyCity);
        profile.CompanyStateCiphertext = protector.Encrypt(request.CompanyState);
        profile.CompanyPincodeCiphertext = protector.Encrypt(request.CompanyPincode);
        profile.CompanyCountryCiphertext = protector.Encrypt(request.CompanyCountry);
        profile.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(profile);
    }

    public async Task<KycDocumentResponse> UploadDocumentAsync(Guid tenantId, KycDocumentUploadRequest request, CancellationToken cancellationToken)
    {
        var profile = await dbContext.KycProfiles.FirstOrDefaultAsync(item => item.TenantId == tenantId, cancellationToken)
            ?? throw new InvalidOperationException("Create a KYC profile before uploading documents.");

        await using var stream = request.File.OpenReadStream();
        var stored = await storage.SaveAsync(request.File.FileName, request.File.ContentType, stream, cancellationToken);

        var document = new KycDocument
        {
            TenantId = tenantId,
            KycProfileId = profile.Id,
            Kind = request.Kind,
            FileName = request.File.FileName,
            ContentType = request.File.ContentType,
            StoragePath = stored.StoragePath,
            Sha256Hash = stored.Sha256Hash,
            SizeBytes = stored.SizeBytes
        };

        dbContext.KycDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new KycDocumentResponse(document.Id.ToString(), document.Kind, document.FileName, document.ContentType, document.SizeBytes, document.CreatedUtc);
    }

    public async Task<KycDocumentResponse> GetDocumentAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await dbContext.KycDocuments
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == documentId, cancellationToken)
            ?? throw new KeyNotFoundException("KYC document not found.");

        return new KycDocumentResponse(document.Id.ToString(), document.Kind, document.FileName, document.ContentType, document.SizeBytes, document.CreatedUtc);
    }

    private KycProfileResponse Map(KycProfile profile) => new(
        profile.Status.ToString(),
        protector.Decrypt(profile.NameCiphertext),
        Masking.Pan(protector.Decrypt(profile.PanCiphertext)),
        protector.Decrypt(profile.DateOfBirthCiphertext),
        profile.AadhaarMasked,
        protector.Decrypt(profile.AccountHolderNameCiphertext),
        profile.BankName,
        Masking.AccountNumber(protector.Decrypt(profile.AccountNumberCiphertext)),
        profile.Ifsc,
        profile.Branch,
        protector.Decrypt(profile.CompanyNameCiphertext),
        protector.Decrypt(profile.CompanyTypeCiphertext),
        protector.Decrypt(profile.CompanyGstCiphertext),
        protector.Decrypt(profile.CompanyAddressCiphertext),
        protector.Decrypt(profile.CompanyCityCiphertext),
        protector.Decrypt(profile.CompanyStateCiphertext),
        protector.Decrypt(profile.CompanyPincodeCiphertext),
        protector.Decrypt(profile.CompanyCountryCiphertext),
        profile.Documents.Select(document => new KycDocumentResponse(document.Id.ToString(), document.Kind, document.FileName, document.ContentType, document.SizeBytes, document.CreatedUtc)).ToList());
}
