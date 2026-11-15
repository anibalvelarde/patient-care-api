using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

// WP-62 (E2E F-5): on PUT, an omitted email keeps what's stored; "" or whitespace CLEARS it (NULL —
// never "", which the unique key would let only one user have); a value is stored trimmed.
public class Wp62EmailUpdateRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    private const string OnFile = "on.file@clinic.test";

    [Theory]
    [InlineData(null, OnFile)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Mixed@Case.test  ", "Mixed@Case.test")]
    public async Task PatientUpdate_Email(string? requested, string? expectedStored)
    {
        var options = Options($"Wp62Patient_{requested?.Length ?? -1}_{requested?.Trim()}");
        using (var ctx = new ApplicationDbContext(options))
        {
            ctx.Patients.Add(new Patient
            {
                Id = 1, Gender = "M", MedicalRecordNumber = "MRN-001",
                User = new User { Id = 1, FirstName = "John", LastName = "Doe", Email = OnFile, ActiveStatus = true },
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            await new PatientProfileRepository(ctx).UpdateAsync(1, 1,
                new PatientProfileUpdateRequest { FirstName = "John", LastName = "Doe", ActiveStatus = true, Email = requested });
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            Assert.Equal(expectedStored, (await ctx.Users.FindAsync(1))!.Email);
        }
    }

    [Theory]
    [InlineData(null, OnFile)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Mixed@Case.test  ", "Mixed@Case.test")]
    public async Task CaretakerUpdate_Email(string? requested, string? expectedStored)
    {
        var options = Options($"Wp62Caretaker_{requested?.Length ?? -1}_{requested?.Trim()}");
        using (var ctx = new ApplicationDbContext(options))
        {
            ctx.Caretakers.Add(new Caretaker
            {
                Id = 1,
                User = new User { Id = 1, FirstName = "Bob", LastName = "Jones", Email = OnFile, ActiveStatus = true },
            });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            await new CaretakerProfileRepository(ctx).UpdateAsync(1, 1,
                new CaretakerProfileUpdateRequest { FirstName = "Bob", LastName = "Jones", IsActive = true, Email = requested });
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            Assert.Equal(expectedStored, (await ctx.Users.FindAsync(1))!.Email);
        }
    }

    [Fact]
    public async Task PatientCaretakerUpdate_PersistsThePrimaryFlag()
    {
        var options = Options("Wp62LinkUpdate");
        using (var ctx = new ApplicationDbContext(options))
        {
            ctx.Set<PatientCaretaker>().Add(new PatientCaretaker { Id = 1, PatientId = 10, CaretakerId = 20, PrimaryCaretaker = false });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var repo = new PatientCaretakerRepository(ctx);
            var link = await repo.GetByCompositeKeyAsync(10, 20);
            link!.PrimaryCaretaker = true;
            await repo.UpdateAsync(link);
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            Assert.True((await new PatientCaretakerRepository(ctx).GetByCompositeKeyAsync(10, 20))!.PrimaryCaretaker);
        }
    }
}
