using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;
using PaRiMerchant.Domain.Enums;

namespace PaRiMerchant.Application.Payments;

public sealed class PaymentService(
    IAppDbContext dbContext,
    ISensitiveDataProtector protector,
    IHttpClientFactory httpClientFactory,
    IOptions<CashfreeOptions> cashfreeOptions)
{
    private static readonly JsonSerializerOptions CashfreeJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly CashfreeOptions cashfreeOptions = cashfreeOptions.Value;

    public async Task<CashfreeCheckoutOrderResponse> CreateCashfreeCheckoutOrderAsync(
        Guid tenantId,
        Guid userId,
        CreateCashfreeCheckoutOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCashfreeApiConfigured();

        if (request.Amount < 1m)
        {
            throw new InvalidOperationException("Amount must be greater than zero.");
        }

        if (!Guid.TryParse(request.ContactId, out var contactId))
        {
            throw new InvalidOperationException("Contact ID is invalid.");
        }

        var contact = await dbContext.Contacts
            .FirstOrDefaultAsync(item => item.Id == contactId && item.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Contact not found.");

        var orderId = BuildCashfreeOrderId();
        var localReference = BuildLocalReference();
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "INR" : request.Currency.Trim().ToUpperInvariant();
        var customerName = protector.Decrypt(contact.NameCiphertext);
        var customerEmail = protector.Decrypt(contact.EmailCiphertext);
        var customerPhone = protector.Decrypt(contact.PhoneCiphertext);

        var payload = new
        {
            order_id = orderId,
            order_amount = request.Amount,
            order_currency = currency,
            customer_details = new
            {
                customer_id = contact.Id.ToString("N"),
                customer_name = customerName,
                customer_email = customerEmail,
                customer_phone = customerPhone
            },
            order_meta = BuildOrderMeta(orderId),
            order_note = "Pari load money checkout",
            order_tags = new
            {
                tenant_id = tenantId.ToString(),
                user_id = userId.ToString(),
                contact_id = contact.Id.ToString(),
                local_reference = localReference
            }
        };

        using var response = await SendCashfreeAsync(HttpMethod.Post, "orders", payload, cancellationToken);
        var providerOrder = await ParseCashfreeOrderResponseAsync(response, cancellationToken);
        await SavePendingCashfreeCheckoutTransactionAsync(
            tenantId,
            contact,
            request.Amount,
            currency,
            orderId,
            localReference,
            providerOrder.CfOrderId,
            customerName,
            cancellationToken);
        return new CashfreeCheckoutOrderResponse(providerOrder.OrderId, providerOrder.CfOrderId, providerOrder.PaymentSessionId, localReference);
    }

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

    public async Task ProcessCashfreeWebhookAsync(CashfreeWebhookRequest request, CancellationToken cancellationToken)
    {
        EnsureValidCashfreeSignature(request);

        using var document = JsonDocument.Parse(request.RawBody);
        var root = document.RootElement;
        var eventType = root.GetProperty("type").GetString() ?? string.Empty;

        switch (eventType.Trim().ToUpperInvariant())
        {
            case "PAYMENT_SUCCESS_WEBHOOK":
                await ProcessCashfreePaymentWebhookAsync(root, request.RawBody, cancellationToken);
                return;
            case "PAYMENT_FAILED_WEBHOOK":
            case "PAYMENT_USER_DROPPED_WEBHOOK":
                await ProcessCashfreeTerminalWebhookAsync(root, request.RawBody, cancellationToken);
                return;
            case "PAYMENT_FORM_ORDER_WEBHOOK":
                await ProcessCashfreeFormWebhookAsync(root, request.RawBody, cancellationToken);
                return;
            default:
                throw new InvalidOperationException("Unsupported Cashfree webhook event.");
        }
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

        if (transaction is not null)
        {
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

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            try
            {
                var providerOrder = await FetchCashfreeOrderAsync(orderId, cancellationToken);
                if (providerOrder is not null)
                {
                    return providerOrder.OrderStatus.Trim().ToUpperInvariant() switch
                    {
                        "PAID" => new LoadMoneyResultResponse(
                            false,
                            "pending",
                            "Payment completed on Cashfree. Waiting for Pari confirmation.",
                            null,
                            providerOrder.OrderId,
                            providerTransactionId,
                            providerOrder.OrderAmount,
                            providerOrder.OrderCurrency,
                            null,
                            "Cashfree hosted checkout collection",
                            providerOrder.CreatedAt),
                        "FAILED" or "FAILURE" or "CANCELLED" or "TERMINATED" or "EXPIRED" => new LoadMoneyResultResponse(
                            false,
                            "failed",
                            "Payment was not completed successfully.",
                            null,
                            providerOrder.OrderId,
                            providerTransactionId,
                            providerOrder.OrderAmount,
                            providerOrder.OrderCurrency,
                            null,
                            "Cashfree hosted checkout collection",
                            providerOrder.CreatedAt),
                        _ => new LoadMoneyResultResponse(
                            false,
                            "pending",
                            "Payment was submitted. Waiting for Cashfree confirmation.",
                            null,
                            providerOrder.OrderId,
                            providerTransactionId,
                            providerOrder.OrderAmount,
                            providerOrder.OrderCurrency,
                            null,
                            "Cashfree hosted checkout collection",
                            providerOrder.CreatedAt)
                    };
                }
            }
            catch
            {
                // Fall back to a generic pending state if provider status cannot be fetched yet.
            }
        }

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

    private async Task ProcessCashfreePaymentWebhookAsync(JsonElement root, string rawBody, CancellationToken cancellationToken)
    {
        var data = root.GetProperty("data");
        var order = data.GetProperty("order");
        var payment = data.GetProperty("payment");
        var customerDetails = data.TryGetProperty("customer_details", out var customerElement) ? customerElement : default;
        var paymentGatewayDetails = data.TryGetProperty("payment_gateway_details", out var gatewayElement) ? gatewayElement : default;

        var orderId = order.GetProperty("order_id").GetString() ?? throw new InvalidOperationException("Cashfree webhook is missing order_id.");
        var verifiedOrder = await FetchCashfreeOrderAsync(orderId, cancellationToken)
            ?? throw new InvalidOperationException("Cashfree order could not be verified.");

        if (!string.Equals(verifiedOrder.OrderStatus, "PAID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cashfree order is not marked as PAID yet.");
        }

        var orderTags = ParseOrderTags(order);
        var customerPhone = customerDetails.ValueKind == JsonValueKind.Object
            ? NormalizeDigits(GetString(customerDetails, "customer_phone"))
            : string.Empty;
        var customerEmail = customerDetails.ValueKind == JsonValueKind.Object
            ? GetString(customerDetails, "customer_email")
            : string.Empty;
        var customerName = customerDetails.ValueKind == JsonValueKind.Object
            ? GetString(customerDetails, "customer_name")
            : string.Empty;
        var cfPaymentId = GetString(payment, "cf_payment_id");
        var paymentMessage = GetString(payment, "payment_message");
        var paymentGroup = GetString(payment, "payment_group");
        var gatewayPaymentId = paymentGatewayDetails.ValueKind == JsonValueKind.Object
            ? GetString(paymentGatewayDetails, "gateway_payment_id")
            : string.Empty;
        var bankReference = GetString(payment, "bank_reference");

        var contact = await ResolveContactForCashfreeAsync(orderTags, customerPhone, cancellationToken);
        var tenantId = await ResolveTenantIdForCashfreeAsync(orderTags, contact, cancellationToken);
        var orderAmount = verifiedOrder.OrderAmount;
        var fee = Math.Round(orderAmount * 0.025m, 2);
        var transaction = await dbContext.Transactions
            .Include(item => item.CardCollection)
            .Include(item => item.Events)
            .Include(item => item.LedgerEntries)
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId
                    && item.TransactionType == TransactionType.CardCollection
                    && item.ProviderReference == orderId,
                cancellationToken);

        if (transaction is null)
        {
            transaction = BuildBaseTransaction(
                tenantId,
                TransactionType.CardCollection,
                orderAmount,
                fee,
                verifiedOrder.OrderCurrency,
                BuildCheckoutDescription(paymentGroup, customerName));

            transaction.ProviderReference = orderId;
            transaction.ExternalReference = ResolveExternalReference(orderTags);
            transaction.CardCollection = new CardCollectionDetail
            {
                TransactionId = transaction.Id
            };

            dbContext.Transactions.Add(transaction);
        }

        transaction.ContactId = contact?.Id;
        transaction.Amount = orderAmount;
        transaction.FeeAmount = fee;
        transaction.NetAmount = orderAmount - fee;
        transaction.Currency = verifiedOrder.OrderCurrency;
        transaction.Status = TransactionStatus.Succeeded;
        transaction.SettlementStatus = "Paid";
        transaction.FailureCode = string.Empty;
        transaction.Description = BuildCheckoutDescription(paymentGroup, customerName);
        transaction.ProviderReference = orderId;

        transaction.CardCollection ??= new CardCollectionDetail
        {
            TransactionId = transaction.Id
        };
        transaction.CardCollection.CardBrand = "Cashfree Hosted Checkout";
        transaction.CardCollection.MaskedCardNumber = ExtractMaskedInstrument(payment);
        transaction.CardCollection.ProviderTokenReference = string.IsNullOrWhiteSpace(cfPaymentId) ? gatewayPaymentId : cfPaymentId;
        transaction.CardCollection.CustomerNameCiphertext = protector.Encrypt(
            string.IsNullOrWhiteSpace(customerName)
                ? contact is null
                    ? "Cashfree Customer"
                    : protector.Decrypt(contact.NameCiphertext)
                : customerName);

        var notes =
            $"Cashfree checkout payment confirmed. OrderId={orderId}, CfPaymentId={cfPaymentId}, GatewayPaymentId={gatewayPaymentId}, PaymentGroup={paymentGroup}, BankReference={bankReference}, ContactMatched={(contact is not null ? "yes" : "no")}, Email={customerEmail}, Phone={customerPhone}, Message={paymentMessage}";
        transaction.Events.Add(BuildEvent(transaction, "cashfree_payment_webhook", transaction.Status, notes, rawBody));

        if (!transaction.LedgerEntries.Any(item => item.EntryType == LedgerEntryType.Credit))
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

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessCashfreeFormWebhookAsync(JsonElement root, string rawBody, CancellationToken cancellationToken)
    {
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

        transaction.CardCollection ??= new CardCollectionDetail
        {
            TransactionId = transaction.Id
        };

        transaction.CardCollection.CardBrand = "Cashfree Payment Form";
        transaction.CardCollection.MaskedCardNumber = string.Empty;
        transaction.CardCollection.ProviderTokenReference = providerTransactionId;
        transaction.CardCollection.CustomerNameCiphertext = protector.Encrypt(customerName);

        var notes =
            $"Cashfree form webhook received. Status={orderStatus}, OrderId={orderId}, TransactionId={providerTransactionId}, FormId={formId}, ContactMatched={(contact is not null ? "yes" : "no")}, Email={customerEmail}, Phone={customerPhone}, FormUrl={formUrl}";
        transaction.Events.Add(BuildEvent(transaction, "cashfree_form_webhook", normalizedStatus, notes, rawBody));

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

    private async Task SavePendingCashfreeCheckoutTransactionAsync(
        Guid tenantId,
        Contact contact,
        decimal amount,
        string currency,
        string orderId,
        string localReference,
        string cfOrderId,
        string customerName,
        CancellationToken cancellationToken)
    {
        var fee = Math.Round(amount * 0.025m, 2);
        var transaction = await dbContext.Transactions
            .Include(item => item.CardCollection)
            .Include(item => item.Events)
            .FirstOrDefaultAsync(
                item => item.TenantId == tenantId
                    && item.TransactionType == TransactionType.CardCollection
                    && item.ProviderReference == orderId,
                cancellationToken);

        if (transaction is null)
        {
            transaction = BuildBaseTransaction(
                tenantId,
                TransactionType.CardCollection,
                amount,
                fee,
                currency,
                BuildCheckoutDescription("hosted_checkout", customerName));

            transaction.ContactId = contact.Id;
            transaction.ExternalReference = localReference;
            transaction.ProviderReference = orderId;
            transaction.Status = TransactionStatus.PendingProvider;
            transaction.SettlementStatus = "AwaitingProvider";
            transaction.CardCollection = new CardCollectionDetail
            {
                TransactionId = transaction.Id,
                CardBrand = "Cashfree Hosted Checkout",
                MaskedCardNumber = string.Empty,
                ProviderTokenReference = string.Empty,
                CustomerNameCiphertext = protector.Encrypt(customerName)
            };

            transaction.Events.Add(BuildEvent(
                transaction,
                "cashfree_order_created",
                transaction.Status,
                $"Cashfree checkout order created. OrderId={orderId}, CfOrderId={cfOrderId}, ContactId={contact.Id}, Awaiting provider confirmation."));

            dbContext.Transactions.Add(transaction);
        }
        else
        {
            transaction.ContactId = contact.Id;
            transaction.Amount = amount;
            transaction.FeeAmount = fee;
            transaction.NetAmount = amount - fee;
            transaction.Currency = currency;
            transaction.ExternalReference = string.IsNullOrWhiteSpace(transaction.ExternalReference) ? localReference : transaction.ExternalReference;
            transaction.ProviderReference = orderId;
            transaction.Status = TransactionStatus.PendingProvider;
            transaction.SettlementStatus = "AwaitingProvider";
            transaction.Description = BuildCheckoutDescription("hosted_checkout", customerName);
            transaction.CardCollection ??= new CardCollectionDetail
            {
                TransactionId = transaction.Id
            };
            transaction.CardCollection.CardBrand = "Cashfree Hosted Checkout";
            transaction.CardCollection.CustomerNameCiphertext = protector.Encrypt(customerName);
            transaction.Events.Add(BuildEvent(
                transaction,
                "cashfree_order_refreshed",
                transaction.Status,
                $"Cashfree checkout order refreshed locally. OrderId={orderId}, CfOrderId={cfOrderId}."));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessCashfreeTerminalWebhookAsync(JsonElement root, string rawBody, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            return;
        }

        var payment = data.TryGetProperty("payment", out var paymentElement) ? paymentElement : default;
        var order = data.TryGetProperty("order", out var orderElement) ? orderElement : default;

        var orderId = GetString(order, "order_id");
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        var customerDetails = order.TryGetProperty("customer_details", out var customerDetailsElement)
            ? customerDetailsElement
            : default;
        var orderStatus = GetString(order, "order_status", GetString(payment, "payment_status", "FAILED"));
        var customerPhone = NormalizeDigits(GetString(customerDetails, "customer_phone"));
        var customerEmail = GetString(customerDetails, "customer_email");
        var customerName = GetString(customerDetails, "customer_name");
        var paymentMessage = GetString(payment, "payment_message");
        var cfPaymentId = GetString(payment, "cf_payment_id");
        var paymentGroup = GetString(payment, "payment_group");

        var transaction = await dbContext.Transactions
            .Include(item => item.CardCollection)
            .Include(item => item.Events)
            .Include(item => item.LedgerEntries)
            .FirstOrDefaultAsync(
                item => item.TransactionType == TransactionType.CardCollection
                    && item.ProviderReference == orderId,
                cancellationToken);

        if (transaction is null)
        {
            return;
        }

        var normalizedStatus = MapCashfreeStatus(orderStatus);
        transaction.Status = normalizedStatus == TransactionStatus.Succeeded ? TransactionStatus.PendingProvider : normalizedStatus;
        transaction.SettlementStatus = MapSettlementStatus(orderStatus);
        transaction.FailureCode = normalizedStatus == TransactionStatus.Failed ? orderStatus : string.Empty;
        if (!string.IsNullOrWhiteSpace(customerName))
        {
            transaction.Description = BuildCheckoutDescription(paymentGroup, customerName);
        }

        transaction.CardCollection ??= new CardCollectionDetail
        {
            TransactionId = transaction.Id
        };
        transaction.CardCollection.CardBrand = "Cashfree Hosted Checkout";
        transaction.CardCollection.ProviderTokenReference = string.IsNullOrWhiteSpace(cfPaymentId)
            ? transaction.CardCollection.ProviderTokenReference
            : cfPaymentId;
        if (!string.IsNullOrWhiteSpace(customerName))
        {
            transaction.CardCollection.CustomerNameCiphertext = protector.Encrypt(customerName);
        }

        transaction.Events.Add(BuildEvent(
            transaction,
            "cashfree_payment_terminal_webhook",
            transaction.Status,
            $"Cashfree checkout terminal webhook received. Status={orderStatus}, OrderId={orderId}, CfPaymentId={cfPaymentId}, ContactMatched={(transaction.ContactId.HasValue ? "yes" : "no")}, Email={customerEmail}, Phone={customerPhone}, Message={paymentMessage}",
            rawBody));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<(decimal AvailableBalance, decimal HeldBalance)> GetWalletSnapshotAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.LedgerEntries.Where(item => item.TenantId == tenantId).ToListAsync(cancellationToken);
        decimal credits = entries.Where(item => item.EntryType is LedgerEntryType.Credit or LedgerEntryType.Reversal).Sum(item => item.Amount);
        decimal debits = entries.Where(item => item.EntryType is LedgerEntryType.Debit or LedgerEntryType.Fee).Sum(item => item.Amount);
        decimal holds = entries.Where(item => item.EntryType == LedgerEntryType.Hold).Sum(item => item.Amount);
        return (credits - debits - holds, holds);
    }

    private async Task<Contact?> ResolveContactForCashfreeAsync(IReadOnlyDictionary<string, string> orderTags, string customerPhone, CancellationToken cancellationToken)
    {
        if (orderTags.TryGetValue("contact_id", out var rawContactId) && Guid.TryParse(rawContactId, out var contactId))
        {
            var contact = await dbContext.Contacts.FirstOrDefaultAsync(item => item.Id == contactId, cancellationToken);
            if (contact is not null)
            {
                return contact;
            }
        }

        return await FindContactByPhoneAsync(customerPhone, cancellationToken);
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

    private async Task<Guid> ResolveTenantIdForCashfreeAsync(IReadOnlyDictionary<string, string> orderTags, Contact? contact, CancellationToken cancellationToken)
    {
        if (orderTags.TryGetValue("tenant_id", out var rawTenantId) && Guid.TryParse(rawTenantId, out var tenantId))
        {
            return tenantId;
        }

        return await ResolveTenantIdAsync(contact, cancellationToken);
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

    private async Task<CashfreeFetchedOrder?> FetchCashfreeOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        EnsureCashfreeApiConfigured();

        using var response = await SendCashfreeAsync(HttpMethod.Get, $"orders/{Uri.EscapeDataString(orderId)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ParseCashfreeFetchedOrderAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCashfreeAsync(HttpMethod method, string relativePath, object? body, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(method, BuildCashfreeUrl(relativePath));
        request.Headers.Add("x-api-version", cashfreeOptions.ApiVersion);
        request.Headers.Add("x-client-id", cashfreeOptions.ClientId);
        request.Headers.Add("x-client-secret", cashfreeOptions.ClientSecret);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: CashfreeJsonOptions);
        }

        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return response;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Cashfree request failed with {(int)response.StatusCode}: {errorBody}");
    }

    private async Task<CashfreeCreatedOrder> ParseCashfreeOrderResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        return new CashfreeCreatedOrder(
            GetString(root, "order_id"),
            root.TryGetProperty("cf_order_id", out var cfOrderIdElement) ? cfOrderIdElement.ToString() : string.Empty,
            GetString(root, "payment_session_id"));
    }

    private async Task<CashfreeFetchedOrder> ParseCashfreeFetchedOrderAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        return new CashfreeFetchedOrder(
            GetString(root, "order_id"),
            root.TryGetProperty("cf_order_id", out var cfOrderIdElement) ? cfOrderIdElement.ToString() : string.Empty,
            GetString(root, "order_status"),
            GetDecimal(root, "order_amount"),
            GetString(root, "order_currency", "INR"),
            root.TryGetProperty("created_at", out var createdAtElement) && createdAtElement.ValueKind == JsonValueKind.String
                ? DateTimeOffset.TryParse(createdAtElement.GetString(), out var createdAt)
                    ? createdAt
                    : null
                : null);
    }

    private object? BuildOrderMeta(string orderId)
    {
        var returnUrl = BuildReturnUrl(orderId);
        var notifyUrl = cashfreeOptions.NotifyUrl?.Trim();
        if (string.IsNullOrWhiteSpace(returnUrl) && string.IsNullOrWhiteSpace(notifyUrl))
        {
            return null;
        }

        return new
        {
            return_url = string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl,
            notify_url = string.IsNullOrWhiteSpace(notifyUrl) ? null : notifyUrl
        };
    }

    private string BuildReturnUrl(string orderId)
    {
        var baseReturnUrl = cashfreeOptions.ReturnUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseReturnUrl))
        {
            return string.Empty;
        }

        var separator = baseReturnUrl.Contains('?') ? "&" : "?";
        return $"{baseReturnUrl}{separator}order_id={Uri.EscapeDataString(orderId)}";
    }

    private void EnsureCashfreeApiConfigured()
    {
        if (string.IsNullOrWhiteSpace(cashfreeOptions.BaseUrl)
            || string.IsNullOrWhiteSpace(cashfreeOptions.ApiVersion)
            || string.IsNullOrWhiteSpace(cashfreeOptions.ClientId)
            || string.IsNullOrWhiteSpace(cashfreeOptions.ClientSecret))
        {
            throw new InvalidOperationException("Cashfree API settings are not configured.");
        }
    }

    private void EnsureValidCashfreeSignature(CashfreeWebhookRequest request)
    {
        var secret = !string.IsNullOrWhiteSpace(cashfreeOptions.WebhookSecret)
            ? cashfreeOptions.WebhookSecret
            : cashfreeOptions.ClientSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Cashfree webhook secret is not configured.");
        }

        var computed = ComputeCashfreeSignature(request.Timestamp, request.RawBody, secret);
        if (!FixedEquals(computed, request.Signature))
        {
            throw new UnauthorizedAccessException("Invalid Cashfree webhook signature.");
        }
    }

    private string BuildCashfreeUrl(string relativePath) => $"{cashfreeOptions.BaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

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

    private static IReadOnlyDictionary<string, string> ParseOrderTags(JsonElement order)
    {
        if (!order.TryGetProperty("order_tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in tagsElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.ToString();
        }

        return result;
    }

    private static string ResolveExternalReference(IReadOnlyDictionary<string, string> orderTags) =>
        orderTags.TryGetValue("local_reference", out var localReference) && !string.IsNullOrWhiteSpace(localReference)
            ? localReference
            : BuildLocalReference();

    private static string ExtractMaskedInstrument(JsonElement payment)
    {
        if (!payment.TryGetProperty("payment_method", out var paymentMethod) || paymentMethod.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var group in paymentMethod.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var field in group.Value.EnumerateObject())
            {
                if (field.Name.Contains("masked", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("instrument_number", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("card_number", StringComparison.OrdinalIgnoreCase))
                {
                    return field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString() ?? string.Empty : field.Value.ToString();
                }
            }
        }

        return string.Empty;
    }

    private static string BuildCheckoutDescription(string paymentGroup, string customerName)
    {
        var channel = string.IsNullOrWhiteSpace(paymentGroup) ? "checkout" : paymentGroup.Replace('_', ' ');
        var suffix = string.IsNullOrWhiteSpace(customerName) ? string.Empty : $" for {customerName}";
        return $"Cashfree {channel} collection{suffix}";
    }

    private static string BuildCashfreeOrderId() => $"pari_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..31];

    private static string BuildLocalReference() => $"TRX-{Guid.NewGuid():N}"[..16];

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
            ExternalReference = BuildLocalReference(),
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

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : element.TryGetProperty(propertyName, out property)
                ? property.ToString()
                : fallback;

    private static decimal GetDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0m;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(property.GetString(), out var parsed) => parsed,
            _ => 0m
        };
    }

    private sealed record CashfreeCreatedOrder(string OrderId, string CfOrderId, string PaymentSessionId);
    private sealed record CashfreeFetchedOrder(string OrderId, string CfOrderId, string OrderStatus, decimal OrderAmount, string OrderCurrency, DateTimeOffset? CreatedAt);
}
