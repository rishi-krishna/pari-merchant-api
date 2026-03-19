using Microsoft.EntityFrameworkCore;
using PaRiMerchant.Application.Abstractions;
using PaRiMerchant.Domain.Entities;

namespace PaRiMerchant.Application.Auth;

public sealed class AuthService(
    IAppDbContext dbContext,
    ISensitiveDataProtector protector,
    IPasswordHasher passwordHasher,
    ITokenService tokenService)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var phoneIndex = protector.ComputeBlindIndex(NormalizeDigits(request.MobileNumber));

        var user = await dbContext.MerchantUsers
            .Include(candidate => candidate.MerchantProfile)
            .FirstOrDefaultAsync(candidate => candidate.PhoneBlindIndex == phoneIndex && candidate.IsActive, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid mobile number or password.");
        }

        user.LastLoginUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResponse(tokenService.CreatePasswordVerifiedToken(user), true, DateTimeOffset.UtcNow.AddMinutes(10));
    }

    public async Task<SessionResponse> VerifyMpinAsync(VerifyMpinRequest request, CancellationToken cancellationToken)
    {
        if (!tokenService.TryReadPasswordVerifiedToken(request.StepUpToken, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid or expired step-up token.");
        }

        var user = await dbContext.MerchantUsers
            .Include(candidate => candidate.MerchantProfile)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Mpin, user.MpinHash))
        {
            throw new UnauthorizedAccessException("Invalid MPIN.");
        }

        var accessToken = tokenService.CreateAccessToken(user);
        var refresh = tokenService.CreateRefreshToken();

        dbContext.RefreshSessions.Add(new RefreshSession
        {
            TenantId = user.TenantId,
            MerchantUserId = user.Id,
            TokenHash = refresh.Hash,
            ExpiresUtc = refresh.ExpiresUtc,
            CreatedByIp = request.IpAddress ?? "unknown"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SessionResponse(accessToken.Token, refresh.RawToken, accessToken.ExpiresUtc, BuildProfile(user));
    }

    public async Task<UserProfile> UnlockWithMpinAsync(Guid userId, UnlockMpinRequest request, CancellationToken cancellationToken)
    {
        var user = await dbContext.MerchantUsers
            .Include(candidate => candidate.MerchantProfile)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken);

        if (user is null || !passwordHasher.Verify(request.Mpin, user.MpinHash))
        {
            throw new UnauthorizedAccessException("Invalid MPIN.");
        }

        return BuildProfile(user);
    }

    public async Task<SessionResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var suppliedHash = protector.ComputeBlindIndex(request.RefreshToken);
        var session = await dbContext.RefreshSessions
            .Include(candidate => candidate.MerchantUser)
            .ThenInclude(user => user.MerchantProfile)
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == suppliedHash && candidate.RevokedUtc == null, cancellationToken);

        if (session is null || session.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        session.RevokedUtc = DateTimeOffset.UtcNow;

        var accessToken = tokenService.CreateAccessToken(session.MerchantUser);
        var nextRefresh = tokenService.CreateRefreshToken();

        dbContext.RefreshSessions.Add(new RefreshSession
        {
            TenantId = session.TenantId,
            MerchantUserId = session.MerchantUserId,
            TokenHash = nextRefresh.Hash,
            ExpiresUtc = nextRefresh.ExpiresUtc,
            CreatedByIp = request.IpAddress ?? "unknown"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SessionResponse(accessToken.Token, nextRefresh.RawToken, accessToken.ExpiresUtc, BuildProfile(session.MerchantUser));
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        var suppliedHash = protector.ComputeBlindIndex(request.RefreshToken);
        var session = await dbContext.RefreshSessions.FirstOrDefaultAsync(candidate => candidate.TokenHash == suppliedHash, cancellationToken);

        if (session is not null)
        {
            session.RevokedUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.MerchantUsers
            .Include(candidate => candidate.MerchantProfile)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("User not found.");

        return BuildProfile(user);
    }

    public async Task<UserProfile> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var displayName = request.DisplayName.Trim();
        var email = request.Email.Trim();
        var phone = NormalizeDigits(request.Phone);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new InvalidOperationException("A valid email address is required.");
        }

        if (phone.Length != 10)
        {
            throw new InvalidOperationException("Phone number must contain exactly 10 digits.");
        }

        var user = await dbContext.MerchantUsers
            .Include(candidate => candidate.MerchantProfile)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("User not found.");

        var phoneBlindIndex = protector.ComputeBlindIndex(phone);
        var emailBlindIndex = protector.ComputeBlindIndex(email.Trim().ToLowerInvariant());

        var phoneInUse = await dbContext.MerchantUsers
            .AnyAsync(candidate => candidate.Id != userId && candidate.PhoneBlindIndex == phoneBlindIndex, cancellationToken);

        if (phoneInUse)
        {
            throw new InvalidOperationException("That phone number is already in use.");
        }

        var emailInUse = await dbContext.MerchantUsers
            .AnyAsync(candidate => candidate.Id != userId && candidate.EmailBlindIndex == emailBlindIndex, cancellationToken);

        if (emailInUse)
        {
            throw new InvalidOperationException("That email address is already in use.");
        }

        user.DisplayNameCiphertext = protector.Encrypt(displayName);
        user.EmailCiphertext = protector.Encrypt(email);
        user.EmailBlindIndex = emailBlindIndex;
        user.PhoneCiphertext = protector.Encrypt(phone);
        user.PhoneBlindIndex = phoneBlindIndex;

        await dbContext.SaveChangesAsync(cancellationToken);
        return BuildProfile(user);
    }

    public async Task UpdateMpinAsync(Guid userId, UpdateMpinRequest request, CancellationToken cancellationToken)
    {
        if (request.NewMpin != request.ConfirmMpin)
        {
            throw new InvalidOperationException("New MPIN and confirmation do not match.");
        }

        if (request.NewMpin.Length != 6 || request.NewMpin.Any(ch => !char.IsDigit(ch)))
        {
            throw new InvalidOperationException("New MPIN must contain exactly 6 digits.");
        }

        var user = await dbContext.MerchantUsers.FirstOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (!passwordHasher.Verify(request.OldMpin, user.MpinHash))
        {
            throw new UnauthorizedAccessException("Current MPIN is invalid.");
        }

        user.MpinHash = passwordHasher.Hash(request.NewMpin);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private UserProfile BuildProfile(MerchantUser user) => new(
        user.Id.ToString(),
        user.TenantId.ToString(),
        user.MerchantProfile.MerchantCode,
        user.Role.ToString(),
        protector.Decrypt(user.DisplayNameCiphertext),
        protector.Decrypt(user.PhoneCiphertext),
        protector.Decrypt(user.EmailCiphertext));

    private static string NormalizeDigits(string value) => new(value.Where(char.IsDigit).ToArray());
}
