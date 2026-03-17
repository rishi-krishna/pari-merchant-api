using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Beneficiaries;

public sealed class BeneficiaryService(IAppDbContext dbContext, ISensitiveDataProtector protector)
{
    public async Task<IReadOnlyList<BeneficiaryResponse>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var items = await dbContext.Beneficiaries
            .Include(item => item.Contact)
            .Where(item => item.TenantId == tenantId && item.IsActive)
            .OrderByDescending(item => item.CreatedUtc)
            .ToListAsync(cancellationToken);

        return items.Select(item => Map(item, item.Contact)).ToList();
    }

    public Task<BeneficiaryResponse> ValidateAsync(Guid tenantId, ValidateBeneficiaryRequest request, CancellationToken cancellationToken)
        => BuildAsync(tenantId, request.ContactId, request.BankName, request.Ifsc, request.AccountNumber, request.AccountHolderName, request.Branch, request.AccountType, false, cancellationToken);

    public Task<BeneficiaryResponse> CreateAsync(Guid tenantId, CreateBeneficiaryRequest request, CancellationToken cancellationToken)
        => BuildAsync(tenantId, request.ContactId, request.BankName, request.Ifsc, request.AccountNumber, request.AccountHolderName, request.Branch, request.AccountType, true, cancellationToken);

    public async Task DeleteAsync(Guid tenantId, Guid beneficiaryId, CancellationToken cancellationToken)
    {
        var beneficiary = await dbContext.Beneficiaries
            .FirstOrDefaultAsync(item => item.Id == beneficiaryId && item.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Beneficiary not found.");

        beneficiary.IsActive = false;
        beneficiary.Status = BeneficiaryStatus.Disabled;
        beneficiary.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<BeneficiaryResponse> BuildAsync(Guid tenantId, string contactIdRaw, string bankName, string ifsc, string accountNumber, string accountHolderName, string branch, string accountType, bool persist, CancellationToken cancellationToken)
    {
        var contactId = Guid.Parse(contactIdRaw);
        var contact = await dbContext.Contacts
            .FirstOrDefaultAsync(item => item.Id == contactId && item.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Contact not found.");

        if (string.IsNullOrWhiteSpace(accountNumber) || accountNumber.Length < 8)
        {
            throw new InvalidOperationException("Account number is invalid.");
        }

        var beneficiary = new Beneficiary
        {
            TenantId = tenantId,
            ContactId = contact.Id,
            Contact = contact,
            AccountHolderNameCiphertext = protector.Encrypt(accountHolderName),
            AccountHolderSearchToken = accountHolderName.Trim().ToLowerInvariant(),
            AccountNumberCiphertext = protector.Encrypt(accountNumber),
            AccountNumberBlindIndex = protector.ComputeBlindIndex(accountNumber),
            BankName = bankName,
            Ifsc = ifsc,
            Branch = branch,
            AccountType = accountType,
            Status = BeneficiaryStatus.Validated
        };

        if (persist)
        {
            dbContext.Beneficiaries.Add(beneficiary);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Map(beneficiary, contact);
    }

    private BeneficiaryResponse Map(Beneficiary beneficiary, Contact contact) => new(
        beneficiary.Id.ToString(),
        contact.Id.ToString(),
        protector.Decrypt(contact.NameCiphertext),
        protector.Decrypt(contact.PhoneCiphertext),
        protector.Decrypt(beneficiary.AccountHolderNameCiphertext),
        protector.Decrypt(beneficiary.AccountNumberCiphertext),
        beneficiary.BankName,
        beneficiary.Ifsc,
        beneficiary.Branch,
        beneficiary.AccountType,
        beneficiary.Status.ToString());
}
