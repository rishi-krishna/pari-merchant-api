using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PaRiMerchant.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User claim is missing.");

        return Guid.Parse(raw);
    }

    public static Guid GetRequiredTenantId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue("tenant_id")
            ?? throw new UnauthorizedAccessException("Tenant claim is missing.");

        return Guid.Parse(raw);
    }
}
