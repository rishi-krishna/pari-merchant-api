using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<MerchantProfile> MerchantProfiles => Set<MerchantProfile>();
    public DbSet<MerchantUser> MerchantUsers => Set<MerchantUser>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();
    public DbSet<KycProfile> KycProfiles => Set<KycProfile>();
    public DbSet<KycDocument> KycDocuments => Set<KycDocument>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionEvent> TransactionEvents => Set<TransactionEvent>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureEnums(modelBuilder);

        modelBuilder.Entity<Tenant>().HasIndex(item => item.Code).IsUnique();
        modelBuilder.Entity<MerchantProfile>().HasIndex(item => item.MerchantCode).IsUnique();
        modelBuilder.Entity<MerchantUser>().HasIndex(item => new { item.TenantId, item.PhoneBlindIndex }).IsUnique();
        modelBuilder.Entity<Contact>().HasIndex(item => new { item.TenantId, item.PhoneBlindIndex }).IsUnique();
        modelBuilder.Entity<Contact>().HasIndex(item => new { item.TenantId, item.EmailBlindIndex });
        modelBuilder.Entity<Beneficiary>().HasIndex(item => new { item.TenantId, item.AccountNumberBlindIndex });
        modelBuilder.Entity<KycProfile>().HasIndex(item => new { item.TenantId, item.PanBlindIndex });
        modelBuilder.Entity<RefreshSession>().HasIndex(item => item.TokenHash).IsUnique();
        modelBuilder.Entity<CardCollectionDetail>().HasKey(item => item.TransactionId);
        modelBuilder.Entity<WalletTopupDetail>().HasKey(item => item.TransactionId);
        modelBuilder.Entity<PayoutDetail>().HasKey(item => item.TransactionId);

        modelBuilder.Entity<Transaction>()
            .HasMany(item => item.Events)
            .WithOne(item => item.Transaction)
            .HasForeignKey(item => item.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transaction>()
            .HasMany(item => item.LedgerEntries)
            .WithOne(item => item.Transaction)
            .HasForeignKey(item => item.TransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Transaction>()
            .HasOne(item => item.CardCollection)
            .WithOne(item => item.Transaction)
            .HasForeignKey<CardCollectionDetail>(item => item.TransactionId);

        modelBuilder.Entity<Transaction>()
            .HasOne(item => item.WalletTopup)
            .WithOne(item => item.Transaction)
            .HasForeignKey<WalletTopupDetail>(item => item.TransactionId);

        modelBuilder.Entity<Transaction>()
            .HasOne(item => item.Payout)
            .WithOne(item => item.Transaction)
            .HasForeignKey<PayoutDetail>(item => item.TransactionId);

        modelBuilder.Entity<PayoutDetail>()
            .HasOne(item => item.Beneficiary)
            .WithMany()
            .HasForeignKey(item => item.BeneficiaryId)
            .OnDelete(DeleteBehavior.Restrict);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureEnums(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MerchantUser>().Property(item => item.Role).HasConversion<string>();
        modelBuilder.Entity<Contact>().Property(item => item.Status).HasConversion<string>();
        modelBuilder.Entity<Beneficiary>().Property(item => item.Status).HasConversion<string>();
        modelBuilder.Entity<KycProfile>().Property(item => item.Status).HasConversion<string>();
        modelBuilder.Entity<Transaction>().Property(item => item.TransactionType).HasConversion<string>();
        modelBuilder.Entity<Transaction>().Property(item => item.Status).HasConversion<string>();
        modelBuilder.Entity<TransactionEvent>().Property(item => item.Status).HasConversion<string>();
        modelBuilder.Entity<LedgerEntry>().Property(item => item.EntryType).HasConversion<string>();
    }
}
