using Xunit;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-30 (U2): PatientProfileRepository.GetPagedAsync / LookupAsync — the paged main list and
/// picker typeahead. InMemory validates shape/ordering/search/filter logic; SQL translation is
/// exercised against real MySQL via docs/wp-30-verification.md.
/// </summary>
public class PatientListPagingRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: name).Options;

    private static Patient NewPatient(int id, string first, string last, string? mrn, string? cedula, bool active) =>
        new()
        {
            Id = id,
            MedicalRecordNumber = mrn,
            Cedula = cedula,
            User = new User { FirstName = first, LastName = last, ActiveStatus = active },
        };

    private static async Task Seed(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        // ANDERSON: active, has a caretaker link (proves the page hydrates includes).
        // BENNET:   active, no cedula.
        // CORONADO: INACTIVE, passport-style cedula.
        // DOE:      active, TEMP MRN.
        context.Patients.AddRange(
            NewPatient(1, "Amy", "Anderson", "L24-0001", "8-111-222", active: true),
            NewPatient(2, "Neya", "Bennet", "L25-0034", null, active: true),
            NewPatient(3, "Luis", "Coronado", "L24-0201", "PA123456", active: false),
            NewPatient(4, "Jane", "Doe", "TEMP-4", null, active: true));
        context.Caretakers.Add(new Caretaker
        {
            Id = 11,
            User = new User { FirstName = "Rosa", LastName = "Gomez", ActiveStatus = true },
        });
        context.Set<PatientCaretaker>().Add(new PatientCaretaker
        {
            PatientId = 1,
            CaretakerId = 11,
            PrimaryCaretaker = true,
            RelationshipToPatient = "Mother",
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPaged_OrdersByName_AndHydratesCaretakers()
    {
        var options = Options("Wp30Patients_Ordering");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var result = await repository.GetPagedAsync(search: null, isActive: null, page: 1, pageSize: 30);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(
            new[] { "Anderson, Amy", "Bennet, Neya", "Coronado, Luis", "Doe, Jane" },
            result.Items.Select(p => p.PatientName).ToArray());

        var anderson = result.Items[0];
        Assert.Equal("8-111-222", anderson.Cedula);
        var caretaker = Assert.Single(anderson.Caretakers);
        Assert.Equal(11, caretaker.CaretakerId);
        Assert.True(caretaker.IsPrimaryCaretaker);
    }

    [Fact]
    public async Task GetPaged_PagingIsStable_TotalCountConstant()
    {
        var options = Options("Wp30Patients_Paging");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var page1 = await repository.GetPagedAsync(null, null, page: 1, pageSize: 2);
        var page2 = await repository.GetPagedAsync(null, null, page: 2, pageSize: 2);
        var beyond = await repository.GetPagedAsync(null, null, page: 9, pageSize: 2);

        Assert.Equal(4, page1.TotalCount);
        Assert.Equal(4, page2.TotalCount);
        Assert.Equal(new[] { 1, 2 }, page1.Items.Select(p => p.PatientId).ToArray());
        Assert.Equal(new[] { 3, 4 }, page2.Items.Select(p => p.PatientId).ToArray());
        Assert.Empty(beyond.Items);
        Assert.Equal(4, beyond.TotalCount);
    }

    [Fact]
    public async Task GetPaged_IsActiveFilter_BacksTheTabs()
    {
        var options = Options("Wp30Patients_IsActive");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var active = await repository.GetPagedAsync(null, isActive: true, page: 1, pageSize: 30);
        var inactive = await repository.GetPagedAsync(null, isActive: false, page: 1, pageSize: 30);

        Assert.Equal(3, active.TotalCount);
        Assert.DoesNotContain(active.Items, p => p.PatientId == 3);
        var coronado = Assert.Single(inactive.Items);
        Assert.Equal(3, coronado.PatientId);
    }

    [Theory]
    [InlineData("neya", 2)]       // first name, case-insensitive
    [InlineData("BENNET", 2)]     // last name, case-insensitive
    [InlineData("l25-0034", 2)]   // MRN, case-insensitive
    [InlineData("pa123", 3)]      // cedula|passport — the WP-30 addition (gate G2)
    public async Task GetPaged_SearchMatchesNameMrnAndCedula(string term, int expectedPatientId)
    {
        var options = Options($"Wp30Patients_Search_{term}");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        var result = await repository.GetPagedAsync(term, isActive: null, page: 1, pageSize: 30);

        var match = Assert.Single(result.Items);
        Assert.Equal(expectedPatientId, match.PatientId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Lookup_CapsResults_SlimShape_IncludesInactive()
    {
        var options = Options("Wp30Patients_Lookup");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new PatientProfileRepository(context);

        // "o" hits Anderson/Coronado/Doe — cap 2 keeps the first two by name order.
        var capped = await repository.LookupAsync("o", maxResults: 2);
        Assert.Equal(2, capped.Count);
        Assert.Equal("Anderson, Amy", capped[0].PatientName);
        Assert.Equal("L24-0001", capped[0].MedicalRecordNumber);
        Assert.Equal(1, capped[0].PatientId);

        // Parity with the old full-census pickers: inactive patients stay findable.
        var inactive = await repository.LookupAsync("coronado", maxResults: 20);
        var coronado = Assert.Single(inactive);
        Assert.Equal(3, coronado.PatientId);
    }
}
