using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.AdminUsers;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-41B spec tests (red-first): create semantics (G3), replace-role-set semantics, identity
/// role protection (G1/G4), reset-password, and ALL G5 guard rails including the
/// last-SYSADMIN edge. Contract: patient-care-super/_contracts/admin-users-api.md.
/// </summary>
public class AdminUserServiceTests
{
    // RoleType fixture — ids mirror the V017 seed shape (identity: 1/2/4; operators: rest).
    private const int TherapistRoleId = 1;
    private const int PatientRoleId = 2;
    private const int CaretakerRoleId = 4;
    private const int SysAdminRoleId = 5;
    private const int ManagerRoleId = 6;
    private const int OwnerRoleId = 7;

    private static IReadOnlyList<RoleType> AllRoleTypes() =>
    [
        new RoleType { Id = TherapistRoleId, Name = "Therapist", Abbreviation = "THERAPIST" },
        new RoleType { Id = PatientRoleId, Name = "Patient", Abbreviation = "PATIENT" },
        new RoleType { Id = CaretakerRoleId, Name = "Caretaker", Abbreviation = "CARETAKER" },
        new RoleType { Id = SysAdminRoleId, Name = "SystemAdmin", Abbreviation = "SYSADMIN" },
        new RoleType { Id = ManagerRoleId, Name = "Manager", Abbreviation = "MGR" },
        new RoleType { Id = OwnerRoleId, Name = "Owner", Abbreviation = "OWN" },
    ];

    private readonly Mock<IAdminUserRepository> _repo = new();
    private readonly Mock<IPasswordHasher> _hasher = new();

