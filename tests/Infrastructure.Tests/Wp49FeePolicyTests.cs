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

    private static BookingService CreateBookingService(ApplicationDbContext context)
        => new(
            Mock.Of<ILogger<BookingService>>(),
            new BookingRepository(context),
            new TherapySessionRepository(context),
            new SessionTransitionMoneyService(
                new SessionServicePaymentRepository(context), new SiteRepository(context)));

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
    public async Task Cancel_IsBlocked_WhenTheSessionCarriesAnActiveFee()
    {
        // Owner ruling 2026-08-08. Cancelling a fee-bearing session fails loudly rather than
        // silently deciding the fee's fate. The alternative considered — keeping the fee — left
        // a live receivable attached to a voided session, a state nobody would look for.
        var options = NewOptions(nameof(Cancel_IsBlocked_WhenTheSessionCarriesAnActiveFee));
        await SeedSessionAsync(options, amount: 125m, provider: 50m, gross: 112.50m,
            lateFee: 37.50m, lateFeeAppliedOn: new DateOnly(2026, 7, 10), statusId: 2);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);

            var ex = await Assert.ThrowsAsync<ArgumentException>(
                () => service.CancelAppointmentAsync(1, "familia avisó"));
            Assert.Equal(SessionTransitionMoneyService.ActiveFeeMessage, ex.Message);
        }

        // Nothing was written — status and money both untouched.
        var session = await GetSessionAsync(options);
        Assert.Equal(2, session.AppointmentStatusId);
        Assert.Equal(125m, session.Amount);
        Assert.Equal(37.50m, session.LateFeeAmount);
        Assert.DoesNotContain("[CANCELLED-ZEROED", session.Notes);
    }

    [Fact]
    public async Task Cancel_Succeeds_OnceTheFeeHasBeenWaived()
    {
        // The other half of the ruling: waive first (manager, with a reason), then anyone who
        // can book may cancel. A waived fee is 0.00 with its latch still set, so the guard must
        // key on the AMOUNT — not on CarriesFee(), which stays true forever.
        var options = NewOptions(nameof(Cancel_Succeeds_OnceTheFeeHasBeenWaived));
        await SeedSessionAsync(options, amount: 125m, provider: 50m, gross: 75m,
            lateFee: 0.00m, lateFeeAppliedOn: new DateOnly(2026, 7, 10), statusId: 2,
            notes: "[FEE-WAIVED 2026-08-08: late 37.50 waived by u#3; reason: goodwill]");

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.CancelAppointmentAsync(1, "familia avisó");
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Equal(0m, session.ProviderAmount);
        Assert.Equal(0m, session.GrossProfit);
        Assert.Equal(0m, session.AmountDue());
        Assert.Contains("[CANCELLED-ZEROED", session.Notes);
    }

    [Fact]
    public async Task Cancel_IsBlocked_ByAnUnwaivedNoShowFee()
    {
        var options = NewOptions(nameof(Cancel_IsBlocked_ByAnUnwaivedNoShowFee));
        await SeedSessionAsync(options, amount: 85m, discount: 0m, provider: 0m, gross: 85m,
            statusId: 5, notes: "[NOSHOW-FEE 2026-07-10: was A:85.00 D:0.00 P:40.00 G:45.00]");

        using var context = new ApplicationDbContext(options);
        var service = CreateBookingService(context);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.CancelAppointmentAsync(1, "correction"));
        Assert.Equal(SessionTransitionMoneyService.ActiveFeeMessage, ex.Message);
    }

    [Fact]
    public async Task Cancel_IsStillBlocked_AfterTheSessionMovesOffNoShowStatus()
    {
        // ⭐ The case that rules out "just check AppointmentStatusId == 5".
        //
        // WP-42 restores nothing when a session transitions OUT of NoShow, so after a 5→2
        // correction the fee money is still in Amount while the status reads Confirmed. A
        // status-based test would report "no active fee" here and let anyone cancel the
        // session, silently discarding money only a manager may forgive.
        //
        // Reproduced live on a restored production copy 2026-08-08: no-showed a $150 session
        // (fee applied, marker stamped), moved it back to Confirmed, and the money was still
        // there — status 2, amount 150.00, marker present.
        var options = NewOptions(nameof(Cancel_IsStillBlocked_AfterTheSessionMovesOffNoShowStatus));
        await SeedSessionAsync(options,
            amount: 150m, discount: 0m, provider: 0m, gross: 150m,
            statusId: 2,   // NOT NoShow any more…
            notes: "[NOSHOW-FEE 2026-08-07: was A:150.00 D:0.00 P:60.00 G:90.00]");  // …but the fee is still on it

        using var context = new ApplicationDbContext(options);
        var service = CreateBookingService(context);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => service.CancelAppointmentAsync(1, "probe"));
        Assert.Equal(SessionTransitionMoneyService.ActiveFeeMessage, ex.Message);
    }

    [Fact]
    public void NoShowStatusWithoutAMarker_IsNotTreatedAsFeeBearing()
    {
        // The converse, and the reason a status test would DEADLOCK a session: a status-5 row
        // that never went through the WP-42 choke point (a legacy import, say) has no marker.
        // A status-based guard would block cancelling it, while waive-fee refuses to act
        // without a marker — leaving the session neither cancellable nor waivable.
        var legacyNoShow = new TherapySession
        {
            Id = 1, Amount = 150m, DiscountAmount = 0m, AppointmentStatusId = 5, Notes = "no marker here",
        };

        Assert.False(SessionTransitionMoneyService.HasActiveFee(legacyNoShow));
        Assert.False(legacyNoShow.HasNoShowFeeMarker());
    }

    [Fact]
    public async Task Cancel_IsUnaffected_OnAnOrdinaryFeeFreeSession()
    {
        // The guard must not change the everyday path.
        var options = NewOptions(nameof(Cancel_IsUnaffected_OnAnOrdinaryFeeFreeSession));
        await SeedSessionAsync(options, amount: 125m, provider: 50m, gross: 75m, statusId: 2);

        using (var context = new ApplicationDbContext(options))
        {
            var result = await CreateBookingService(context).CancelAppointmentAsync(1, "familia avisó");
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Equal(0m, session.GrossProfit);
        Assert.Contains("[CANCELLED-ZEROED", session.Notes);
    }
}
