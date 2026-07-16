using System;
using System.Linq;
using System.Threading.Tasks;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// Authentication orchestration: validates credentials, applies lockout policy, issues
/// access + refresh tokens, and handles password changes. Knows nothing about JWTs or
/// HTTP — it depends only on Core interfaces, so the token strategy and storage are
/// swappable (e.g. a future external IdP).
/// </summary>
public class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MinPasswordLength = 8;
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private const int DefaultIdleLogoffMinutes = 60;

    private readonly IAuthRepository _authRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ISiteRepository _siteRepository;

    public AuthService(IAuthRepository authRepository, IPasswordHasher passwordHasher, ITokenService tokenService, ISiteRepository siteRepository)
    {
        _authRepository = authRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _siteRepository = siteRepository;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return AuthResult.Fail(InvalidCredentialsMessage);
        }

        var user = await _authRepository.GetByEmailAsync(request.Email.Trim());

        // Generic message either way so we don't reveal which emails exist.
        if (user is null)
        {
            return AuthResult.Fail(InvalidCredentialsMessage);
        }

        if (!user.ActiveStatus)
        {
            return AuthResult.Fail("This account is inactive. Contact an administrator.");
        }

        if (user.LockoutUntil is { } lockout && lockout > DateTime.UtcNow)
        {
            return AuthResult.Fail("This account is temporarily locked due to failed login attempts. Try again later.");
        }

        if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts += 1;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;
            }
            await _authRepository.SaveUserAsync(user);
            return AuthResult.Fail(InvalidCredentialsMessage);
        }

        // Successful authentication — reset lockout bookkeeping.
        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        await _authRepository.SaveUserAsync(user);

        return await IssueTokensAsync(user.Id, user.Email, user.GetFullName(), user.MustChangePassword);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthResult.Fail("A refresh token is required.");
        }

        var userId = _tokenService.ValidateRefreshToken(refreshToken);
        if (userId is null)
        {
            return AuthResult.Fail("The refresh token is invalid or has expired.");
        }

        var user = await _authRepository.GetByIdAsync(userId.Value);
        if (user is null || !user.ActiveStatus)
        {
            return AuthResult.Fail("The refresh token is invalid or has expired.");
        }

        return await IssueTokensAsync(user.Id, user.Email, user.GetFullName(), user.MustChangePassword);
    }

    public async Task<AuthResult> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        if (request is null || string.IsNullOrEmpty(request.NewPassword))
        {
            return AuthResult.Fail("A new password is required.");
        }

        if (request.NewPassword.Length < MinPasswordLength)
        {
            return AuthResult.Fail($"The new password must be at least {MinPasswordLength} characters long.");
        }

        var user = await _authRepository.GetByIdAsync(userId);
        if (user is null)
        {
            return AuthResult.Fail("User not found.");
        }

        // Require the current password to be correct (the temporary password counts).
        if (string.IsNullOrEmpty(user.PasswordHash) || !_passwordHasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
        {
            return AuthResult.Fail("The current password is incorrect.");
        }

        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
        {
            return AuthResult.Fail("The new password must be different from the current password.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.PasswordChangedAt = DateTime.UtcNow;
        user.MustChangePassword = false;
        user.FailedLoginAttempts = 0;
        user.LockoutUntil = null;
        await _authRepository.SaveUserAsync(user);

        return await IssueTokensAsync(user.Id, user.Email, user.GetFullName(), mustChangePassword: false);
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(int userId)
    {
        var user = await _authRepository.GetByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var claims = await _authRepository.GetEffectiveClaimsAsync(userId);
        var roles = await _authRepository.GetRoleNamesAsync(userId);

        return new CurrentUserResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.GetFullName(),
            MustChangePassword = user.MustChangePassword,
            Roles = roles,
            Claims = claims,
            IsSystemAdmin = claims.Any(c =>
                c.Type == SystemClaims.SystemClaimType && c.Value == SystemClaims.FullAccessValue),
            IdleLogoffMinutes = await GetIdleLogoffMinutesAsync()
        };
    }

    // WP-32 (U4): the idle auto-logoff setting is a Site attribute, but every operator needs it
    // and the Site GETs are admin-gated — so it rides on /auth/me. Single-site platform today:
    // read the first/only Site row (lowest Id); fall back to the default if no Site exists.
    private async Task<int> GetIdleLogoffMinutesAsync()
    {
        var sites = await _siteRepository.GetAllAsync();
        return sites.OrderBy(s => s.Id).FirstOrDefault()?.IdleLogoffMinutes ?? DefaultIdleLogoffMinutes;
    }

    private async Task<AuthResult> IssueTokensAsync(int userId, string? email, string fullName, bool mustChangePassword)
    {
        var claims = await _authRepository.GetEffectiveClaimsAsync(userId);
        var roles = await _authRepository.GetRoleNamesAsync(userId);

        var access = _tokenService.CreateAccessToken(userId, email, fullName, claims, roles, mustChangePassword);
        var refresh = _tokenService.CreateRefreshToken(userId);

        return AuthResult.Success(access.Token, refresh, access.ExpiresInSeconds, mustChangePassword);
    }
}
