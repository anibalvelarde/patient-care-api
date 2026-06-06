using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Auth;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>Orchestrates authentication: credential checks, token issuance, password changes.</summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request);

    Task<AuthResult> RefreshAsync(string refreshToken);

    /// <summary>Changes the caller's password and clears the must-change / lockout flags.</summary>
    Task<AuthResult> ChangePasswordAsync(int userId, ChangePasswordRequest request);

    Task<CurrentUserResponse?> GetCurrentUserAsync(int userId);
}
