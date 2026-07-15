using Xunit;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-30 (U2): CaretakerProfileRepository.GetPagedAsync / LookupAsync — mirrors the patient-side
/// coverage (search fields per gate G2: name + email).
/// </summary>
public class CaretakerListPagingRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName: name).Options;

    private static Caretaker NewCaretaker(int id, string first, string last, string? email, bool active) =>
        new()
        {
            Id = id,
            User = new User { FirstName = first, LastName = last, Email = email, ActiveStatus = active },
        };

    private static async Task Seed(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        // GOMEZ:  active, linked to a patient (proves the page hydrates includes).
        // MENA:   INACTIVE.
        // ZAPATA: active, no email.
        context.Caretakers.AddRange(
            NewCaretaker(1, "Ana", "Gomez", "ana@familia.pa", active: true),
            NewCaretaker(2, "Luis", "Mena", "luis.mena@example.com", active: false),
            NewCaretaker(3, "Rosa", "Zapata", null, active: true));
        context.Patients.Add(new Patient
        {
            Id = 21,
            MedicalRecordNumber = "L24-0100",
            User = new User { FirstName = "Nino", LastName = "Gomez", ActiveStatus = true },
        });
        context.Set<PatientCaretaker>().Add(new PatientCaretaker
        {
            PatientId = 21,
            CaretakerId = 1,
            PrimaryCaretaker = true,
            RelationshipToPatient = "Mother",
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPaged_OrdersByName_AndHydratesPatients()
    {
        var options = Options("Wp30Caretakers_Ordering");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new CaretakerProfileRepository(context);

        var result = await repository.GetPagedAsync(search: null, isActive: null, page: 1, pageSize: 30);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            new[] { "Gomez, Ana", "Mena, Luis", "Zapata, Rosa" },
            result.Items.Select(c => c.CaretakerName).ToArray());

        var gomez = result.Items[0];
        var linked = Assert.Single(gomez.Patients);
        Assert.Equal(21, linked.PatientId);
        Assert.True(linked.IsPrimaryCaretaker);
    }

    [Fact]
    public async Task GetPaged_PagingIsStable_TotalCountConstant()
    {
        var options = Options("Wp30Caretakers_Paging");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new CaretakerProfileRepository(context);

        var page1 = await repository.GetPagedAsync(null, null, page: 1, pageSize: 2);
        var page2 = await repository.GetPagedAsync(null, null, page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(new[] { 1, 2 }, page1.Items.Select(c => c.CaretakerId).ToArray());
        var last = Assert.Single(page2.Items);
        Assert.Equal(3, last.CaretakerId);
        Assert.Equal(3, page2.TotalCount);
    }

    [Fact]
    public async Task GetPaged_IsActiveFilter_BacksTheTabs()
    {
        var options = Options("Wp30Caretakers_IsActive");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new CaretakerProfileRepository(context);

        var active = await repository.GetPagedAsync(null, isActive: true, page: 1, pageSize: 30);
        var inactive = await repository.GetPagedAsync(null, isActive: false, page: 1, pageSize: 30);

        Assert.Equal(2, active.TotalCount);
        Assert.DoesNotContain(active.Items, c => c.CaretakerId == 2);
        var mena = Assert.Single(inactive.Items);
        Assert.Equal(2, mena.CaretakerId);
    }

    [Theory]
    [InlineData("rosa", 3)]                    // first name
    [InlineData("GOMEZ", 1)]                   // last name, case-insensitive
    [InlineData("luis.mena@example.com", 2)]   // email (gate G2)
    public async Task GetPaged_SearchMatchesNameAndEmail(string term, int expectedCaretakerId)
    {
        var options = Options($"Wp30Caretakers_Search_{expectedCaretakerId}");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new CaretakerProfileRepository(context);

        var result = await repository.GetPagedAsync(term, isActive: null, page: 1, pageSize: 30);

        var match = Assert.Single(result.Items);
        Assert.Equal(expectedCaretakerId, match.CaretakerId);
    }

    [Fact]
    public async Task Lookup_CapsResults_SlimShape_IncludesInactive()
    {
        var options = Options("Wp30Caretakers_Lookup");
        await Seed(options);

        using var context = new ApplicationDbContext(options);
        var repository = new CaretakerProfileRepository(context);

        // "a" hits all 3 — cap 2 keeps the first two by name order.
        var capped = await repository.LookupAsync("a", maxResults: 2);
        Assert.Equal(2, capped.Count);
        Assert.Equal("Gomez, Ana", capped[0].CaretakerName);
        Assert.Equal(1, capped[0].CaretakerId);

        // Parity with the old full-census pickers: inactive caretakers stay findable.
        var inactive = await repository.LookupAsync("mena", maxResults: 20);
        var mena = Assert.Single(inactive);
        Assert.Equal(2, mena.CaretakerId);
    }
}
