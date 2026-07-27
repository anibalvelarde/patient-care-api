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
/// WP-21 (F1): PatientProfileRepository.GetSessionHistoryAsync — the paged patient-summary query
/// behind the Session History tab's patient list. The InMemory provider validates the
/// shape/ordering/search logic; the SQL translation itself is exercised against real MySQL via
/// the curl verification doc (correlated scalar subqueries translate on both providers).
/// </summary>
public class PatientSessionHistoryRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: name).Options;

    private static Patient NewPatient(int id, string first, string last, string? mrn = null) =>
        new() { Id = id, MedicalRecordNumber = mrn, User = new User { FirstName = first, LastName = last } };

    private static TherapySession NewSession(int id, int patientId, DateOnly date) => new()
    {
        Id = id,
        PatientId = patientId,
        TherapistId = 1,
        SessionDate = date,
        SessionTime = new TimeOnly(9, 0),
        Amount = 100,
    };

    private static async Task Seed(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        context.AppointmentStatuses.Add(new AppointmentStatus { Id = 4, Name = "Completed", Description = "Session took place" });

        // ANDERSON: 3 sessions, newest 2026-07-01 (most recent overall).
        // BENNET:   1 session on 2026-06-15.
        // CORONADO: 2 sessions, newest 2026-06-15 — ties BENNET on date, breaks on last name.
        // DOE:      zero sessions — must sort last with null date / 0 count.
        context.Patients.AddRange(
            NewPatient(1, "Amy", "Anderson", "L24-0001"),
            NewPatient(2, "Neya", "Bennet", "L25-0034"),
            NewPatient(3, "Luis", "Coronado", "L24-0201"),
            NewPatient(4, "Jane", "Doe", "TEMP-4"));
        context.TherapySessions.AddRange(
            NewSession(1, 1, new DateOnly(2026, 3, 1)),
            NewSession(2, 1, new DateOnly(2026, 7, 1)),
            NewSession(3, 1, new DateOnly(2026, 5, 1)),
            NewSession(4, 2, new DateOnly(2026, 6, 15)),
            NewSession(5, 3, new DateOnly(2026, 6, 15)),
            NewSession(6, 3, new DateOnly(2026, 1, 10)));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task OrdersByRecency_ZeroSessionPatientsLast_WithAggregates()
    {
        var options = Options("Wp21Summary_Ordering");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var result = await repository.GetSessionHistoryAsync(search: null, page: 1, pageSize: 30);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(
            new[] { "Anderson, Amy", "Bennet, Neya", "Coronado, Luis", "Doe, Jane" },
            result.Items.Select(r => r.PatientName).ToArray());

        var anderson = result.Items[0];
        Assert.Equal(1, anderson.PatientId);
        Assert.Equal("L24-0001", anderson.MedicalRecordNumber);
        Assert.Equal(new DateOnly(2026, 7, 1), anderson.LastSessionDate);
        Assert.Equal(3, anderson.TotalSessions);

        var doe = result.Items[3];
        Assert.Null(doe.LastSessionDate);
        Assert.Equal(0, doe.TotalSessions);
    }

    // WP-35 (SH-1): FirstSessionDate = MIN(SessionDate) of ANY status (owner ruling G1 —
    // consistent with TotalSessions), null-safe for zero-session patients.
    [Fact]
    public async Task Summary_CarriesFirstAndLastSessionDates_NullSafeForZeroSessions()
    {
        var options = Options("Wp35Summary_FirstSessionDate");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var result = await repository.GetSessionHistoryAsync(search: null, page: 1, pageSize: 30);

        // ANDERSON: 3 sessions — first 2026-03-01, last 2026-07-01.
        var anderson = result.Items.Single(r => r.PatientId == 1);
        Assert.Equal(new DateOnly(2026, 3, 1), anderson.FirstSessionDate);
        Assert.Equal(new DateOnly(2026, 7, 1), anderson.LastSessionDate);

        // BENNET: single session — first == last.
        var bennet = result.Items.Single(r => r.PatientId == 2);
        Assert.Equal(new DateOnly(2026, 6, 15), bennet.FirstSessionDate);
        Assert.Equal(bennet.LastSessionDate, bennet.FirstSessionDate);

        // DOE: zero sessions — both dates null, count 0.
        var doe = result.Items.Single(r => r.PatientId == 4);
        Assert.Null(doe.FirstSessionDate);
        Assert.Null(doe.LastSessionDate);
        Assert.Equal(0, doe.TotalSessions);
    }

    [Fact]
    public async Task Paging_IsStable_AndTotalCountConstant()
    {
        var options = Options("Wp21Summary_Paging");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var page1 = await repository.GetSessionHistoryAsync(null, page: 1, pageSize: 2);
        var page2 = await repository.GetSessionHistoryAsync(null, page: 2, pageSize: 2);

        Assert.Equal(4, page1.TotalCount);
        Assert.Equal(4, page2.TotalCount);
        Assert.Equal(new[] { 1, 2 }, page1.Items.Select(r => r.PatientId).ToArray());
        Assert.Equal(new[] { 3, 4 }, page2.Items.Select(r => r.PatientId).ToArray());

        var beyond = await repository.GetSessionHistoryAsync(null, page: 9, pageSize: 2);
        Assert.Empty(beyond.Items);
        Assert.Equal(4, beyond.TotalCount);
    }

    [Theory]
    [InlineData("neya", 2)]        // first name, case-insensitive
    [InlineData("BENNET", 2)]      // last name, case-insensitive
    [InlineData("l25-0034", 2)]    // MRN, case-insensitive
    public async Task Search_MatchesNameAndMrn_CaseInsensitively(string term, int expectedPatientId)
    {
        var options = Options($"Wp21Summary_Search_{term}");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var result = await repository.GetSessionHistoryAsync(term, page: 1, pageSize: 30);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(expectedPatientId, Assert.Single(result.Items).PatientId);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyWithZeroCount()
    {
        var options = Options("Wp21Summary_SearchEmpty");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var result = await repository.GetSessionHistoryAsync("zzz-no-such-patient", page: 1, pageSize: 30);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }
}
