using Xunit;
using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

// WP-36 (NP-1): slim scalar MAX-sequence query backing the NC{yy}-#### mint.
public class PatientRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    private static Patient PatientWithMrn(int id, string? mrn) => new()
    {
        Id = id,
        MedicalRecordNumber = mrn,
        User = new User { Id = id, FirstName = $"F{id}", LastName = $"L{id}" },
    };

    [Fact]
    public async Task GetMaxMrnSequenceAsync_ReturnsZero_WhenNoRowsMatchThePrefix()
    {
        var options = CreateInMemoryOptions("Wp36MaxSeq_Empty");
        using (var context = new ApplicationDbContext(options))
        {
            // Legacy-import, TEMP and NULL rows must never feed the NC sequence.
            context.Patients.AddRange(
                PatientWithMrn(1, "L26-0917"),
                PatientWithMrn(2, "TEMP-2"),
                PatientWithMrn(3, null));
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new PatientRepository(context);
            Assert.Equal(0, await repository.GetMaxMrnSequenceAsync("NC26-"));
        }
    }

    [Fact]
    public async Task GetMaxMrnSequenceAsync_ReturnsNumericMax_ScopedToThePrefixYear()
    {
        var options = CreateInMemoryOptions("Wp36MaxSeq_Max");
        using (var context = new ApplicationDbContext(options))
        {
            context.Patients.AddRange(
                PatientWithMrn(1, "NC26-0001"),
                PatientWithMrn(2, "NC26-0042"),
                PatientWithMrn(3, "NC26-0007"),
                PatientWithMrn(4, "NC25-0999"),   // prior year — sequence resets, must not count
                PatientWithMrn(5, "L26-9999"),    // legacy prefix never collides (plan key fact)
                PatientWithMrn(6, null));
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new PatientRepository(context);
            Assert.Equal(42, await repository.GetMaxMrnSequenceAsync("NC26-"));
            Assert.Equal(999, await repository.GetMaxMrnSequenceAsync("NC25-"));
        }
    }

    [Fact]
    public async Task GetMaxMrnSequenceAsync_IgnoresNonNumericSuffixes()
    {
        var options = CreateInMemoryOptions("Wp36MaxSeq_NonNumeric");
        using (var context = new ApplicationDbContext(options))
        {
            // Hand-typed junk under the prefix (pre-WP-36 MRNs were free text) must not crash
            // the mint — parseable rows win, junk-only would yield 0.
            context.Patients.AddRange(
                PatientWithMrn(1, "NC26-XXXX"),
                PatientWithMrn(2, "NC26-0005"));
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var repository = new PatientRepository(context);
            Assert.Equal(5, await repository.GetMaxMrnSequenceAsync("NC26-"));
        }
    }
}
