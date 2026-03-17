using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Wallet;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class WalletController(WalletService walletService) : ControllerBase
{
    [HttpGet("wallet")]
    public Task<WalletSummaryResponse> GetWalletAsync(CancellationToken cancellationToken)
        => walletService.GetSummaryAsync(User.GetRequiredTenantId(), cancellationToken);

    [HttpGet("ledger")]
    public Task<IReadOnlyList<LedgerEntryResponse>> GetLedgerAsync(CancellationToken cancellationToken)
        => walletService.GetLedgerAsync(User.GetRequiredTenantId(), cancellationToken);
}
