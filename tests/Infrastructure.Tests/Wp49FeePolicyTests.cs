using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;
using Xunit;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-49 (BR3): end-to-end guards for the late fee through the real handler + repositories.
///
/// The headline test here is <see cref="DiscountEdit_OnAFeeBearingSession_KeepsTheFeeInGrossProfit"/>.
/// It exists because of Finding 1, the highest-risk defect in this WP: the discount-edit
/// recompute rebuilds GrossProfit from scratch, so a forgotten late-fee argument leaves the fee
/// collectible in AmountDue() while it silently disappears from clinic P&L — correct on the day
/// the fee is applied, wrong forever after, with no exception and no other failing test.
/// </summary>
public class Wp49FeePolicyTests
{
    private static DbContextOptions<ApplicationDbContext> NewOptions(string name)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"Wp49_{name}_{Guid.NewGuid():N}")
            .Options;

    private static async Task SeedSessionAsync(
        DbContextOptions<ApplicationDbContext> options,
        decimal amount = 125m, decimal discount = 0m, decimal provider = 0m, decimal gross = 125m,
        decimal amountPaid = 0m, decimal? lateFee = null, DateOnly? lateFeeAppliedOn = null,
        decimal? onSite = null, int statusId = 4, string notes = "")
    {
        using var context = new ApplicationDbContext(options);
        context.AppointmentStatuses.AddRange(
            new AppointmentStatus { Id = 2, Name = "Confirmed", Description = "Confirmed" },
            new AppointmentStatus { Id = 3, Name = "Cancelled", Description = "Cancelled" },
            new AppointmentStatus { Id = 4, Name = "Completed", Description = "Completed" },
            new AppointmentStatus { Id = 5, Name = "NoShow", Description = "No show" });

        context.TherapySessions.Add(new TherapySession
        {
            Id = 1,
            Patient = new Patient { Id = 1, User = new User { Id = 1, FirstName = "John", LastName = "Doe" } },
            Therapist = new Therapist { Id = 1, User = new User { Id = 2, FirstName = "Jane", LastName = "Smith" } },
            SessionDate = new DateOnly(2026, 7, 1),
            SessionTime = new TimeOnly(10, 0),
            Duration = 60,
            Amount = amount,
            DiscountAmount = discount,
            ProviderAmount = provider,
            GrossProfit = gross,
            AmountPaid = amountPaid,
            LateFeeAmount = lateFee,
            LateFeeAppliedOn = lateFeeAppliedOn,
            OnSiteChargeAmount = onSite,
            AppointmentStatusId = statusId,
            Notes = notes,
        });

        await context.SaveChangesAsync();
    }

    private static SessionEventHandler CreateHandler(ApplicationDbContext context, decimal therapistFee = 0m)
    {
        var patientService = new Mock<IPatientProfileService>();
        patientService.Setup(s => s.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "Doe, John", HasSenadisDiscount = false });
        var therapistService = new Mock<ITherapistProfileService>();
        therapistService.Setup(s => s.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new TherapistProfile { TherapistId = 1, TherapistName = "Smith, Jane", FeePerSession = therapistFee });

        return new SessionEventHandler(
            Mock.Of<ILogger<SessionEventHandler>>(),
            new SessionEventRepository(context),
            new TherapySessionRepository(context),
            therapistService.Object,
            patientService.Object,
            Mock.Of<IRepository<SpecialtyType>>(),
            Mock.Of<ITherapistSpecialtyRepository>(),
            Mock.Of<IPatientCaretakerRepository>(),
            Mock.Of<ISpecialtyPriceService>(),
            new SiteRepository(context),
            new SessionTransitionMoneyService(
                new SessionServicePaymentRepository(context), new SiteRepository(context)));
    }

    private static SessionEventUpdateRequest EchoedUpdateRequest(TherapySession s) => new()
    {
        SessionTime = s.SessionTime,
        TherapyType = s.TherapyTypes,
        Amount = s.Amount,
        AmountPaid = s.AmountPaid,
        Discount = s.DiscountAmount,
        ProviderAmount = s.ProviderAmount,
        Notes = s.Notes,
        AppointmentStatusId = s.AppointmentStatusId,
    };

    private static async Task<TherapySession> GetSessionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var context = new ApplicationDbContext(options);
        return await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
    }

    // ── Finding 1: the GrossProfit trap ──────────────────────────────────────────────────

    [Fact]
    public async Task DiscountEdit_OnAFeeBearingSession_KeepsTheFeeInGrossProfit()
    {
        var options = NewOptions(nameof(DiscountEdit_OnAFeeBearingSession_KeepsTheFeeInGrossProfit));
        // 125 gross, no therapist fee, a 37.50 late fee already applied ⇒ GrossProfit 162.50.
        await SeedSessionAsync(options, amount: 125m, gross: 162.50m,
            lateFee: 37.50m, lateFeeAppliedOn: new DateOnly(2026, 7, 10));

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.Discount = 25m;   // an ordinary, unrelated discount edit

            Assert.True(await handler.UpdateAsync(1, request));
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(25m, updated.DiscountAmount);
        // net 100 − fee 0 + late fee 37.50. If the recompute dropped the fee this would be
        // 100.00, AmountDue would still say 137.50, and nothing else would complain.
        Assert.Equal(137.50m, updated.GrossProfit);
        Assert.Equal(137.50m, updated.AmountDue());
        Assert.Equal(37.50m, updated.LateFeeAmount);
    }

    [Fact]
    public async Task DiscountEdit_OnAFeeFreeSession_IsUnchangedByWp49()
    {
        var options = NewOptions(nameof(DiscountEdit_OnAFeeFreeSession_IsUnchangedByWp49));
        await SeedSessionAsync(options, amount: 125m, gross: 125m);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.Discount = 25m;

            Assert.True(await handler.UpdateAsync(1, request));
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(100m, updated.GrossProfit);
        Assert.Null(updated.LateFeeAmount);
    }

    // ── mirror parity ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PastDuePredicate_SqlMirror_MatchesAmountDue_WithBothAddOnTerms()
    {
        // Mirror #1 is the only AmountDue() copy written in SQL, so no compiler protects it.
        // A session whose ONLY unpaid balance is a late fee must still be found by the
        // repository's past-due query — the case a narrower predicate would silently miss.
        var options = NewOptions(nameof(PastDuePredicate_SqlMirror_MatchesAmountDue_WithBothAddOnTerms));
        await SeedSessionAsync(options,
            amount: 100m, discount: 0m, amountPaid: 100m,   // service charge fully settled
            onSite: 0m, lateFee: 37.50m, lateFeeAppliedOn: new DateOnly(2026, 1, 10), gross: 137.50m);

        using var context = new ApplicationDbContext(options);
        var repo = new SessionEventRepository(context);

        var pastDue = await repo.GetAllPastDueAsync();

        var row = Assert.Single(pastDue);
        Assert.Equal(37.50m, row.AmountDue);
        Assert.Equal(37.50m, row.LateFeeAmount);
        Assert.True(row.CarriesFee);
    }

    [Fact]
    public async Task PastDuePredicate_ExcludesASessionWhoseFeeWasWaived()
    {
        // Waived ⇒ LateFeeAmount 0.00 ⇒ nothing owed ⇒ off the delinquent list, even though
        // the latch (and therefore CarriesFee) is still set.
        var options = NewOptions(nameof(PastDuePredicate_ExcludesASessionWhoseFeeWasWaived));
        await SeedSessionAsync(options,
            amount: 100m, amountPaid: 100m,
            lateFee: 0.00m, lateFeeAppliedOn: new DateOnly(2026, 1, 10), gross: 100m);

        using var context = new ApplicationDbContext(options);
        var repo = new SessionEventRepository(context);

        Assert.Empty(await repo.GetAllPastDueAsync());
    }

    // ── the fee survives a later status transition ───────────────────────────────────────

    [Fact]
    public async Task Cancel_KeepsTheLateFee_AndKeepsItInGrossProfit()
    {
        // Cancelling voids the SERVICE charge (BR2), not a penalty already levied for late
        // payment. If cancel cleared the fee, any Appointments.Book holder could erase a
        // manager's charge without the fee claim — the loophole ruling 4 closes for discounts.
        var options = NewOptions(nameof(Cancel_KeepsTheLateFee_AndKeepsItInGrossProfit));
        await SeedSessionAsync(options, amount: 125m, provider: 50m, gross: 112.50m,
            lateFee: 37.50m, lateFeeAppliedOn: new DateOnly(2026, 7, 10), statusId: 2);

        using (var context = new ApplicationDbContext(options))
        {
            var service = new BookingService(
                Mock.Of<ILogger<BookingService>>(),
                new BookingRepository(context),
                new TherapySessionRepository(context),
                new SessionTransitionMoneyService(
                    new SessionServicePaymentRepository(context), new SiteRepository(context)));

            var result = await service.CancelAppointmentAsync(1, "familia avisó");
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Equal(0m, session.ProviderAmount);
        Assert.Equal(37.50m, session.LateFeeAmount);   // the penalty stands
        Assert.Equal(37.50m, session.GrossProfit);     // and still reaches P&L
        Assert.Equal(37.50m, session.AmountDue());
        Assert.Contains("[CANCELLED-ZEROED", session.Notes);
    }
}
