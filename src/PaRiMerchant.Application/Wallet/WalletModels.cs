namespace PaRiMerchant.Application.Wallet;

public sealed record WalletSummaryResponse(decimal AvailableBalance, decimal HeldBalance, string Currency);
public sealed record LedgerEntryResponse(string Id, string EntryType, decimal Amount, string Currency, string Description, DateTimeOffset CreatedUtc, string? TransactionId);
