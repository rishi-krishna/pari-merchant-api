namespace PaRiMerchant.Application.Transactions;

public sealed record TransactionResponse(string Id, string TransactionType, string Status, decimal Amount, decimal FeeAmount, decimal NetAmount, string Currency, string ExternalReference, string ProviderReference, string Description, string SettlementStatus, DateTimeOffset CreatedUtc);
public sealed record TransactionEventResponse(string Id, string EventType, string Status, string Notes, DateTimeOffset CreatedUtc);
