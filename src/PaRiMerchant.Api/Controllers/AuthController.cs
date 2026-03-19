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

    [Authorize]
    [HttpPost("mpin/unlock")]
    public Task<UserProfile> UnlockMpinAsync([FromBody] UnlockMpinRequest request, CancellationToken cancellationToken)
        => authService.UnlockWithMpinAsync(User.GetRequiredUserId(), request, cancellationToken);

    [Authorize]
    [HttpPost("mpin/update")]
    public async Task<IActionResult> UpdateMpinAsync([FromBody] UpdateMpinRequest request, CancellationToken cancellationToken)
    {
        await authService.UpdateMpinAsync(User.GetRequiredUserId(), request, cancellationToken);
        return NoContent();
    }

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

    [Authorize]
    [HttpPut("me")]
    public Task<UserProfile> UpdateProfileAsync([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
        => authService.UpdateProfileAsync(User.GetRequiredUserId(), request, cancellationToken);
}
