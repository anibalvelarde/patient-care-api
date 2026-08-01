using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// WP-41B: admin user management (SA-only v1). Operator accounts only (G1); identity role
/// links are never touched (G4); guard rails per G5 surface as ArgumentException → 400 via
/// the global exception handler.
/// </summary>
public interface IAdminUserService
{
    Task<PagedResult<AdminUserSummary>> GetPagedAsync(string? search, int page, int pageSize);

    /// <summary>Throws NotFoundException when the user is missing or holds no operator role.</summary>
    Task<AdminUserSummary> GetByIdAsync(int userId);

    /// <summary>Creates an active operator account with MustChangePassword = true (G3).</summary>
    Task<AdminUserSummary> CreateAsync(AdminUserCreateRequest request);

    /// <summary>
    /// Replace-role-set and/or active-status update. <paramref name="callerUserId"/> is the
    /// authenticated caller, needed for the G5 self-deactivate / self-demote guard rails.
    /// </summary>
    Task UpdateAsync(int userId, AdminUserUpdateRequest request, int callerUserId);

    /// <summary>Sets a temporary password + MustChangePassword = true (existing must-change flow).</summary>
    Task ResetPasswordAsync(int userId, AdminUserResetPasswordRequest request);
}
