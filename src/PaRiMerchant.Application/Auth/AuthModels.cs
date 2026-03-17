namespace PaRiMerchant.Application.Auth;

public sealed record LoginRequest(string MobileNumber, string Password);
public sealed record LoginResponse(string StepUpToken, bool RequiresMpin, DateTimeOffset ExpiresUtc);
public sealed record VerifyMpinRequest(string StepUpToken, string Mpin, string? IpAddress = null);
public sealed record RefreshRequest(string RefreshToken, string? IpAddress = null);
public sealed record LogoutRequest(string RefreshToken);
public sealed record SessionResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresUtc, UserProfile Me);
public sealed record UserProfile(string UserId, string TenantId, string MerchantCode, string Role, string DisplayName, string MaskedPhone, string MaskedEmail);
