using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaRiMerchant.Api.Extensions;
using PaRiMerchant.Application.Auth;

namespace PaRiMerchant.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public Task<LoginResponse> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => authService.LoginAsync(request, cancellationToken);

    [AllowAnonymous]
    [HttpPost("mpin/verify")]
    public Task<SessionResponse> VerifyMpinAsync([FromBody] VerifyMpinRequest request, CancellationToken cancellationToken)
        => authService.VerifyMpinAsync(request with { IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown" }, cancellationToken);

    [AllowAnonymous]
    [HttpPost("refresh")]
    public Task<SessionResponse> RefreshAsync([FromBody] RefreshRequest request, CancellationToken cancellationToken)
        => authService.RefreshAsync(request with { IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown" }, cancellationToken);

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public Task<UserProfile> MeAsync(CancellationToken cancellationToken)
        => authService.GetProfileAsync(User.GetRequiredUserId(), cancellationToken);
}
