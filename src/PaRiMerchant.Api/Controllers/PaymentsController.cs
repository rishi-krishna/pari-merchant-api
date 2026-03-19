using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Payments;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class PaymentsController(PaymentService paymentService) : ControllerBase
{
    [HttpPost("load-money/cashfree/order")]
    public Task<CashfreeCheckoutOrderResponse> CreateCashfreeCheckoutOrderAsync([FromBody] CreateCashfreeCheckoutOrderRequest request, CancellationToken cancellationToken)
        => paymentService.CreateCashfreeCheckoutOrderAsync(User.GetRequiredTenantId(), User.GetRequiredUserId(), request, cancellationToken);

    [HttpPost("load-money/collection/initiate")]
    public Task<PaymentTransactionResponse> InitiateCollectionAsync([FromBody] InitiateCollectionRequest request, CancellationToken cancellationToken)
        => paymentService.InitiateCollectionAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPost("load-money/self-topup/initiate")]
    public Task<PaymentTransactionResponse> InitiateSelfTopupAsync([FromBody] InitiateSelfTopupRequest request, CancellationToken cancellationToken)
        => paymentService.InitiateSelfTopupAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPost("payouts")]
    public Task<PaymentTransactionResponse> CreatePayoutAsync([FromBody] CreatePayoutRequest request, CancellationToken cancellationToken)
        => paymentService.CreatePayoutAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [Authorize]
    [HttpGet("load-money/result")]
    public Task<LoadMoneyResultResponse> GetLoadMoneyResultAsync(
        [FromQuery] string? orderId,
        [FromQuery] string? providerTransactionId,
        CancellationToken cancellationToken)
        => paymentService.GetLoadMoneyResultAsync(User.GetRequiredTenantId(), orderId, providerTransactionId, cancellationToken);

    [AllowAnonymous]
    [HttpPost("payments/cashfree/webhook")]
    [HttpPost("payments/cashfree/forms/webhook")]
    public async Task<IActionResult> HandleCashfreeWebhookAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var signature = Request.Headers["x-webhook-signature"].ToString();
        var timestamp = Request.Headers["x-webhook-timestamp"].ToString();

        await paymentService.ProcessCashfreeWebhookAsync(new CashfreeWebhookRequest(rawBody, signature, timestamp), cancellationToken);
        return Ok(new { received = true });
    }
}
