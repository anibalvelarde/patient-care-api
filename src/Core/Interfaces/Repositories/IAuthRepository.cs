using System.Collections.Generic;
using System.Threading.Tasks;
using Neurocorp.Api.Core.BusinessObjects.Auth;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

/// <summary>
/// Data access for authentication and the claims-aware authorization model.
/// Computes a user's effective claims from RoleClaim + UserClaim (with denials applied).
/// </summary>
public interface IAuthRepository
{
    Task<User?> GetByEmailAsync(string email);

    Task<User?> GetByIdAsync(int id);

    /// <summary>
    /// Returns the user's effective claims:
    /// (claims from every role in UserRole) UNION (UserClaim where IsDenied=0)
    /// MINUS (UserClaim where IsDenied=1).
    /// </summary>
    Task<IReadOnlyList<ClaimDto>> GetEffectiveClaimsAsync(int userId);

    Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId);

    /// <summary>Persists changes to a tracked user (login bookkeeping, password change).</summary>
    Task SaveUserAsync(User user);
}
