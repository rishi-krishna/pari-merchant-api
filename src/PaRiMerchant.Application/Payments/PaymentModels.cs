namespace PaRiMerchant.Application.Payments;

public sealed record InitiateCollectionRequest(decimal Amount, string Currency, string CustomerName, string CardBrand, string MaskedCardNumber, string ProviderTokenReference, string Description);
public sealed record InitiateSelfTopupRequest(decimal Amount, string Currency, string CardBrand, string MaskedCardNumber, string ProviderTokenReference, string Description);
public sealed record CreatePayoutRequest(string BeneficiaryId, decimal Amount, string Currency, string Purpose);
public sealed record CreateCashfreeCheckoutOrderRequest(string ContactId, decimal Amount, string Currency);
public sealed record CashfreeCheckoutOrderResponse(string OrderId, string CfOrderId, string PaymentSessionId, string Reference);
public sealed record PaymentTransactionResponse(string TransactionId, string TransactionType, string Status, decimal Amount, decimal FeeAmount, decimal NetAmount, string Currency, string Description, string ExternalReference, string ProviderReference);
public sealed record LoadMoneyResultResponse(bool Found, string Status, string Message, string? TransactionId, string? OrderId, string? ProviderTransactionId, decimal? Amount, string Currency, string? Reference, string? Description, DateTimeOffset? CreatedUtc);
public sealed record CashfreeWebhookRequest(string RawBody, string Signature, string Timestamp);
