using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Payments;

public sealed class PaymentService(
    IAppDbContext dbContext,
    ISensitiveDataProtector protector,
    IOptions<CashfreeOptions> cashfreeOptions)
{
    private readonly CashfreeOptions cashfreeOptions = cashfreeOptions.Value;

    public async Task<PaymentTransactionResponse> InitiateCollectionAsync(Guid tenantId, InitiateCollectionRequest request, CancellationToken cancellationToken)
    {
        var fee = Math.Round(request.Amount * 0.025m, 2);
        var transaction = BuildBaseTransaction(tenantId, TransactionType.CardCollection, request.Amount, fee, request.Currency, request.Description);
        transaction.Status = TransactionStatus.PendingProvider;
        transaction.SettlementStatus = "AwaitingProvider";
        transaction.CardCollection = new CardCollectionDetail
        {
            TransactionId = transaction.Id,
            CardBrand = request.CardBrand,
            MaskedCardNumber = request.MaskedCardNumber,
            ProviderTokenReference = request.ProviderTokenReference,
            CustomerNameCiphertext = protector.Encrypt(request.CustomerName)
        };

        transaction.Events.Add(BuildEvent(transaction, "collection_initiated", transaction.Status, "Customer collection initiated."));
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(transaction);
    }

    public async Task<PaymentTransactionResponse> InitiateSelfTopupAsync(Guid tenantId, InitiateSelfTopupRequest request, CancellationToken cancellationToken)
    {
        var fee = Math.Round(request.Amount * 0.01m, 2);
        var transaction = BuildBaseTransaction(tenantId, TransactionType.SelfTopup, request.Amount, fee, request.Currency, request.Description);
        transaction.Status = TransactionStatus.PendingProvider;
        transaction.SettlementStatus = "AwaitingProvider";
        transaction.WalletTopup = new WalletTopupDetail
        {
            TransactionId = transaction.Id,
            CardBrand = request.CardBrand,
            MaskedCardNumber = request.MaskedCardNumber,
            ProviderTokenReference = request.ProviderTokenReference
        };

        transaction.Events.Add(BuildEvent(transaction, "self_topup_initiated", transaction.Status, "Merchant self-topup initiated."));
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(transaction);
    }

    public async Task<PaymentTransactionResponse> CreatePayoutAsync(Guid tenantId, CreatePayoutRequest request, CancellationToken cancellationToken)
    {
        var beneficiaryId = Guid.Parse(request.BeneficiaryId);
        var beneficiary = await dbContext.Beneficiaries
            .Include(item => item.Contact)
            .FirstOrDefaultAsync(item => item.Id == beneficiaryId && item.TenantId == tenantId && item.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("Beneficiary not found.");

        if (beneficiary.Status != BeneficiaryStatus.Validated)
        {
            throw new InvalidOperationException("Beneficiary must be validated before payout.");
        }

        var summary = await GetWalletSnapshotAsync(tenantId, cancellationToken);
        if (summary.AvailableBalance < request.Amount)
        {
            throw new InvalidOperationException("Insufficient wallet balance.");
        }

        var transaction = BuildBaseTransaction(tenantId, TransactionType.Payout, request.Amount, 0m, request.Currency, request.Purpose);
        transaction.BeneficiaryId = beneficiary.Id;
        transaction.ContactId = beneficiary.ContactId;
        transaction.Status = TransactionStatus.Processing;
        transaction.SettlementStatus = "HoldPlaced";
        transaction.Payout = new PayoutDetail
        {
            TransactionId = transaction.Id,
            BeneficiaryId = beneficiary.Id,
            Purpose = request.Purpose,
            BankReference = string.Empty
        };

        transaction.Events.Add(BuildEvent(transaction, "payout_created", transaction.Status, "Payout created and funds held."));
        transaction.LedgerEntries.Add(new LedgerEntry
        {
            TenantId = tenantId,
            TransactionId = transaction.Id,
            EntryType = LedgerEntryType.Hold,
            Amount = request.Amount,
            Currency = request.Currency,
            Description = "Payout hold"
        });

        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(transaction);
    }

    public async Task ProcessCashfreeFormWebhookAsync(CashfreeWebhookRequest request, CancellationToken cancellationToken)
    {
        EnsureValidCashfreeSignature(request);

        using var document = JsonDocument.Parse(request.RawBody);
        var root = document.RootElement;
        var eventType = root.GetProperty("type").GetString() ?? string.Empty;

        if (!string.Equals(eventType, "PAYMENT_FORM_ORDER_WEBHOOK", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported Cashfree webhook event.");
        }

        var data = root.GetProperty("data");
        var form = data.GetProperty("form");
        var order = data.GetProperty("order");
        var customerDetails = order.GetProperty("customer_details");

        var orderId = order.GetProperty("order_id").GetString() ?? throw new InvalidOperationException("Cashfree webhook is missing order_id.");
        var orderAmount = order.GetProperty("order_amount").GetDecimal();
        var orderStatus = order.GetProperty("order_status").GetString() ?? "UNKNOWN";
        var providerTransactionId = order.TryGetProperty("transaction_id", out var transactionIdElement)
            ? transactionIdElement.ToString()
            : string.Empty;
        var customerPhone = customerDetails.TryGetProperty("customer_phone", out var phoneElement)
            ? NormalizeDigits(phoneElement.GetString() ?? string.Empty)
            : string.Empty;
        var customerEmail = customerDetails.TryGetProperty("customer_email", out var emailElement)
            ? emailElement.GetString() ?? string.Empty
            : string.Empty;
        var customerName = customerDetails.TryGetProperty("customer_name", out var nameElement)
            ? nameElement.GetString() ?? string.Empty
            : string.Empty;
        var formId = form.TryGetProperty("form_id", out var formIdElement) ? formIdElement.GetString() ?? string.Empty : string.Empty;
        var formUrl = form.TryGetProperty("form_url", out var formUrlElement) ? formUrlElement.GetString() ?? string.Empty : string.Empty;
        var currency = form.TryGetProperty("form_currency", out var currencyElement) ? currencyElement.GetString() ?? "INR" : "INR";

        var contact = await FindContactByPhoneAsync(customerPhone, cancellationToken);
        var tenantId = await ResolveTenantIdAsync(contact, cancellationToken);
        var fee = Math.Round(orderAmount * 0.025m, 2);
        var normalizedStatus = MapCashfreeStatus(orderStatus);

        var transaction = await dbContext.Transactions
            .Include(item => item.CardCollection)
            .Include(item => item.Events)
            .Include(item => item.LedgerEntries)
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId
                    && item.TransactionType == TransactionType.CardCollection
                    && item.ProviderReference == orderId,
                cancellationToken);

        var isNewTransaction = transaction is null;
        if (transaction is null)
        {
            transaction = BuildBaseTransaction(
                tenantId,
                TransactionType.CardCollection,
                orderAmount,
                fee,
                currency,
                $"Cashfree payment form collection{(string.IsNullOrWhiteSpace(customerName) ? string.Empty : $" from {customerName}")}");

            transaction.ProviderReference = orderId;
            transaction.CardCollection = new CardCollectionDetail
            {
                TransactionId = transaction.Id,
                CardBrand = "Cashfree Payment Form",
                MaskedCardNumber = string.Empty,
                ProviderTokenReference = providerTransactionId,
                CustomerNameCiphertext = protector.Encrypt(customerName)
            };
        }

        transaction.ContactId = contact?.Id;
        transaction.Amount = orderAmount;
        transaction.FeeAmount = fee;
        transaction.NetAmount = orderAmount - fee;
        transaction.Currency = string.IsNullOrWhiteSpace(currency) ? "INR" : currency;
        transaction.Status = normalizedStatus;
        transaction.SettlementStatus = MapSettlementStatus(orderStatus);
        transaction.FailureCode = normalizedStatus == TransactionStatus.Failed ? orderStatus : string.Empty;
        transaction.Description = $"Cashfree payment form{(string.IsNullOrWhiteSpace(formId) ? string.Empty : $" {formId}")}";
        transaction.ProviderReference = orderId;

        if (transaction.CardCollection is null)
        {
            transaction.CardCollection = new CardCollectionDetail
            {
                TransactionId = transaction.Id
            };
        }

        transaction.CardCollection.CardBrand = "Cashfree Payment Form";
        transaction.CardCollection.MaskedCardNumber = string.Empty;
        transaction.CardCollection.ProviderTokenReference = providerTransactionId;
        transaction.CardCollection.CustomerNameCiphertext = protector.Encrypt(customerName);

        var notes =
            $"Cashfree form webhook received. Status={orderStatus}, OrderId={orderId}, TransactionId={providerTransactionId}, FormId={formId}, ContactMatched={(contact is not null ? "yes" : "no")}, Email={customerEmail}, Phone={customerPhone}, FormUrl={formUrl}";
        transaction.Events.Add(BuildEvent(transaction, "cashfree_form_webhook", normalizedStatus, notes, request.RawBody));

        if (normalizedStatus == TransactionStatus.Succeeded && !transaction.LedgerEntries.Any(item => item.EntryType == LedgerEntryType.Credit))
        {
            transaction.LedgerEntries.Add(new LedgerEntry
            {
                TenantId = tenantId,
                TransactionId = transaction.Id,
                EntryType = LedgerEntryType.Credit,
                Amount = transaction.NetAmount,
                Currency = transaction.Currency,
                Description = "Cashfree collection credit"
            });

            if (transaction.FeeAmount > 0)
            {
                transaction.LedgerEntries.Add(new LedgerEntry
                {
                    TenantId = tenantId,
                    TransactionId = transaction.Id,
                    EntryType = LedgerEntryType.Fee,
                    Amount = transaction.FeeAmount,
                    Currency = transaction.Currency,
                    Description = "Cashfree collection fee"
                });
            }
        }

        if (isNewTransaction)
        {
            dbContext.Transactions.Add(transaction);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<LoadMoneyResultResponse> GetLoadMoneyResultAsync(Guid tenantId, string? orderId, string? providerTransactionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(providerTransactionId))
        {
            return new LoadMoneyResultResponse(false, "unknown", "No payment reference was provided.", null, null, null, null, "INR", null, null, null);
        }

        var query = dbContext.Transactions
            .Include(item => item.CardCollection)
            .Where(item => item.TenantId == tenantId && item.TransactionType == TransactionType.CardCollection);

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            query = query.Where(item => item.ProviderReference == orderId);
        }
        else
        {
            query = query.Where(item => item.CardCollection != null && item.CardCollection.ProviderTokenReference == providerTransactionId);
        }

        var transaction = await query
            .OrderByDescending(item => item.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (transaction is null)
        {
            return new LoadMoneyResultResponse(
                false,
                "pending",
                "Payment was submitted. Waiting for Cashfree confirmation.",
                null,
                orderId,
                providerTransactionId,
                null,
                "INR",
                null,
                null,
                null);
        }

        return new LoadMoneyResultResponse(
            true,
            ToPublicStatus(transaction.Status),
            BuildResultMessage(transaction.Status),
            transaction.Id.ToString(),
            transaction.ProviderReference,
            transaction.CardCollection?.ProviderTokenReference,
            transaction.Amount,
            transaction.Currency,
            transaction.ExternalReference,
            transaction.Description,
            transaction.CreatedUtc);
    }

    private async Task<(decimal AvailableBalance, decimal HeldBalance)> GetWalletSnapshotAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.LedgerEntries.Where(item => item.TenantId == tenantId).ToListAsync(cancellationToken);
        decimal credits = entries.Where(item => item.EntryType is LedgerEntryType.Credit or LedgerEntryType.Reversal).Sum(item => item.Amount);
        decimal debits = entries.Where(item => item.EntryType is LedgerEntryType.Debit or LedgerEntryType.Fee).Sum(item => item.Amount);
        decimal holds = entries.Where(item => item.EntryType == LedgerEntryType.Hold).Sum(item => item.Amount);
        return (credits - debits - holds, holds);
    }

    private async Task<Contact?> FindContactByPhoneAsync(string customerPhone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerPhone))
        {
            return null;
        }

        var phoneBlindIndex = protector.ComputeBlindIndex(customerPhone);
        return await dbContext.Contacts.FirstOrDefaultAsync(item => item.PhoneBlindIndex == phoneBlindIndex, cancellationToken);
    }

    private async Task<Guid> ResolveTenantIdAsync(Contact? contact, CancellationToken cancellationToken)
    {
        if (contact is not null)
        {
            return contact.TenantId;
        }

        var tenantId = await dbContext.Tenants
            .OrderBy(item => item.CreatedUtc)
            .Select(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("No tenant found for Cashfree webhook processing.");
        }

        return tenantId;
    }

    private void EnsureValidCashfreeSignature(CashfreeWebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(cashfreeOptions.WebhookSecret))
        {
            throw new InvalidOperationException("Cashfree webhook secret is not configured.");
        }

        var computed = ComputeCashfreeSignature(request.Timestamp, request.RawBody, cashfreeOptions.WebhookSecret);
        if (!FixedEquals(computed, request.Signature))
        {
            throw new UnauthorizedAccessException("Invalid Cashfree webhook signature.");
        }
    }

    private static string ComputeCashfreeSignature(string timestamp, string rawBody, string secret)
    {
        var payload = Encoding.UTF8.GetBytes($"{timestamp}{rawBody}");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(payload));
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left.Trim());
        var rightBytes = Encoding.UTF8.GetBytes(right.Trim());
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static TransactionStatus MapCashfreeStatus(string status) =>
        status.Trim().ToUpperInvariant() switch
        {
            "PAID" => TransactionStatus.Succeeded,
            "FAILED" or "FAILURE" or "CANCELLED" or "TERMINATED" or "EXPIRED" => TransactionStatus.Failed,
            _ => TransactionStatus.PendingProvider
        };

    private static string MapSettlementStatus(string status) =>
        status.Trim().ToUpperInvariant() switch
        {
            "PAID" => "Paid",
            "FAILED" or "FAILURE" or "CANCELLED" or "TERMINATED" or "EXPIRED" => "Failed",
            _ => "AwaitingProvider"
        };

    private static string ToPublicStatus(TransactionStatus status) =>
        status switch
        {
            TransactionStatus.Succeeded => "success",
            TransactionStatus.Failed => "failed",
            _ => "pending"
        };

    private static string BuildResultMessage(TransactionStatus status) =>
        status switch
        {
            TransactionStatus.Succeeded => "Payment received and synced successfully.",
            TransactionStatus.Failed => "Payment was not completed successfully.",
            _ => "Payment is still waiting for confirmation."
        };

    private static Transaction BuildBaseTransaction(Guid tenantId, TransactionType type, decimal amount, decimal feeAmount, string currency, string description) =>
        new()
        {
            TenantId = tenantId,
            TransactionType = type,
            Amount = amount,
            FeeAmount = feeAmount,
            NetAmount = amount - feeAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "INR" : currency,
            Description = description,
            ExternalReference = $"TRX-{Guid.NewGuid():N}"[..16],
            ProviderReference = string.Empty
        };

    private static TransactionEvent BuildEvent(Transaction transaction, string eventType, TransactionStatus status, string notes, string payloadJson = "") =>
        new()
        {
            TransactionId = transaction.Id,
            EventType = eventType,
            Status = status,
            Notes = notes,
            PayloadJson = payloadJson
        };

    private static PaymentTransactionResponse Map(Transaction transaction) =>
        new(transaction.Id.ToString(), transaction.TransactionType.ToString(), transaction.Status.ToString(), transaction.Amount, transaction.FeeAmount, transaction.NetAmount, transaction.Currency, transaction.Description, transaction.ExternalReference, transaction.ProviderReference);

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());
}
