using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

/// <summary>
/// WP-41B: data access for admin user management (operator accounts only — G1 scope).
/// "Operator" = a SystemUser holding at least one non-identity role (see RoleTaxonomy).
/// </summary>
public interface IAdminUserRepository
{
    /// <summary>Paged operators-only list; search = case-insensitive contains on name/email.</summary>
    Task<PagedResult<AdminUserSummary>> GetPagedAsync(string? search, int page, int pageSize);

    /// <summary>Summary for one user, or null when the user does not exist OR holds no operator role.</summary>
    Task<AdminUserSummary?> GetSummaryAsync(int userId);

    /// <summary>All RoleType rows (small lookup table) for id/identity validation.</summary>
    Task<IReadOnlyList<RoleType>> GetRoleTypesAsync();

    Task<bool> EmailExistsAsync(string email);

    /// <summary>Tracked SystemUser row (null when missing).</summary>
    Task<User?> GetUserAsync(int userId);

    /// <summary>Tracked UserRole links for one user (identity AND operator).</summary>
    Task<IReadOnlyList<UserRole>> GetUserRolesAsync(int userId);

    /// <summary>Active SystemUsers holding the SystemAdmin role, excluding <paramref name="excludedUserId"/>.</summary>
    Task<int> CountOtherActiveSystemAdminsAsync(int excludedUserId);

    /// <summary>Inserts the user then its UserRole links; returns the new UserID.</summary>
    Task<int> CreateUserWithRolesAsync(User user, IReadOnlyCollection<int> roleTypeIds);

    /// <summary>
    /// Persists a role-set change plus any pending tracked-entity edits (e.g. ActiveStatus) in
    /// one SaveChanges: removes <paramref name="rolesToRemove"/>, adds links for
    /// <paramref name="roleTypeIdsToAdd"/>.
    /// </summary>
    Task ApplyUpdateAsync(User user, IReadOnlyCollection<UserRole> rolesToRemove, IReadOnlyCollection<int> roleTypeIdsToAdd);

    /// <summary>Persists edits to a tracked user row (reset-password flow).</summary>
    Task SaveUserAsync(User user);
}
