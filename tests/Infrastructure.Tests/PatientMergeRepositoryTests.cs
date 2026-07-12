using Xunit;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-22 (F2): PatientMergeRepository — reads, deletes, identity retirement, and the merge-log
/// insert on the InMemory provider. The two ExecuteUpdate bulk repoints (ReassignSessionsAsync /
/// ReassignTreatmentPlansAsync) are NOT covered here — the InMemory provider does not support
/// ExecuteUpdate — and are verified against real MySQL via docs/patient-merge-wp22-verification.md
/// plus the batch runbook's invariant queries (same trade-off as WP-21's correlated subqueries).
/// </summary>
public class PatientMergeRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: name).Options;

    private static async Task Seed(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        // Patient 1 (user 10): the survivor — real caretaker 40 (user 400), 1 session, 1 plan.
        // Patient 2 (user 20): the duplicate — synthetic caretaker 55 (user 500), 2 sessions.
        context.Patients.AddRange(
            new Patient { Id = 1, MedicalRecordNumber = "L24-0312", User = new User { Id = 10, FirstName = "Juan", LastName = "Perez" } },
            new Patient { Id = 2, MedicalRecordNumber = "L24-0313", User = new User { Id = 20, FirstName = "Jaun", LastName = "Perez" } });
        context.Caretakers.AddRange(
            new Caretaker { Id = 40, Notes = string.Empty, User = new User { Id = 400, FirstName = "Maria", LastName = "Perez" } },
            new Caretaker { Id = 55, Notes = "SYNTHETIC placeholder caretaker (legacy-import backfill 2026-07) for patient L24-0313", User = new User { Id = 500, FirstName = "Jaun", LastName = "Perez-SH (LEGACY)" } });
        context.Set<PatientCaretaker>().AddRange(
            new PatientCaretaker { Id = 1040, PatientId = 1, CaretakerId = 40, PrimaryCaretaker = true },
            new PatientCaretaker { Id = 2055, PatientId = 2, CaretakerId = 55, PrimaryCaretaker = true });
        context.Set<UserRole>().AddRange(
            new UserRole { Id = 1, UserId = 20, RoleId = 2 },
            new UserRole { Id = 2, UserId = 500, RoleId = 4 });
        context.UserClaims.Add(new UserClaim { Id = 1, UserId = 20, ClaimType = "Permission", ClaimValue = "X" });
        context.TherapySessions.AddRange(
            new TherapySession { Id = 1, PatientId = 1, TherapistId = 1, SessionDate = new DateOnly(2026, 6, 1), SessionTime = new TimeOnly(9, 0) },
            new TherapySession { Id = 2, PatientId = 2, TherapistId = 1, SessionDate = new DateOnly(2026, 6, 2), SessionTime = new TimeOnly(9, 0) },
            new TherapySession { Id = 3, PatientId = 2, TherapistId = 1, SessionDate = new DateOnly(2026, 6, 3), SessionTime = new TimeOnly(9, 0) });
        context.TreatmentPlans.Add(new TreatmentPlan { Id = 1, PatientId = 1, DiscoverySessionId = 1, CreatedByTherapistId = 1 });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPatientWithUserAsync_LoadsUser_AndNullForMissing()
    {
        var options = Options("Wp22_GetPatient");
        await Seed(options);
        using var context = new ApplicationDbContext(options);
        var repo = new PatientMergeRepository(context);

        var patient = await repo.GetPatientWithUserAsync(2);
        var missing = await repo.GetPatientWithUserAsync(999);

        Assert.NotNull(patient);
        Assert.NotNull(patient!.User);
        Assert.Equal(20, patient.User!.Id);
        Assert.Equal("Perez, Jaun", patient.User.GetFullName());
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetCaretakerLinksAsync_IncludesCaretakerUserAndNotes()
    {
        var options = Options("Wp22_Links");
        await Seed(options);
        using var context = new ApplicationDbContext(options);
        var repo = new PatientMergeRepository(context);

        var links = await repo.GetCaretakerLinksAsync(2);

        var link = Assert.Single(links);
        Assert.NotNull(link.Caretaker);
        Assert.StartsWith("SYNTHETIC placeholder", link.Caretaker!.Notes);
        Assert.NotNull(link.Caretaker.User);
        Assert.Equal(500, link.Caretaker.User!.Id);
    }

    [Fact]
    public async Task Counts_RolesAndIdentityChecks_ReadCorrectly()
    {
        var options = Options("Wp22_Counts");
        await Seed(options);
        using var context = new ApplicationDbContext(options);
        var repo = new PatientMergeRepository(context);

        Assert.Equal(2, await repo.CountSessionsAsync(2));
        Assert.Equal(1, await repo.CountSessionsAsync(1));
        Assert.Equal(1, await repo.CountTreatmentPlansAsync(1));
        Assert.Equal(0, await repo.CountTreatmentPlansAsync(2));

        var roles = await repo.GetUserRolesAsync(20);
        Assert.Single(roles);
        Assert.Equal(2, roles[0].RoleId);

        Assert.False(await repo.IsTherapistUserAsync(20));
        Assert.False(await repo.IsCaretakerUserAsync(20));
        Assert.True(await repo.IsCaretakerUserAsync(500)); // the synthetic caretaker's user
    }

    [Fact]
    public async Task DeleteUserIdentityAsync_RemovesRolesClaimsAndUser()
    {
        var options = Options("Wp22_Retire");
        await Seed(options);
        using (var context = new ApplicationDbContext(options))
        {
            var repo = new PatientMergeRepository(context);
            await repo.DeleteUserIdentityAsync(20);
        }

        using var verify = new ApplicationDbContext(options);
        Assert.Empty(verify.Set<UserRole>().Where(r => r.UserId == 20));
        Assert.Empty(verify.UserClaims.Where(c => c.UserId == 20));
        Assert.Null(await verify.Users.FindAsync(20));
        // Other identities untouched.
        Assert.NotNull(await verify.Users.FindAsync(10));
        Assert.Single(verify.Set<UserRole>().Where(r => r.UserId == 500));
    }

    [Fact]
    public async Task DeleteUserIdentityAsync_MissingUser_IsNoOp()
    {
        var options = Options("Wp22_RetireMissing");
        await Seed(options);
        using var context = new ApplicationDbContext(options);
        var repo = new PatientMergeRepository(context);

        await repo.DeleteUserIdentityAsync(9999); // must not throw
    }

    [Fact]
    public async Task DeletePatient_CaretakerAndLink_Work()
    {
        var options = Options("Wp22_Deletes");
        await Seed(options);
        using (var context = new ApplicationDbContext(options))
        {
            var repo = new PatientMergeRepository(context);
            var links = await repo.GetCaretakerLinksAsync(2);
            await repo.DeleteCaretakerLinkAsync(links[0]);
            await repo.DeleteCaretakerAsync(links[0].Caretaker!);
            var patient = await repo.GetPatientWithUserAsync(2);
            await repo.DeletePatientAsync(patient!);
        }

        using var verify = new ApplicationDbContext(options);
        Assert.Empty(verify.Set<PatientCaretaker>().Where(pc => pc.PatientId == 2));
        Assert.Null(await verify.Caretakers.FindAsync(55));
        Assert.Null(await verify.Patients.FindAsync(2));
        Assert.NotNull(await verify.Patients.FindAsync(1)); // survivor untouched
    }

    [Fact]
    public async Task AddMergeLogAsync_PersistsRow_WithGeneratedId()
    {
        var options = Options("Wp22_Log");
        await Seed(options);
        int logId;
        using (var context = new ApplicationDbContext(options))
        {
            var repo = new PatientMergeRepository(context);
            var log = await repo.AddMergeLogAsync(new PatientMergeLog
            {
                SurvivorPatientId = 1,
                EliminatedPatientId = 2,
                EliminatedUserId = 20,
                EliminatedName = "Perez, Jaun",
                EliminatedMrn = "L24-0313",
                SessionsRemapped = 2,
                CaretakerLinksDeduped = 0,
                SyntheticCaretakersDeleted = 1,
                FieldsFilled = "DateOfBirth",
                MergedByUserId = 7,
            });
            logId = log.Id;
        }

        Assert.True(logId > 0);
        using var verify = new ApplicationDbContext(options);
        var saved = await verify.PatientMergeLogs.FindAsync(logId);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.EliminatedPatientId);
        Assert.Equal("Perez, Jaun", saved.EliminatedName);
        Assert.Equal(2, saved.SessionsRemapped);
        Assert.Equal(1, saved.SyntheticCaretakersDeleted);
    }

    [Fact]
    public async Task UpdateCaretakerLinkAsync_PersistsRepoint()
    {
        var options = Options("Wp22_Repoint");
        await Seed(options);
        using (var context = new ApplicationDbContext(options))
        {
            var repo = new PatientMergeRepository(context);
            var links = await repo.GetCaretakerLinksAsync(2);
            links[0].PatientId = 1;
            links[0].PrimaryCaretaker = false;
            await repo.UpdateCaretakerLinkAsync(links[0]);
        }

        using var verify = new ApplicationDbContext(options);
        var moved = await verify.Set<PatientCaretaker>().FindAsync(2055);
        Assert.Equal(1, moved!.PatientId);
        Assert.False(moved.PrimaryCaretaker);
    }
}
