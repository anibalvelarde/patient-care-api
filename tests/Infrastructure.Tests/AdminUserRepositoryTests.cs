using Xunit;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-41B: AdminUserRepository — operators-only visibility (G1), mixed-role identityRoles
/// hints (G4), paging + case-insensitive name/email search (WP-21/WP-30 pattern), and the
/// active-SYSADMIN census backing the G5 last-SYSADMIN guard rail.
/// </summary>
public class AdminUserRepositoryTests
{
    private const int TherapistRoleId = 1;
    private const int PatientRoleId = 2;
    private const int CaretakerRoleId = 4;
    private const int SysAdminRoleId = 5;
    private const int ManagerRoleId = 6;
    private const int FrontDeskRoleId = 7;

    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: name).Options;

    private static async Task Seed(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        context.RoleTypes.AddRange(
            new RoleType { Id = TherapistRoleId, Name = "Therapist", Abbreviation = "THERAPIST" },
            new RoleType { Id = PatientRoleId, Name = "Patient", Abbreviation = "PATIENT" },
            new RoleType { Id = CaretakerRoleId, Name = "Caretaker", Abbreviation = "CARETAKER" },
            new RoleType { Id = SysAdminRoleId, Name = "SystemAdmin", Abbreviation = "SYSADMIN" },
            new RoleType { Id = ManagerRoleId, Name = "Manager", Abbreviation = "MGR" },
            new RoleType { Id = FrontDeskRoleId, Name = "FrontDesk", Abbreviation = "FD" });

        context.Users.AddRange(
            // 10 ADMIN: active SYSADMIN (operator).
            new User { Id = 10, FirstName = "Sam", LastName = "Admin", Email = "sam.admin@neurocorp.test", ActiveStatus = true, MustChangePassword = false },
            // 11 FRONT: active FrontDesk who is ALSO a Patient (mixed role, G4).
            new User { Id = 11, FirstName = "Mora", LastName = "Front", Email = "mora.front@neurocorp.test", ActiveStatus = true, MustChangePassword = true },
            // 12 IENT: Patient identity only — INVISIBLE to this API (G1).
            new User { Id = 12, FirstName = "Pat", LastName = "Ient", Email = "pat.ient@familia.pa", ActiveStatus = true },
            // 13 GONE: INACTIVE Manager (still listed; isActive=false on the summary).
            new User { Id = 13, FirstName = "Ines", LastName = "Gone", Email = "ines.gone@neurocorp.test", ActiveStatus = false },
            // 14 NOROLE: user with no roles at all — invisible.
            new User { Id = 14, FirstName = "Nadie", LastName = "Norole", Email = "nadie@neurocorp.test", ActiveStatus = true });

        context.Set<UserRole>().AddRange(
            new UserRole { Id = 100, UserId = 10, RoleId = SysAdminRoleId },
            new UserRole { Id = 101, UserId = 11, RoleId = FrontDeskRoleId },
            new UserRole { Id = 102, UserId = 11, RoleId = PatientRoleId },
            new UserRole { Id = 103, UserId = 12, RoleId = PatientRoleId },
            new UserRole { Id = 104, UserId = 13, RoleId = ManagerRoleId });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPaged_ListsOperatorsOnly_IdentityOnlyAndRolelessInvisible()
    {
        var options = Options("Wp41AdminUsers_OperatorsOnly");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        var result = await repository.GetPagedAsync(search: null, page: 1, pageSize: 25);

        Assert.Equal(3, result.TotalCount);
        // Ordered by LastName, FirstName.
        Assert.Equal([10, 11, 13], result.Items.Select(u => u.UserId).ToArray());
        Assert.DoesNotContain(result.Items, u => u.UserId == 12 || u.UserId == 14);
    }

    [Fact]
    public async Task GetPaged_MixedRoleUser_CarriesIdentityRoleHints_AndOnlyOperatorRolesManageable()
    {
        var options = Options("Wp41AdminUsers_Mixed");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        var result = await repository.GetPagedAsync(search: "front", page: 1, pageSize: 25);

        var mora = Assert.Single(result.Items);
        Assert.Equal(11, mora.UserId);
        Assert.True(mora.MustChangePassword);
        var operatorRole = Assert.Single(mora.OperatorRoles);
        Assert.Equal(FrontDeskRoleId, operatorRole.RoleTypeId);
        Assert.Equal("FrontDesk", operatorRole.Name);
        Assert.Equal(["Patient"], mora.IdentityRoles.ToArray());
    }

    [Theory]
    [InlineData("SAM")]                       // first name, case-insensitive
    [InlineData("admin")]                     // last name
    [InlineData("SAM.ADMIN@NEUROCORP.TEST")]  // email
    public async Task GetPaged_Search_CaseInsensitive_OnNameAndEmail(string search)
    {
        var options = Options($"Wp41AdminUsers_Search_{search}");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        var result = await repository.GetPagedAsync(search, page: 1, pageSize: 25);

        var hit = Assert.Single(result.Items);
        Assert.Equal(10, hit.UserId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetPaged_PagingIsStable_TotalCountConstant()
    {
        var options = Options("Wp41AdminUsers_Paging");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        var page1 = await repository.GetPagedAsync(null, page: 1, pageSize: 2);
        var page2 = await repository.GetPagedAsync(null, page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal([10, 11], page1.Items.Select(u => u.UserId).ToArray());
        var last = Assert.Single(page2.Items);
        Assert.Equal(13, last.UserId);
        Assert.Equal(3, page2.TotalCount);
    }

    [Fact]
    public async Task GetSummary_IdentityOnlyOrUnknown_ReturnsNull()
    {
        var options = Options("Wp41AdminUsers_SummaryNull");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        Assert.Null(await repository.GetSummaryAsync(12));   // Patient identity only
        Assert.Null(await repository.GetSummaryAsync(14));   // no roles
        Assert.Null(await repository.GetSummaryAsync(9999)); // missing
    }

    [Fact]
    public async Task GetSummary_Operator_ReturnsContractShape()
    {
        var options = Options("Wp41AdminUsers_SummaryShape");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        var summary = await repository.GetSummaryAsync(13);

        Assert.NotNull(summary);
        Assert.Equal(13, summary!.UserId);
        Assert.Equal("Ines", summary.FirstName);
        Assert.Equal("Gone", summary.LastName);
        Assert.Equal("ines.gone@neurocorp.test", summary.Email);
        Assert.False(summary.IsActive);
        var role = Assert.Single(summary.OperatorRoles);
        Assert.Equal(ManagerRoleId, role.RoleTypeId);
        Assert.Equal("Manager", role.Name);
        Assert.Empty(summary.IdentityRoles);
    }

    [Fact]
    public async Task CountOtherActiveSystemAdmins_CountsOnlyActiveHolders_ExcludingTarget()
    {
        var options = Options("Wp41AdminUsers_SaCensus");
        await Seed(options);

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new AdminUserRepository(context);
            // User 10 is the only (active) SYSADMIN → excluding them leaves zero.
            Assert.Equal(0, await repository.CountOtherActiveSystemAdminsAsync(10));
            Assert.Equal(1, await repository.CountOtherActiveSystemAdminsAsync(11));
        }

        // Add an INACTIVE second SYSADMIN — must not count.
        using (var context = new ApplicationDbContext(options))
        {
            context.Users.Add(new User { Id = 15, FirstName = "Old", LastName = "Sa", Email = "old.sa@neurocorp.test", ActiveStatus = false });
            context.Set<UserRole>().Add(new UserRole { Id = 105, UserId = 15, RoleId = SysAdminRoleId });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new AdminUserRepository(context);
            Assert.Equal(0, await repository.CountOtherActiveSystemAdminsAsync(10));
        }
    }

    [Fact]
    public async Task CreateUserWithRoles_PersistsUserAndOperatorLinks()
    {
        var options = Options("Wp41AdminUsers_Create");
        await Seed(options);

        int newId;
        using (var context = new ApplicationDbContext(options))
        {
            var repository = new AdminUserRepository(context);
            var user = new User
            {
                FirstName = "Dana",
                LastName = "Ops",
                Email = "dana.ops@neurocorp.test",
                PasswordHash = "PBKDF2$temp",
                MustChangePassword = true,
                ActiveStatus = true,
            };
            newId = await repository.CreateUserWithRolesAsync(user, [ManagerRoleId, FrontDeskRoleId]);
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new AdminUserRepository(context);
            var summary = await repository.GetSummaryAsync(newId);
            Assert.NotNull(summary);
            Assert.True(summary!.IsActive);
            Assert.True(summary.MustChangePassword);
            Assert.Equal([ManagerRoleId, FrontDeskRoleId],
                summary.OperatorRoles.Select(r => r.RoleTypeId).OrderBy(id => id).ToArray());
        }
    }

    [Fact]
    public async Task ApplyUpdate_RemovesAndAddsLinks_AndFlushesUserEdits()
    {
        var options = Options("Wp41AdminUsers_ApplyUpdate");
        await Seed(options);

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new AdminUserRepository(context);
            var user = await repository.GetUserAsync(11);
            var roles = await repository.GetUserRolesAsync(11);
            var frontDeskLink = roles.Single(r => r.RoleId == FrontDeskRoleId);

            user!.ActiveStatus = false;
            await repository.ApplyUpdateAsync(user, [frontDeskLink], [ManagerRoleId]);
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new AdminUserRepository(context);
            var summary = await repository.GetSummaryAsync(11);
            Assert.NotNull(summary);
            Assert.False(summary!.IsActive);
            var role = Assert.Single(summary.OperatorRoles);
            Assert.Equal(ManagerRoleId, role.RoleTypeId);
            // Identity link untouched (G4).
            Assert.Equal(["Patient"], summary.IdentityRoles.ToArray());
        }
    }

    [Fact]
    public async Task EmailExists_MatchesExactEmail()
    {
        var options = Options("Wp41AdminUsers_EmailExists");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new AdminUserRepository(context);

        Assert.True(await repository.EmailExistsAsync("sam.admin@neurocorp.test"));
        Assert.False(await repository.EmailExistsAsync("nobody@neurocorp.test"));
    }
}
