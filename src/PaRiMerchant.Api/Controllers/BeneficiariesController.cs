using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Beneficiaries;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/beneficiaries")]
public sealed class BeneficiariesController(BeneficiaryService beneficiaryService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<BeneficiaryResponse>> GetAsync(CancellationToken cancellationToken)
        => beneficiaryService.GetAllAsync(User.GetRequiredTenantId(), cancellationToken);

    [HttpPost("validate")]
    public Task<BeneficiaryResponse> ValidateAsync([FromBody] ValidateBeneficiaryRequest request, CancellationToken cancellationToken)
        => beneficiaryService.ValidateAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpPost]
    public Task<BeneficiaryResponse> CreateAsync([FromBody] CreateBeneficiaryRequest request, CancellationToken cancellationToken)
        => beneficiaryService.CreateAsync(User.GetRequiredTenantId(), request, cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await beneficiaryService.DeleteAsync(User.GetRequiredTenantId(), id, cancellationToken);
        return NoContent();
    }
}
