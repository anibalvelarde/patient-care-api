using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Neurocorp.Api.Core.Authorization;
using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-41B: admin user-management orchestration (SA-only v1).
///
/// Scope (G1): operator accounts only — a user missing or holding no operator role is a 404.
/// Role composition (G4): roles are purely additive; this service manages OPERATOR links only
/// and never touches identity links (Patient/Therapist/Caretaker).
/// Guard rails (G5): self-deactivate, self-demote-from-SystemAdmin, and last-active-SystemAdmin
/// protection — all hard 400s (ArgumentException → global handler).
/// Error mapping: ArgumentException → 400, NotFoundException → 404, ConflictException → 409.
/// </summary>
public class AdminUserService : IAdminUserService
{
    private const string UserResourceName = "User";

    private readonly IAdminUserRepository _repository;
    private readonly IPasswordHasher _passwordHasher;

    public AdminUserService(IAdminUserRepository repository, IPasswordHasher passwordHasher)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
    }

    public Task<PagedResult<AdminUserSummary>> GetPagedAsync(string? search, int page, int pageSize)
        => _repository.GetPagedAsync(search, page, pageSize);

    public async Task<AdminUserSummary> GetByIdAsync(int userId)
    {
        var summary = await _repository.GetSummaryAsync(userId);
        return summary ?? throw new NotFoundException(UserResourceName, userId);
    }

    public async Task<AdminUserSummary> CreateAsync(AdminUserCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireField(request.FirstName, "firstName");
        RequireField(request.LastName, "lastName");
        RequireField(request.Email, "email");
        ValidateTempPassword(request.TempPassword, "tempPassword");
        var roleTypeIds = await ValidateOperatorRoleSetAsync(request.OperatorRoleTypeIds);

        var email = request.Email!.Trim();
        if (await _repository.EmailExistsAsync(email))
        {
            // Deterministic pre-check; the DB's uq_systemuser_email unique key (mapped to the
            // same 409 message by GlobalExceptionHandler) remains the race-proof backstop.
            throw new ConflictException("A user with this email address already exists.");
        }

        var user = new User
        {
            FirstName = request.FirstName!.Trim(),
            LastName = request.LastName!.Trim(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.TempPassword!),
            MustChangePassword = true, // G3: temp password, forced change at next login
            ActiveStatus = true,
        };

        var newUserId = await _repository.CreateUserWithRolesAsync(user, roleTypeIds);
        return (await _repository.GetSummaryAsync(newUserId))!;
    }

    public async Task UpdateAsync(int userId, AdminUserUpdateRequest request, int callerUserId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var summary = await _repository.GetSummaryAsync(userId)
            ?? throw new NotFoundException(UserResourceName, userId);

        IReadOnlyCollection<int>? newRoleSet = null;
        int? sysAdminRoleTypeId = null;
        if (request.OperatorRoleTypeIds is not null)
        {
            if (request.OperatorRoleTypeIds.Count == 0)
            {
                throw new ArgumentException(
                    "An operator account must keep at least one operator role. " +
                    "Deactivate the account instead of removing all of its roles.");
            }
            newRoleSet = await ValidateOperatorRoleSetAsync(request.OperatorRoleTypeIds);
            sysAdminRoleTypeId = summary.OperatorRoles
                .FirstOrDefault(r => r.Name == RoleTaxonomy.SystemAdminRoleName)?.RoleTypeId;
        }

        EnforceGuardRails(userId, request, summary, newRoleSet, sysAdminRoleTypeId, callerUserId);
        await EnforceLastSystemAdminProtectionAsync(userId, request, summary, newRoleSet, sysAdminRoleTypeId);

        var user = (await _repository.GetUserAsync(userId))!;
        if (request.IsActive is { } isActive)
        {
            user.ActiveStatus = isActive;
        }

        var rolesToRemove = Array.Empty<UserRole>() as IReadOnlyCollection<UserRole>;
        var roleIdsToAdd = Array.Empty<int>() as IReadOnlyCollection<int>;
        if (newRoleSet is not null)
        {
            var operatorRoleIds = (await _repository.GetRoleTypesAsync())
                .Where(r => !RoleTaxonomy.IsIdentityRole(r.Name))
                .Select(r => r.Id)
                .ToHashSet();
            var currentLinks = await _repository.GetUserRolesAsync(userId);

            // Replace-set semantics on OPERATOR links only — identity links are untouched (G4).
            rolesToRemove = currentLinks
                .Where(link => operatorRoleIds.Contains(link.RoleId) && !newRoleSet.Contains(link.RoleId))
                .ToList();
            var currentRoleIds = currentLinks.Select(link => link.RoleId).ToHashSet();
            roleIdsToAdd = newRoleSet.Where(id => !currentRoleIds.Contains(id)).ToList();
        }

        await _repository.ApplyUpdateAsync(user, rolesToRemove, roleIdsToAdd);
    }

    public async Task ResetPasswordAsync(int userId, AdminUserResetPasswordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await _repository.GetSummaryAsync(userId)
            ?? throw new NotFoundException(UserResourceName, userId);
        ValidateTempPassword(request.TempPassword, "tempPassword");

        var user = (await _repository.GetUserAsync(userId))!;
        user.PasswordHash = _passwordHasher.Hash(request.TempPassword!);
        user.MustChangePassword = true; // G3: next login forces AuthController.ChangePassword
        user.FailedLoginAttempts = 0;   // fresh start, mirroring manage-app-user.sql Section A
        user.LockoutUntil = null;
        await _repository.SaveUserAsync(user);
    }

    // ---- validation helpers ----------------------------------------------------

    private static void RequireField(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"The '{fieldName}' field is required.");
        }
    }

    private static void ValidateTempPassword(string? tempPassword, string fieldName)
    {
        RequireField(tempPassword, fieldName);
        if (tempPassword!.Length < PasswordPolicy.MinLength)
        {
            throw new ArgumentException(PasswordPolicy.TooShortMessage("temporary password"));
        }
    }

    /// <summary>Validates ids exist and are operator (non-identity) roles; returns the distinct set.</summary>
    private async Task<IReadOnlyCollection<int>> ValidateOperatorRoleSetAsync(IReadOnlyCollection<int>? roleTypeIds)
    {
        if (roleTypeIds is null || roleTypeIds.Count == 0)
        {
            throw new ArgumentException("At least one operator role ('operatorRoleTypeIds') is required.");
        }

        var requested = roleTypeIds.Distinct().ToList();
        var roleTypesById = (await _repository.GetRoleTypesAsync()).ToDictionary(r => r.Id);

        var unknown = requested.Where(id => !roleTypesById.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Unknown role type id(s): {string.Join(", ", unknown)}.");
        }

        var identity = requested.Where(id => RoleTaxonomy.IsIdentityRole(roleTypesById[id].Name)).ToList();
        if (identity.Count > 0)
        {
            throw new ArgumentException(
                "Identity roles (Patient, Therapist, Caretaker) cannot be assigned or removed here; " +
                "this API manages operator roles only.");
        }

        return requested;
    }

    // ---- G5 guard rails ---------------------------------------------------------

    private static void EnforceGuardRails(
        int userId,
        AdminUserUpdateRequest request,
        AdminUserSummary summary,
        IReadOnlyCollection<int>? newRoleSet,
        int? sysAdminRoleTypeId,
        int callerUserId)
    {
        if (request.IsActive == false && userId == callerUserId)
        {
            throw new ArgumentException("You cannot deactivate your own account.");
        }

        var holdsSysAdmin = summary.OperatorRoles.Any(r => r.Name == RoleTaxonomy.SystemAdminRoleName);
        var removesSysAdmin = newRoleSet is not null && holdsSysAdmin
            && (sysAdminRoleTypeId is null || !newRoleSet.Contains(sysAdminRoleTypeId.Value));
        if (removesSysAdmin && userId == callerUserId)
        {
            throw new ArgumentException("You cannot remove the SystemAdmin role from your own account.");
        }
    }

    private async Task EnforceLastSystemAdminProtectionAsync(
        int userId,
        AdminUserUpdateRequest request,
        AdminUserSummary summary,
        IReadOnlyCollection<int>? newRoleSet,
        int? sysAdminRoleTypeId)
    {
        var holdsSysAdmin = summary.OperatorRoles.Any(r => r.Name == RoleTaxonomy.SystemAdminRoleName);
        if (!holdsSysAdmin || !summary.IsActive)
        {
            return; // the change cannot reduce the active-SystemAdmin census
        }

        var staysActive = request.IsActive ?? true;
        var keepsSysAdmin = newRoleSet is null
            || (sysAdminRoleTypeId is not null && newRoleSet.Contains(sysAdminRoleTypeId.Value));
        if (staysActive && keepsSysAdmin)
        {
            return;
        }

        if (await _repository.CountOtherActiveSystemAdminsAsync(userId) == 0)
        {
            throw new ArgumentException(
                "This change would leave the system with no active SystemAdmin account.");
        }
    }
}
