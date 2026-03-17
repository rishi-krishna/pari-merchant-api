using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Domain.Entities;

namespace PaRiMerchant.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<MerchantProfile> MerchantProfiles { get; }
    DbSet<MerchantUser> MerchantUsers { get; }
    DbSet<RefreshSession> RefreshSessions { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<Beneficiary> Beneficiaries { get; }
    DbSet<KycProfile> KycProfiles { get; }
    DbSet<KycDocument> KycDocuments { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<TransactionEvent> TransactionEvents { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<AuditEvent> AuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
