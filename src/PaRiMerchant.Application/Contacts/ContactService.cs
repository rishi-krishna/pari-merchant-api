using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Contacts;

public sealed class ContactService(IAppDbContext dbContext, ISensitiveDataProtector protector)
{
    public async Task<IReadOnlyList<ContactResponse>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var contacts = await dbContext.Contacts
            .Where(contact => contact.TenantId == tenantId)
            .OrderByDescending(contact => contact.CreatedUtc)
            .ToListAsync(cancellationToken);

        return contacts.Select(Map).ToList();
    }

    public async Task<ContactResponse> SearchByPhoneAsync(Guid tenantId, string phone, CancellationToken cancellationToken)
    {
        var phoneIndex = protector.ComputeBlindIndex(NormalizeDigits(phone));
        var contact = await dbContext.Contacts
            .FirstOrDefaultAsync(candidate => candidate.TenantId == tenantId && candidate.PhoneBlindIndex == phoneIndex, cancellationToken)
            ?? throw new KeyNotFoundException("Contact not found.");

        return Map(contact);
    }

    public async Task<ContactResponse> CreateAsync(Guid tenantId, UpsertContactRequest request, CancellationToken cancellationToken)
    {
        var contact = new Contact
        {
            TenantId = tenantId,
            NameCiphertext = protector.Encrypt(request.Name),
            NameSearchToken = request.Name.Trim().ToLowerInvariant(),
            EmailCiphertext = protector.Encrypt(request.Email),
            EmailBlindIndex = protector.ComputeBlindIndex(request.Email.Trim().ToLowerInvariant()),
            PhoneCiphertext = protector.Encrypt(NormalizeDigits(request.Phone)),
            PhoneBlindIndex = protector.ComputeBlindIndex(NormalizeDigits(request.Phone)),
            CityCiphertext = protector.Encrypt(request.City),
            Status = ParseStatus(request.Status)
        };

        dbContext.Contacts.Add(contact);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    public async Task<ContactResponse> UpdateAsync(Guid tenantId, Guid contactId, UpsertContactRequest request, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts.FirstOrDefaultAsync(candidate => candidate.Id == contactId && candidate.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Contact not found.");

        contact.NameCiphertext = protector.Encrypt(request.Name);
        contact.NameSearchToken = request.Name.Trim().ToLowerInvariant();
        contact.EmailCiphertext = protector.Encrypt(request.Email);
        contact.EmailBlindIndex = protector.ComputeBlindIndex(request.Email.Trim().ToLowerInvariant());
        contact.PhoneCiphertext = protector.Encrypt(NormalizeDigits(request.Phone));
        contact.PhoneBlindIndex = protector.ComputeBlindIndex(NormalizeDigits(request.Phone));
        contact.CityCiphertext = protector.Encrypt(request.City);
        contact.Status = ParseStatus(request.Status);
        contact.UpdatedUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    private ContactResponse Map(Contact contact)
    {
        var email = protector.Decrypt(contact.EmailCiphertext);
        var phone = protector.Decrypt(contact.PhoneCiphertext);
        return new ContactResponse(
            contact.Id.ToString(),
            protector.Decrypt(contact.NameCiphertext),
            email,
            phone,
            protector.Decrypt(contact.CityCiphertext),
            contact.Status.ToString());
    }

    private static ContactStatus ParseStatus(string value) =>
        Enum.TryParse<ContactStatus>(value, true, out var status) ? status : ContactStatus.Active;

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());
}
