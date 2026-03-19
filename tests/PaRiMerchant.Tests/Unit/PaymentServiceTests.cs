using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Application.Payments;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;
using PaRiMerchant.Infrastructure.Persistence;
using Xunit;

namespace PaRiMerchant.Tests.Unit;

public sealed class PaymentServiceTests
{
    [Fact]
    public async Task CreatePayoutAsync_Throws_When_Beneficiary_Is_Not_Validated()
    {
        await using var dbContext = BuildContext();
        var tenantId = Guid.NewGuid();
        var contact = new Contact { TenantId = tenantId, NameCiphertext = "enc:Sample User", NameSearchToken = "sample user", EmailCiphertext = "enc:sample@example.com", EmailBlindIndex = "email", PhoneCiphertext = "enc:9000000000", PhoneBlindIndex = "phone", CityCiphertext = "enc:Sample City" };
        var beneficiary = new Beneficiary { TenantId = tenantId, Contact = contact, AccountHolderNameCiphertext = "enc:Sample Beneficiary", AccountHolderSearchToken = "sample beneficiary", AccountNumberCiphertext = "enc:12345678", AccountNumberBlindIndex = "acct", BankName = "Sample Bank", Ifsc = "SAMP000001", Branch = "Main Branch", Status = BeneficiaryStatus.PendingValidation };
        dbContext.Contacts.Add(contact);
        dbContext.Beneficiaries.Add(beneficiary);
        await dbContext.SaveChangesAsync();

        var service = new PaymentService(dbContext, new FakeProtector(), Options.Create(new CashfreeOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePayoutAsync(tenantId, new CreatePayoutRequest(beneficiary.Id.ToString(), 100m, "INR", "Test payout"), CancellationToken.None));
    }

    [Fact]
    public async Task CreatePayoutAsync_Throws_When_Wallet_Balance_Is_Insufficient()
    {
        await using var dbContext = BuildContext();
        var tenantId = Guid.NewGuid();
        var contact = new Contact { TenantId = tenantId, NameCiphertext = "enc:Sample User", NameSearchToken = "sample user", EmailCiphertext = "enc:sample@example.com", EmailBlindIndex = "email", PhoneCiphertext = "enc:9000000000", PhoneBlindIndex = "phone", CityCiphertext = "enc:Sample City" };
        var beneficiary = new Beneficiary { TenantId = tenantId, Contact = contact, AccountHolderNameCiphertext = "enc:Sample Beneficiary", AccountHolderSearchToken = "sample beneficiary", AccountNumberCiphertext = "enc:12345678", AccountNumberBlindIndex = "acct", BankName = "Sample Bank", Ifsc = "SAMP000001", Branch = "Main Branch", Status = BeneficiaryStatus.Validated };
        dbContext.Contacts.Add(contact);
        dbContext.Beneficiaries.Add(beneficiary);
        await dbContext.SaveChangesAsync();

        var service = new PaymentService(dbContext, new FakeProtector(), Options.Create(new CashfreeOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePayoutAsync(tenantId, new CreatePayoutRequest(beneficiary.Id.ToString(), 100m, "INR", "Test payout"), CancellationToken.None));
    }

    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private sealed class FakeProtector : ISensitiveDataProtector
    {
        public string Encrypt(string plaintext) => $"enc:{plaintext}";
        public string Decrypt(string ciphertext) => ciphertext.Replace("enc:", string.Empty);
        public string ComputeBlindIndex(string plaintext) => plaintext;
    }
}
