using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;

namespace PaRiMerchant.Infrastructure.Security;

public sealed class JwtTokenService(IOptions<SecurityOptions> options) : ITokenService
{
    private readonly SecurityOptions _options = options.Value;

    public string CreatePasswordVerifiedToken(MerchantUser user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("stepup", "true")
        };

        return WriteToken(claims, DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public AccessTokenEnvelope CreateAccessToken(MerchantUser user)
    {
        var expiresUtc = DateTimeOffset.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("merchant_profile_id", user.MerchantProfileId.ToString())
        };

        return new AccessTokenEnvelope(WriteToken(claims, expiresUtc), expiresUtc);
    }

    public (string RawToken, string Hash, DateTimeOffset ExpiresUtc) CreateRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var expires = DateTimeOffset.UtcNow.AddDays(30);
        return (raw, ComputeBlindIndex(raw), expires);
    }

    public bool TryReadPasswordVerifiedToken(string token, out Guid userId)
    {
        userId = Guid.Empty;

        try
        {
            var handler = new JwtSecurityTokenHandler
            {
                MapInboundClaims = false
            };

            var principal = handler.ValidateToken(token, GetValidationParameters(), out _);
            if (!bool.TryParse(principal.FindFirstValue("stepup"), out var isStepUp) || !isStepUp)
            {
                return false;
            }

            return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
        }
        catch
        {
            return false;
        }
    }

    private string WriteToken(IEnumerable<Claim> claims, DateTimeOffset expiresUtc)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expiresUtc.UtcDateTime, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private TokenValidationParameters GetValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };

    private string ComputeBlindIndex(string plaintext)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(_options.BlindIndexKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintext.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash);
    }
}