    private AdminUserService NewService()
    {
        _repo.Setup(r => r.GetRoleTypesAsync()).ReturnsAsync(AllRoleTypes());
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"PBKDF2${p}");
        return new AdminUserService(_repo.Object, _hasher.Object);
    }

    private static AdminUserCreateRequest ValidCreate() => new()
    {
        FirstName = "Dana",
        LastName = "Ops",
        Email = " dana.ops@neurocorp.test ",
        TempPassword = "Temp1234!",
        OperatorRoleTypeIds = [ManagerRoleId],
    };

    private static AdminUserSummary SummaryFor(int userId, bool isActive = true, params (int Id, string Name)[] operatorRoles) => new()
    {
        UserId = userId,
        IsActive = isActive,
        OperatorRoles = operatorRoles.Select(r => new AdminUserRoleInfo { RoleTypeId = r.Id, Name = r.Name }).ToList(),
    };

    // ── Create ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Valid_CreatesActiveUser_MustChange_TrimmedEmail_RolesLinked()
    {
        var svc = NewService();
        User? persisted = null;
        IReadOnlyCollection<int>? persistedRoles = null;
        _repo.Setup(r => r.EmailExistsAsync("dana.ops@neurocorp.test")).ReturnsAsync(false);
        _repo.Setup(r => r.CreateUserWithRolesAsync(It.IsAny<User>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Callback<User, IReadOnlyCollection<int>>((u, roles) => { persisted = u; persistedRoles = roles; })
            .ReturnsAsync(42);
        _repo.Setup(r => r.GetSummaryAsync(42))
            .ReturnsAsync(SummaryFor(42, true, (ManagerRoleId, "Manager")));

        var result = await svc.CreateAsync(ValidCreate());

        Assert.NotNull(persisted);
        Assert.True(persisted!.ActiveStatus);
        Assert.True(persisted.MustChangePassword);
        Assert.Equal("dana.ops@neurocorp.test", persisted.Email);
        Assert.Equal("PBKDF2$Temp1234!", persisted.PasswordHash);
        Assert.Equal([ManagerRoleId], persistedRoles!.ToArray());
        Assert.Equal(42, result.UserId);
    }

    [Theory]
    [InlineData(null, "Ops", "a@b.c", "Temp1234!")]
    [InlineData("Dana", null, "a@b.c", "Temp1234!")]
    [InlineData("Dana", "Ops", null, "Temp1234!")]
    [InlineData("Dana", "Ops", "a@b.c", null)]
    [InlineData(" ", "Ops", "a@b.c", "Temp1234!")]
    public async Task Create_MissingRequiredField_Throws400(string? first, string? last, string? email, string? temp)
    {
        var svc = NewService();
        var request = new AdminUserCreateRequest
        {
            FirstName = first, LastName = last, Email = email, TempPassword = temp,
            OperatorRoleTypeIds = [ManagerRoleId],
        };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(request));
    }

    [Fact]
    public async Task Create_TempPasswordBelowPolicyMinimum_Throws400()
    {
        var svc = NewService();
        var request = ValidCreate();
        request.TempPassword = "short7!";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(request));
        Assert.Contains("at least 8 characters", ex.Message);
    }

    [Fact]
    public async Task Create_NoOperatorRoles_Throws400()
    {
        var svc = NewService();
        var request = ValidCreate();
        request.OperatorRoleTypeIds = [];

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(request));
    }

    [Fact]
    public async Task Create_IdentityRoleId_Throws400()
    {
        var svc = NewService();
        var request = ValidCreate();
        request.OperatorRoleTypeIds = [ManagerRoleId, PatientRoleId];

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(request));
        Assert.Contains("Identity roles", ex.Message);
    }

    [Fact]
    public async Task Create_UnknownRoleId_Throws400()
    {
        var svc = NewService();
        var request = ValidCreate();
        request.OperatorRoleTypeIds = [999];

        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(request));
    }

    [Fact]
    public async Task Create_DuplicateEmail_Throws409()
    {
        var svc = NewService();
        _repo.Setup(r => r.EmailExistsAsync("dana.ops@neurocorp.test")).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => svc.CreateAsync(ValidCreate()));
        Assert.Equal("A user with this email address already exists.", ex.Message);
    }

    // ── Get by id ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_MissingOrIdentityOnly_Throws404()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync((AdminUserSummary?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => svc.GetByIdAsync(7));
    }

    // ── Update: replace-role-set semantics ─────────────────────────────────────

    [Fact]
    public async Task Update_UnknownUserOrIdentityOnly_Throws404()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync((AdminUserSummary?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => svc.UpdateAsync(7, new AdminUserUpdateRequest { IsActive = false }, callerUserId: 1));
    }

    [Fact]
    public async Task Update_EmptyRoleList_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpdateAsync(7, new AdminUserUpdateRequest { OperatorRoleTypeIds = [] }, callerUserId: 1));
        Assert.Contains("at least one operator role", ex.Message);
    }

    [Fact]
    public async Task Update_IdentityRoleIdInList_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpdateAsync(7, new AdminUserUpdateRequest { OperatorRoleTypeIds = [CaretakerRoleId] }, callerUserId: 1));
    }

    [Fact]
    public async Task Update_AllFieldsOmitted_ChangesNothing()
    {
        var svc = NewService();
        var user = new User { Id = 7, ActiveStatus = true };
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.GetUserAsync(7)).ReturnsAsync(user);
        _repo.Setup(r => r.GetUserRolesAsync(7)).ReturnsAsync([]);
        _repo.Setup(r => r.ApplyUpdateAsync(user, It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Returns(Task.CompletedTask);

        await svc.UpdateAsync(7, new AdminUserUpdateRequest(), callerUserId: 1);

        Assert.True(user.ActiveStatus);
        _repo.Verify(r => r.ApplyUpdateAsync(user,
            It.Is<IReadOnlyCollection<UserRole>>(rem => rem.Count == 0),
            It.Is<IReadOnlyCollection<int>>(add => add.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task Update_ReplacesOperatorRoles_IdentityLinksUntouched()
    {
        // User 7 is Manager + Patient (mixed role, G4). New set = [Owner]:
        // remove Manager link, add Owner link, NEVER touch the Patient link.
        var svc = NewService();
        var user = new User { Id = 7, ActiveStatus = true };
        var managerLink = new UserRole { Id = 100, UserId = 7, RoleId = ManagerRoleId };
        var patientLink = new UserRole { Id = 101, UserId = 7, RoleId = PatientRoleId };
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.GetUserAsync(7)).ReturnsAsync(user);
        _repo.Setup(r => r.GetUserRolesAsync(7)).ReturnsAsync([managerLink, patientLink]);
        IReadOnlyCollection<UserRole>? removed = null;
        IReadOnlyCollection<int>? added = null;
        _repo.Setup(r => r.ApplyUpdateAsync(user, It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Callback<User, IReadOnlyCollection<UserRole>, IReadOnlyCollection<int>>((_, rem, add) => { removed = rem; added = add; })
            .Returns(Task.CompletedTask);

        await svc.UpdateAsync(7, new AdminUserUpdateRequest { OperatorRoleTypeIds = [OwnerRoleId] }, callerUserId: 1);

        Assert.Equal([managerLink], removed!.ToArray());
        Assert.Equal([OwnerRoleId], added!.ToArray());
    }

    [Fact]
    public async Task Update_RoleAlreadyHeld_NotReAdded()
    {
        var svc = NewService();
        var user = new User { Id = 7, ActiveStatus = true };
        var managerLink = new UserRole { Id = 100, UserId = 7, RoleId = ManagerRoleId };
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.GetUserAsync(7)).ReturnsAsync(user);
        _repo.Setup(r => r.GetUserRolesAsync(7)).ReturnsAsync([managerLink]);
        _repo.Setup(r => r.ApplyUpdateAsync(user, It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Returns(Task.CompletedTask);

        await svc.UpdateAsync(7, new AdminUserUpdateRequest { OperatorRoleTypeIds = [ManagerRoleId, OwnerRoleId] }, callerUserId: 1);

        _repo.Verify(r => r.ApplyUpdateAsync(user,
            It.Is<IReadOnlyCollection<UserRole>>(rem => rem.Count == 0),
            It.Is<IReadOnlyCollection<int>>(add => add.SequenceEqual(new[] { OwnerRoleId }))), Times.Once);
    }

    [Fact]
    public async Task Update_SetsActiveStatus_WhenProvided()
    {
        var svc = NewService();
        var user = new User { Id = 7, ActiveStatus = true };
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.GetUserAsync(7)).ReturnsAsync(user);
        _repo.Setup(r => r.GetUserRolesAsync(7)).ReturnsAsync([]);
        _repo.Setup(r => r.ApplyUpdateAsync(user, It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Returns(Task.CompletedTask);

        await svc.UpdateAsync(7, new AdminUserUpdateRequest { IsActive = false }, callerUserId: 1);

        Assert.False(user.ActiveStatus);
    }

    // ── Update: G5 guard rails ─────────────────────────────────────────────────

    [Fact]
    public async Task Update_SelfDeactivate_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(9)).ReturnsAsync(SummaryFor(9, true, (SysAdminRoleId, "SystemAdmin")));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpdateAsync(9, new AdminUserUpdateRequest { IsActive = false }, callerUserId: 9));
        Assert.Contains("your own account", ex.Message);
    }

    [Fact]
    public async Task Update_SelfDemoteFromSysAdmin_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(9))
            .ReturnsAsync(SummaryFor(9, true, (SysAdminRoleId, "SystemAdmin"), (ManagerRoleId, "Manager")));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpdateAsync(9, new AdminUserUpdateRequest { OperatorRoleTypeIds = [ManagerRoleId] }, callerUserId: 9));
        Assert.Contains("SystemAdmin", ex.Message);
    }

    [Fact]
    public async Task Update_DeactivateLastActiveSysAdmin_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(9)).ReturnsAsync(SummaryFor(9, true, (SysAdminRoleId, "SystemAdmin")));
        _repo.Setup(r => r.CountOtherActiveSystemAdminsAsync(9)).ReturnsAsync(0);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpdateAsync(9, new AdminUserUpdateRequest { IsActive = false }, callerUserId: 1));
        Assert.Contains("no active SystemAdmin", ex.Message);
    }

    [Fact]
    public async Task Update_RemoveSysAdminRoleFromLastActiveSysAdmin_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(9))
            .ReturnsAsync(SummaryFor(9, true, (SysAdminRoleId, "SystemAdmin"), (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.CountOtherActiveSystemAdminsAsync(9)).ReturnsAsync(0);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.UpdateAsync(9, new AdminUserUpdateRequest { OperatorRoleTypeIds = [ManagerRoleId] }, callerUserId: 1));
        Assert.Contains("no active SystemAdmin", ex.Message);
    }

    [Fact]
    public async Task Update_DeactivateSysAdmin_WhenAnotherActiveSysAdminExists_Succeeds()
    {
        var svc = NewService();
        var user = new User { Id = 9, ActiveStatus = true };
        _repo.Setup(r => r.GetSummaryAsync(9)).ReturnsAsync(SummaryFor(9, true, (SysAdminRoleId, "SystemAdmin")));
        _repo.Setup(r => r.CountOtherActiveSystemAdminsAsync(9)).ReturnsAsync(1);
        _repo.Setup(r => r.GetUserAsync(9)).ReturnsAsync(user);
        _repo.Setup(r => r.GetUserRolesAsync(9)).ReturnsAsync([]);
        _repo.Setup(r => r.ApplyUpdateAsync(user, It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Returns(Task.CompletedTask);

        await svc.UpdateAsync(9, new AdminUserUpdateRequest { IsActive = false }, callerUserId: 1);

        Assert.False(user.ActiveStatus);
    }

    [Fact]
    public async Task Update_DeactivateNonSysAdmin_NeverConsultsSysAdminCount()
    {
        var svc = NewService();
        var user = new User { Id = 7, ActiveStatus = true };
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.GetUserAsync(7)).ReturnsAsync(user);
        _repo.Setup(r => r.GetUserRolesAsync(7)).ReturnsAsync([]);
        _repo.Setup(r => r.ApplyUpdateAsync(user, It.IsAny<IReadOnlyCollection<UserRole>>(), It.IsAny<IReadOnlyCollection<int>>()))
            .Returns(Task.CompletedTask);

        await svc.UpdateAsync(7, new AdminUserUpdateRequest { IsActive = false }, callerUserId: 1);

        _repo.Verify(r => r.CountOtherActiveSystemAdminsAsync(It.IsAny<int>()), Times.Never);
    }

    // ── Reset password ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_SetsHash_MustChange_AndClearsLockout()
    {
        var svc = NewService();
        var user = new User
        {
            Id = 7,
            ActiveStatus = true,
            PasswordHash = "PBKDF2$old",
            MustChangePassword = false,
            FailedLoginAttempts = 4,
            LockoutUntil = DateTime.UtcNow.AddMinutes(10),
        };
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));
        _repo.Setup(r => r.GetUserAsync(7)).ReturnsAsync(user);
        _repo.Setup(r => r.SaveUserAsync(user)).Returns(Task.CompletedTask);

        await svc.ResetPasswordAsync(7, new AdminUserResetPasswordRequest { TempPassword = "NewTemp99" });

        Assert.Equal("PBKDF2$NewTemp99", user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutUntil);
        _repo.Verify(r => r.SaveUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_BelowPolicyMinimum_Throws400()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync(SummaryFor(7, true, (ManagerRoleId, "Manager")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ResetPasswordAsync(7, new AdminUserResetPasswordRequest { TempPassword = "short7!" }));
    }

    [Fact]
    public async Task ResetPassword_MissingOrIdentityOnlyUser_Throws404()
    {
        var svc = NewService();
        _repo.Setup(r => r.GetSummaryAsync(7)).ReturnsAsync((AdminUserSummary?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => svc.ResetPasswordAsync(7, new AdminUserResetPasswordRequest { TempPassword = "NewTemp99" }));
    }
}
