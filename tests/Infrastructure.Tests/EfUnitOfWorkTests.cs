using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Infrastructure.Data;

namespace Infrastructure.Tests;

// WP-36 (G1a): the mint-collision retry re-runs the WHOLE create unit of work on the same
// scoped DbContext. That only works if a failed unit of work leaves the context clean — the
// rolled-back writes must not stay tracked (an Added Patient carrying the collided MRN would
// otherwise be replayed by the retry's first SaveChanges and collide forever).
public class EfUnitOfWorkTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            // InMemory has no real transactions — suppress the warning; commit/rollback
            // mechanics are the provider's concern, tracker hygiene is what's under test.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ExecuteAsync_OnFailure_ClearsTrackedEntities_SoACallerRetryStartsClean()
    {
        using var context = CreateContext("Wp36UowClear_Failure");
        var uow = new EfUnitOfWork(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteAsync<int>(async () =>
        {
            context.Users.Add(new User { Id = 1, FirstName = "Jane", LastName = "Doe" });
            await context.SaveChangesAsync();
            context.Patients.Add(new Patient { Id = 1, MedicalRecordNumber = "NC26-0042" });
            throw new InvalidOperationException("simulated duplicate-key failure");
        }));

        // Nothing from the failed attempt may survive in the tracker — neither the saved-then-
        // rolled-back User nor the still-Added Patient.
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ExecuteAsync_OnSuccess_CommitsAndReturnsResult()
    {
        using var context = CreateContext("Wp36UowClear_Success");
        var uow = new EfUnitOfWork(context);

        var result = await uow.ExecuteAsync(async () =>
        {
            context.Users.Add(new User { Id = 1, FirstName = "Jane", LastName = "Doe" });
            await context.SaveChangesAsync();
            return 42;
        });

        Assert.Equal(42, result);
        Assert.Equal(1, await context.Users.CountAsync());
    }
}
