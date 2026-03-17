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
    [HttpPost("load-money/collection/initiate")]
    public Task<PaymentTransactionResponse> InitiateCollectionAsync([FromBody] InitiateCollectionRequest request, CancellationToken cancellationToken)
        => paymentService.InitiateCollectionAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPost("load-money/self-topup/initiate")]
    public Task<PaymentTransactionResponse> InitiateSelfTopupAsync([FromBody] InitiateSelfTopupRequest request, CancellationToken cancellationToken)
        => paymentService.InitiateSelfTopupAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPost("payouts")]
    public Task<PaymentTransactionResponse> CreatePayoutAsync([FromBody] CreatePayoutRequest request, CancellationToken cancellationToken)
        => paymentService.CreatePayoutAsync(User.GetRequiredTenantId(), request, cancellationToken);
}
