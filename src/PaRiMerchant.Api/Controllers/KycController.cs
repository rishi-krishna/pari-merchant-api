using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Kyc;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/kyc")]
public sealed class KycController(KycService kycService) : ControllerBase
{
    [HttpGet("profile")]
    public Task<KycProfileResponse?> GetProfileAsync(CancellationToken cancellationToken)
        => kycService.GetAsync(User.GetRequiredTenantId(), cancellationToken);

    [HttpPut("profile")]
    public Task<KycProfileResponse> PutProfileAsync([FromBody] KycProfileRequest request, CancellationToken cancellationToken)
        => kycService.UpsertAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPost("documents")]
    [RequestSizeLimit(10_000_000)]
    public Task<KycDocumentResponse> UploadDocumentAsync([FromForm] string kind, [FromForm] IFormFile file, CancellationToken cancellationToken)
        => kycService.UploadDocumentAsync(User.GetRequiredTenantId(), new KycDocumentUploadRequest(kind, file), cancellationToken);

    [HttpGet("documents/{id:guid}")]
    public Task<KycDocumentResponse> GetDocumentAsync(Guid id, CancellationToken cancellationToken)
        => kycService.GetDocumentAsync(User.GetRequiredTenantId(), id, cancellationToken);
}
