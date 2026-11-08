using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Neurocorp.Api.Core.BusinessObjects.Payments;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Configurations.HealthChecks;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests;

/// <summary>
/// WP-58: the payment service against the real repositories (InMemory), and the ledgerReconcile
/// health check. InMemory has no real transactions, so rollback itself is proven on MySQL in the
/// local E2E run; here we prove the invariant <c>AmountPaid = LegacyAmountPaid + SUM(allocations)</c>
/// end to end and that the health check sees a break in it.
/// </summary>
public class Wp58MoneyIntegrityTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var ctx = new ApplicationDbContext(options);
        ctx.Set<Caretaker>().Add(new Caretaker { Id = 1135, Notes = string.Empty, User = new User { Id = 50, FirstName = "Juliana", LastName = "Duarte" } });
        ctx.Set<PaymentType>().Add(new PaymentType { Id = 4, Name = "Cash", Abbreviation = "CASH" });
        ctx.Set<Patient>().Add(new Patient { Id = 871, User = new User { Id = 871, FirstName = "Hernan", LastName = "Vergara" } });
        ctx.Set<PatientCaretaker>().Add(new PatientCaretaker { Id = 1, PatientId = 871, CaretakerId = 1135 });
        // Legacy-imported session: $175 billed, $50 paid per the spreadsheet, no Payment rows.
        ctx.TherapySessions.Add(new TherapySession
        {
            Id = 11042, PatientId = 871, TherapistId = 9, Amount = 175m, DiscountAmount = 0m,
            AmountPaid = 50m, LegacyAmountPaid = 50m, Notes = "", TherapyTypes = "NP",
        });
        await ctx.SaveChangesAsync();
    }

    private static PaymentRecordService Service(ApplicationDbContext ctx) => new(
        NullLogger<PaymentRecordService>.Instance,
        new PaymentRepository(ctx), new SessionPaymentRepository(ctx), new TherapySessionRepository(ctx),
        new PatientCaretakerRepository(ctx), new EfRepository<PaymentType>(ctx), new CaretakerRepository(ctx),
        new EfUnitOfWork(ctx));

    private static LedgerReconcileHealthCheck Check(ApplicationDbContext ctx) =>
        new(ctx, NullLogger<LedgerReconcileHealthCheck>.Instance);

    [Fact]
    public async Task PaymentOnALegacySession_AddsToTheBaseline_AndTheLedgerStaysHealthy()
    {
        var options = NewOptions(nameof(PaymentOnALegacySession_AddsToTheBaseline_AndTheLedgerStaysHealthy));
        await SeedAsync(options);

        using (var ctx = new ApplicationDbContext(options))
        {
            Assert.Equal(HealthStatus.Healthy, (await Check(ctx).CheckHealthAsync(new HealthCheckContext())).Status);

            await Service(ctx).CreateAsync(new PaymentRecordRequest
            {
                Amount = 20m, PaymentDate = new DateTime(2026, 11, 8), CaretakerId = 1135, PaymentTypeId = 4,
                SessionAllocations = { new SessionAllocationItem { SessionId = 11042, AmountAllocated = 20m } },
            });
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var session = await ctx.TherapySessions.SingleAsync(s => s.Id == 11042);
            Assert.Equal(70m, session.AmountPaid);       // 50 legacy + 20 ledger
            Assert.Equal(50m, session.LegacyAmountPaid); // baseline untouched
            Assert.Equal(HealthStatus.Healthy, (await Check(ctx).CheckHealthAsync(new HealthCheckContext())).Status);
        }
    }

    [Fact]
    public async Task HealthCheck_IsDegraded_AndNamesTheSession_WhenAmountPaidDriftsFromTheLedger()
    {
        // The F-18 shape: something wrote AmountPaid directly (a stale client used to zero it).
        var options = NewOptions(nameof(HealthCheck_IsDegraded_AndNamesTheSession_WhenAmountPaidDriftsFromTheLedger));
        await SeedAsync(options);
        using (var ctx = new ApplicationDbContext(options))
        {
            var session = await ctx.TherapySessions.SingleAsync(s => s.Id == 11042);
            session.AmountPaid = 0m;
            await ctx.SaveChangesAsync();
        }

        using (var ctx = new ApplicationDbContext(options))
        {
            var result = await Check(ctx).CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            Assert.Equal(1, result.Data["mismatchCount"]);
            Assert.Equal("11042", result.Data["sampleSessionIds"]);
        }
    }
}
