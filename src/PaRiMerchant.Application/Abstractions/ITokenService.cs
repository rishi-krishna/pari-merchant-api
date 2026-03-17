using PaRiMerchant.Domain.Entities;

namespace PaRiMerchant.Application.Abstractions;

public interface ITokenService
{
    string CreatePasswordVerifiedToken(MerchantUser user);
    AccessTokenEnvelope CreateAccessToken(MerchantUser user);
    (string RawToken, string Hash, DateTimeOffset ExpiresUtc) CreateRefreshToken();
    bool TryReadPasswordVerifiedToken(string token, out Guid userId);
}

public sealed record AccessTokenEnvelope(string Token, DateTimeOffset ExpiresUtc);
