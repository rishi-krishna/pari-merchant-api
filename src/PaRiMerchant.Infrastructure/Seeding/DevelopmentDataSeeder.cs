using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;
using PaRiMerchant.Infrastructure.Persistence;

namespace PaRiMerchant.Infrastructure.Seeding;

public sealed class DevelopmentDataSeeder(
    AppDbContext dbContext,
    ISensitiveDataProtector protector,
    IPasswordHasher passwordHasher)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Tenants.AnyAsync(cancellationToken))
        {
            return;
        }

        var tenant = new Tenant { Name = "PaRi Merchant Portal", Code = "pari-demo" };
        var profile = new MerchantProfile
        {
            Tenant = tenant,
            MerchantCode = "MER1001",
            DisplayNameCiphertext = protector.Encrypt("Demo Merchant"),
            CompanyNameCiphertext = protector.Encrypt("Demo Merchant Private Limited"),
            KycStatus = KycStatus.Approved
        };

        var user = new MerchantUser
        {
            Tenant = tenant,
            MerchantProfile = profile,
            DisplayNameCiphertext = protector.Encrypt("Demo Merchant"),
            EmailCiphertext = protector.Encrypt("merchant@example.test"),
            EmailBlindIndex = protector.ComputeBlindIndex("merchant@example.test"),
            PhoneCiphertext = protector.Encrypt("9000000000"),
            PhoneBlindIndex = protector.ComputeBlindIndex("9000000000"),
            PasswordHash = passwordHasher.Hash("Demo@1234"),
            MpinHash = passwordHasher.Hash("654321"),
            Role = UserRole.MerchantAdmin
        };

        dbContext.Tenants.Add(tenant);
        dbContext.MerchantProfiles.Add(profile);
        dbContext.MerchantUsers.Add(user);
        dbContext.LedgerEntries.Add(new LedgerEntry
        {
            TenantId = tenant.Id,
            EntryType = LedgerEntryType.Credit,
            Amount = 20000m,
            Currency = "INR",
            Description = "Opening balance"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
