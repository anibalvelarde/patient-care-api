using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Data;

namespace Neurocorp.Api.Infrastructure.Repositories;

/// <summary>
/// WP-41B: EF Core data access for admin user management. Operator = a SystemUser holding at
/// least one non-identity role (RoleTaxonomy). Search/paging follow the WP-21/WP-30 pattern:
/// explicit ToLower() keeps the InMemory test provider aligned with MySQL's _ci collation;
/// stable ordering LastName, then FirstName, then Id.
/// </summary>
public class AdminUserRepository : IAdminUserRepository
{
    // Materialized lowercase identity names so the EF provider sees constants.
    private static readonly string[] IdentityRoleNamesLower =
        RoleTaxonomy.IdentityRoleNames.Select(n => n.ToLowerInvariant()).ToArray();

    private readonly ApplicationDbContext _dbContext;

    public AdminUserRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<AdminUserSummary>> GetPagedAsync(string? search, int page, int pageSize)
    {
        var operatorRoleIds = await OperatorRoleIdsAsync();
        var operators = _dbContext.Users
            .AsNoTracking()
            .Where(u => _dbContext.Set<UserRole>()
                .Any(ur => ur.UserId == u.Id && operatorRoleIds.Contains(ur.RoleId)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            operators = operators.Where(u =>
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var totalCount = await operators.CountAsync();
        var pageUsers = await operators
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<AdminUserSummary>
        {
            Items = await BuildSummariesAsync(pageUsers),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<AdminUserSummary?> GetSummaryAsync(int userId)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
        {
            return null;
        }

        var summary = (await BuildSummariesAsync([user])).Single();
        // G1 scope: a user with no operator role is invisible to this API.
        return summary.OperatorRoles.Count > 0 ? summary : null;
    }

    public async Task<IReadOnlyList<RoleType>> GetRoleTypesAsync()
        => await _dbContext.RoleTypes.AsNoTracking().ToListAsync();

    public async Task<bool> EmailExistsAsync(string email)
        => await _dbContext.Users.AnyAsync(u => u.Email == email);

    public async Task<User?> GetUserAsync(int userId)
        => await _dbContext.Users.FindAsync(userId);

    public async Task<IReadOnlyList<UserRole>> GetUserRolesAsync(int userId)
        => await _dbContext.Set<UserRole>().Where(ur => ur.UserId == userId).ToListAsync();

    public async Task<int> CountOtherActiveSystemAdminsAsync(int excludedUserId)
    {
        var sysAdminRoleIds = await _dbContext.RoleTypes
            .Where(r => r.Name == RoleTaxonomy.SystemAdminRoleName)
            .Select(r => r.Id)
            .ToListAsync();

        return await _dbContext.Users
            .Where(u => u.Id != excludedUserId && u.ActiveStatus)
            .Where(u => _dbContext.Set<UserRole>()
                .Any(ur => ur.UserId == u.Id && sysAdminRoleIds.Contains(ur.RoleId)))
            .CountAsync();
    }

    public async Task<int> CreateUserWithRolesAsync(User user, IReadOnlyCollection<int> roleTypeIds)
    {
        _dbContext.Users.Add(user);
        // First save assigns the UserID (and is where uq_systemuser_email fires, becoming the
        // global handler's duplicate-key 409) — no role links exist yet if it throws.
        await _dbContext.SaveChangesAsync();

        foreach (var roleTypeId in roleTypeIds)
        {
            _dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = roleTypeId });
        }
        await _dbContext.SaveChangesAsync();
        return user.Id;
    }

    public async Task ApplyUpdateAsync(User user, IReadOnlyCollection<UserRole> rolesToRemove, IReadOnlyCollection<int> roleTypeIdsToAdd)
    {
        _dbContext.Set<UserRole>().RemoveRange(rolesToRemove);
        foreach (var roleTypeId in roleTypeIdsToAdd)
        {
            _dbContext.Set<UserRole>().Add(new UserRole { UserId = user.Id, RoleId = roleTypeId });
        }
        // user is tracked (GetUserAsync) — its ActiveStatus edit flushes in the same save.
        await _dbContext.SaveChangesAsync();
    }

    public async Task SaveUserAsync(User user)
    {
        if (_dbContext.Entry(user).State == EntityState.Detached)
        {
            _dbContext.Users.Attach(user);
            _dbContext.Entry(user).State = EntityState.Modified;
        }
        await _dbContext.SaveChangesAsync();
    }

    // ---- projection helpers -----------------------------------------------------

    private async Task<List<int>> OperatorRoleIdsAsync()
        => await _dbContext.RoleTypes
            .Where(r => !IdentityRoleNamesLower.Contains(r.Name.ToLower()))
            .Select(r => r.Id)
            .ToListAsync();

    private async Task<IReadOnlyList<AdminUserSummary>> BuildSummariesAsync(IReadOnlyList<User> users)
    {
        var userIds = users.Select(u => u.Id).ToList();
        var roleRows = await _dbContext.Set<UserRole>()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_dbContext.RoleTypes,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, RoleTypeId = r.Id, r.Name })
            .ToListAsync();
        var rolesByUser = roleRows.ToLookup(r => r.UserId);

        return users.Select(u =>
        {
            var roles = rolesByUser[u.Id].ToList();
            return new AdminUserSummary
            {
                UserId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email ?? string.Empty,
                IsActive = u.ActiveStatus,
                MustChangePassword = u.MustChangePassword,
                OperatorRoles = roles
                    .Where(r => !RoleTaxonomy.IsIdentityRole(r.Name))
                    .OrderBy(r => r.Name)
                    .Select(r => new AdminUserRoleInfo { RoleTypeId = r.RoleTypeId, Name = r.Name })
                    .ToList(),
                IdentityRoles = roles
                    .Where(r => RoleTaxonomy.IsIdentityRole(r.Name))
                    .OrderBy(r => r.Name)
                    .Select(r => r.Name)
                    .ToList(),
            };
        }).ToList();
    }
}
