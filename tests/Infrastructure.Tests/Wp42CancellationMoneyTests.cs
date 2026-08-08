using Xunit;
using Moq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Patients;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.BusinessObjects.Therapists;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;
using Neurocorp.Api.Infrastructure.Data;
using Neurocorp.Api.Infrastructure.Repositories;

namespace Infrastructure.Tests.Repositories;

/// <summary>
/// WP-42 (B1): cancellation / no-show money integrity, end-to-end through the real services +
/// repositories over the InMemory provider. Written RED-FIRST against the pre-WP-42 behavior
/// (cancel kept the money, no-show kept the money, covered sessions were freely editable) and
/// turned green by the shared transition choke point (SessionTransitionMoneyService) + the
/// covered-session lock.
/// </summary>
public class Wp42CancellationMoneyTests
{
    // ---------------------------------------------------------------- helpers

    private static DbContextOptions<ApplicationDbContext> NewOptions(string name)
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"Wp42_{name}_{Guid.NewGuid():N}")
            .Options;

    /// <summary>Seeds statuses 2/3/4/5, one patient, one therapist, and a money-carrying session.</summary>
    private static async Task SeedSessionAsync(
        DbContextOptions<ApplicationDbContext> options,
        decimal amount = 100m, decimal discount = 15m, decimal provider = 50m, decimal gross = 35m,
        decimal amountPaid = 0m, int statusId = 2, string notes = "", int? siteId = null,
        bool covered = false, bool reversed = false)
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
            SessionDate = new DateOnly(2026, 7, 20),
            SessionTime = new TimeOnly(10, 0),
            Duration = 60,
            Amount = amount,
            DiscountAmount = discount,
            ProviderAmount = provider,
            GrossProfit = gross,
            AmountPaid = amountPaid,
            AppointmentStatusId = statusId,
            SiteId = siteId,
            Notes = notes,
        });

        if (covered)
        {
            // A payroll disbursement covering the session (WP-14). "reversed" adds the WP-14.5
            // offsetting negative allocation — net covered amount returns to 0.
            context.ServicePayments.Add(new ServicePayment
            {
                Id = 10, TherapistId = 1, Amount = provider, PaymentDate = DateTime.UtcNow, PaymentTypeId = 1,
            });
            context.SessionServicePayments.Add(new SessionServicePayment
            {
                Id = 100, ServicePaymentId = 10, TherapySessionId = 1, AmountApplied = provider,
            });
            if (reversed)
            {
                context.ServicePayments.Add(new ServicePayment
                {
                    Id = 11, TherapistId = 1, Amount = -provider, PaymentDate = DateTime.UtcNow, PaymentTypeId = 1,
                    ReversesServicePaymentId = 10,
                });
                context.SessionServicePayments.Add(new SessionServicePayment
                {
                    Id = 101, ServicePaymentId = 11, TherapySessionId = 1, AmountApplied = -provider,
                });
            }
        }

        await context.SaveChangesAsync();
    }

    private static BookingService CreateBookingService(ApplicationDbContext context)
        => new(
            Mock.Of<ILogger<BookingService>>(),
            new BookingRepository(context),
            new TherapySessionRepository(context),
            CreateTransitionMoneyService(context));

    private static SessionTransitionMoneyService CreateTransitionMoneyService(ApplicationDbContext context)
        => new(new SessionServicePaymentRepository(context), new SiteRepository(context));

    /// <summary>Handler wired with REAL session repositories over the context; party services mocked.</summary>
    private static SessionEventHandler CreateHandler(ApplicationDbContext context)
    {
        var patientService = new Mock<IPatientProfileService>();
        patientService.Setup(s => s.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new PatientProfile { PatientId = 1, PatientName = "Doe, John", HasSenadisDiscount = false });
        var therapistService = new Mock<ITherapistProfileService>();
        therapistService.Setup(s => s.GetByIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new TherapistProfile { TherapistId = 1, TherapistName = "Smith, Jane", FeePerSession = 50m });

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
            CreateTransitionMoneyService(context));
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

    // ------------------------------------------------- 1) /cancel zeroes money

    [Fact]
    public async Task Cancel_ZeroesMoneyFields_AndStampsMarker()
    {
        var options = NewOptions(nameof(Cancel_ZeroesMoneyFields_AndStampsMarker));
        await SeedSessionAsync(options);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.CancelAppointmentAsync(1, "familia avisó");
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Equal(0m, session.DiscountAmount);
        Assert.Equal(0m, session.ProviderAmount);
        Assert.Equal(0m, session.GrossProfit);
        Assert.Contains("[CANCELLED-ZEROED", session.Notes);
        Assert.Contains("was A:100.00 D:15.00 P:50.00 G:35.00", session.Notes);
        Assert.Contains("familia avisó", session.Notes); // the cancel reason still lands
    }

    [Fact]
    public async Task Cancel_AlreadyZero_TransitionsWithoutRestamping()
    {
        var options = NewOptions(nameof(Cancel_AlreadyZero_TransitionsWithoutRestamping));
        await SeedSessionAsync(options, amount: 0m, discount: 0m, provider: 0m, gross: 0m,
            notes: "[CANCELLED-ZEROED 2026-07-28: was A:100.00 D:15.00 P:50.00 G:35.00]", statusId: 2);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.CancelAppointmentAsync(1, null);
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        // idempotent: exactly ONE marker — the pre-existing one
        var first = session.Notes.IndexOf("[CANCELLED-ZEROED", StringComparison.Ordinal);
        var last = session.Notes.LastIndexOf("[CANCELLED-ZEROED", StringComparison.Ordinal);
        Assert.True(first >= 0 && first == last, $"expected a single marker, notes: {session.Notes}");
    }

    // ------------------------------------------------- 2) guards on /cancel

    [Fact]
    public async Task Cancel_WithAmountPaid_Blocks400_AndNothingChanges()
    {
        var options = NewOptions(nameof(Cancel_WithAmountPaid_Blocks400_AndNothingChanges));
        await SeedSessionAsync(options, amountPaid: 25m);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CancelAppointmentAsync(1, "reason"));
            Assert.Equal(SessionTransitionMoneyService.PaymentsRecordedMessage, ex.Message);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(2, session.AppointmentStatusId);   // status untouched
        Assert.Equal(100m, session.Amount);             // money untouched
        Assert.Equal("", session.Notes);                // reason NOT appended
    }

    [Fact]
    public async Task Cancel_WithNonReversedAllocation_Blocks400_AndNothingChanges()
    {
        var options = NewOptions(nameof(Cancel_WithNonReversedAllocation_Blocks400_AndNothingChanges));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CancelAppointmentAsync(1, "reason"));
            Assert.Equal(SessionTransitionMoneyService.CoveredSessionMessage, ex.Message);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(2, session.AppointmentStatusId);
        Assert.Equal(100m, session.Amount);
    }

    [Fact]
    public async Task Cancel_WithReversedAllocation_Proceeds()
    {
        var options = NewOptions(nameof(Cancel_WithReversedAllocation_Proceeds));
        await SeedSessionAsync(options, covered: true, reversed: true);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.CancelAppointmentAsync(1, null);
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Contains("[CANCELLED-ZEROED", session.Notes);
    }

    // ------------------------------------------------- 3) /noshow applies the site fee

    [Fact]
    public async Task NoShow_AppliesDefaultFullPriceFee_WhenSessionHasNoSite()
    {
        var options = NewOptions(nameof(NoShow_AppliesDefaultFullPriceFee_WhenSessionHasNoSite));
        await SeedSessionAsync(options, amount: 85m, discount: 17m, provider: 40m, gross: 28m);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.UpdateStatusAsync(1, 5); // the /noshow endpoint
            Assert.Equal(5, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        // WP-49 (BR1/D5): the default moved 30% → 100%, so a no-show now bills the full booked
        // amount. This is a PERCENT change — 100% of 85.00 — not a $100 fee.
        Assert.Equal(85.00m, session.Amount);       // round(100% × 85.00, 2)
        Assert.Equal(0m, session.DiscountAmount);
        Assert.Equal(0m, session.ProviderAmount);   // therapist rendered nothing (G2)
        Assert.Equal(85.00m, session.GrossProfit);  // clinic keeps 100%
        Assert.Contains("[NOSHOW-FEE", session.Notes);
        Assert.Contains("was A:85.00 D:17.00 P:40.00 G:28.00", session.Notes);
    }

    [Fact]
    public async Task NoShow_UsesSiteNoShowFeePct_IncludingHalfCentRounding()
    {
        var options = NewOptions(nameof(NoShow_UsesSiteNoShowFeePct_IncludingHalfCentRounding));
        await SeedSessionAsync(options, amount: 85m, discount: 0m, provider: 40m, gross: 45m, siteId: 7);
        using (var context = new ApplicationDbContext(options))
        {
            context.Sites.Add(new Site { Id = 7, SiteName = "Main", InceptionDate = DateTime.UtcNow, NoShowFeePct = 12.50m });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            await service.UpdateStatusAsync(1, 5);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(10.63m, session.Amount);   // round(12.5% × 85.00, 2) = 10.625 → 10.63 (away from zero)
        Assert.Equal(10.63m, session.GrossProfit);
    }

    [Fact]
    public async Task NoShow_PctZero_FeeIsZero()
    {
        var options = NewOptions(nameof(NoShow_PctZero_FeeIsZero));
        await SeedSessionAsync(options, amount: 85m, discount: 0m, provider: 40m, gross: 45m, siteId: 7);
        using (var context = new ApplicationDbContext(options))
        {
            context.Sites.Add(new Site { Id = 7, SiteName = "Main", InceptionDate = DateTime.UtcNow, NoShowFeePct = 0m });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            await service.UpdateStatusAsync(1, 5);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Equal(0m, session.GrossProfit);
        Assert.Contains("[NOSHOW-FEE", session.Notes); // originals preserved even for a 0 fee
    }

    [Fact]
    public async Task NoShow_WithNonReversedAllocation_Blocks400()
    {
        var options = NewOptions(nameof(NoShow_WithNonReversedAllocation_Blocks400));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateStatusAsync(1, 5));
            Assert.Equal(SessionTransitionMoneyService.CoveredSessionMessage, ex.Message);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(2, session.AppointmentStatusId);
        Assert.Equal(100m, session.Amount);
    }

    [Fact]
    public async Task NoShow_AlreadyFeed_DoesNotCompound()
    {
        var options = NewOptions(nameof(NoShow_AlreadyFeed_DoesNotCompound));
        // A session that already went through the no-show fee (marker on file), re-asserted to 5
        // (e.g. corrected out and back) — the fee must NOT be recomputed off the fee.
        await SeedSessionAsync(options, amount: 25.50m, discount: 0m, provider: 0m, gross: 25.50m,
            statusId: 2, notes: "[NOSHOW-FEE 2026-07-30: was A:85.00 D:17.00 P:40.00 G:28.00]");

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            await service.UpdateStatusAsync(1, 5);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(25.50m, session.Amount); // NOT 30% of 25.50
        var first = session.Notes.IndexOf("[NOSHOW-FEE", StringComparison.Ordinal);
        var last = session.Notes.LastIndexOf("[NOSHOW-FEE", StringComparison.Ordinal);
        Assert.True(first >= 0 && first == last, $"expected a single marker, notes: {session.Notes}");
    }

    [Fact]
    public async Task Complete_OnMoneySession_LeavesMoneyUntouched()
    {
        // Non-3/5 lifecycle transitions carry no money semantics — /complete stays pass-through.
        var options = NewOptions(nameof(Complete_OnMoneySession_LeavesMoneyUntouched));
        await SeedSessionAsync(options, amountPaid: 25m, covered: true); // even guarded state is fine

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.UpdateStatusAsync(1, 4);
            Assert.Equal(4, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(100m, session.Amount);
        Assert.Equal(50m, session.ProviderAmount);
        Assert.Equal("", session.Notes);
    }

    // ------------------------------------------------- 4) /confirm with Declined ≡ cancel

    [Fact]
    public async Task ConfirmDeclined_AppliesCancelMoneySemantics()
    {
        var options = NewOptions(nameof(ConfirmDeclined_AppliesCancelMoneySemantics));
        await SeedSessionAsync(options, statusId: 2);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.ConfirmAppointmentAsync(1, new ConfirmationRequest
            {
                ConfirmationMethod = "Phone",
                ConfirmationResult = "Declined",
            });
            Assert.Equal(3, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(0m, session.Amount);
        Assert.Equal(0m, session.ProviderAmount);
        Assert.Contains("[CANCELLED-ZEROED", session.Notes);

        using (var context = new ApplicationDbContext(options))
        {
            Assert.Equal(1, await context.AppointmentConfirmations.CountAsync()); // attempt recorded
        }
    }

    [Fact]
    public async Task ConfirmDeclined_Blocked_DoesNotRecordConfirmationAttempt()
    {
        var options = NewOptions(nameof(ConfirmDeclined_Blocked_DoesNotRecordConfirmationAttempt));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            await Assert.ThrowsAsync<ArgumentException>(() => service.ConfirmAppointmentAsync(1, new ConfirmationRequest
            {
                ConfirmationMethod = "Phone",
                ConfirmationResult = "Declined",
            }));
        }

        using (var context = new ApplicationDbContext(options))
        {
            // per contract: when the guard blocks, the attempt is NOT recorded
            Assert.Equal(0, await context.AppointmentConfirmations.CountAsync());
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            Assert.Equal(2, session.AppointmentStatusId);
            Assert.Equal(100m, session.Amount);
        }
    }

    [Fact]
    public async Task ConfirmConfirmed_OnCoveredSession_StillWorks()
    {
        // Confirming (→ 2) is not a money transition — the guard must not interfere.
        var options = NewOptions(nameof(ConfirmConfirmed_OnCoveredSession_StillWorks));
        await SeedSessionAsync(options, statusId: 4, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var service = CreateBookingService(context);
            var result = await service.ConfirmAppointmentAsync(1, new ConfirmationRequest
            {
                ConfirmationMethod = "Phone",
                ConfirmationResult = "Confirmed",
            });
            Assert.Equal(2, result.AppointmentStatusId);
        }

        var session = await GetSessionAsync(options);
        Assert.Equal(100m, session.Amount); // money untouched
    }

    // ------------------------------------------------- 5) PUT /api/sessions/{id} — one choke point

    [Fact]
    public async Task Put_StatusChangeTo3_ZeroesMoney_AndStampsMarker()
    {
        var options = NewOptions(nameof(Put_StatusChangeTo3_ZeroesMoney_AndStampsMarker));
        await SeedSessionAsync(options);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.AppointmentStatusId = 3; // the generic-PUT path into Cancelled

            Assert.True(await handler.UpdateAsync(1, request));
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(3, updated.AppointmentStatusId);
        Assert.Equal(0m, updated.Amount);
        Assert.Equal(0m, updated.DiscountAmount);
        Assert.Equal(0m, updated.ProviderAmount);
        Assert.Equal(0m, updated.GrossProfit);
        Assert.Contains("[CANCELLED-ZEROED", updated.Notes);
    }

    [Fact]
    public async Task Put_StatusChangeTo5_AppliesFee()
    {
        var options = NewOptions(nameof(Put_StatusChangeTo5_AppliesFee));
        await SeedSessionAsync(options, amount: 85m, discount: 0m, provider: 40m, gross: 45m);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.AppointmentStatusId = 5;

            Assert.True(await handler.UpdateAsync(1, request));
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(5, updated.AppointmentStatusId);
        // WP-49 (BR1/D5): 100% of 85.00, not 30%.
        Assert.Equal(85.00m, updated.Amount);
        Assert.Equal(85.00m, updated.GrossProfit);
        Assert.Equal(0m, updated.ProviderAmount);
        Assert.Contains("[NOSHOW-FEE", updated.Notes);
    }

    [Fact]
    public async Task Put_StatusChangeTo3_OnPaidSession_Blocks400()
    {
        var options = NewOptions(nameof(Put_StatusChangeTo3_OnPaidSession_Blocks400));
        await SeedSessionAsync(options, amountPaid: 10m);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.AppointmentStatusId = 3;

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => handler.UpdateAsync(1, request));
            Assert.Equal(SessionTransitionMoneyService.PaymentsRecordedMessage, ex.Message);
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(2, updated.AppointmentStatusId);
        Assert.Equal(100m, updated.Amount);
    }

    // ------------------------------------------------- 6) covered-session lock on PUT (G5)

    [Fact]
    public async Task Put_AmountChange_OnCoveredSession_Blocks400_BeforeRecompute()
    {
        var options = NewOptions(nameof(Put_AmountChange_OnCoveredSession_Blocks400_BeforeRecompute));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.Amount = 60m; // money edit on an already-paid session

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => handler.UpdateAsync(1, request));
            Assert.Equal(SessionTransitionMoneyService.CoveredSessionMessage, ex.Message);
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(100m, updated.Amount);         // nothing changed
        Assert.Equal(50m, updated.ProviderAmount);  // the paid fee was NOT silently recomputed
    }

    [Fact]
    public async Task Put_DiscountChange_OnCoveredSession_Blocks400()
    {
        var options = NewOptions(nameof(Put_DiscountChange_OnCoveredSession_Blocks400));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.Discount = 40m;

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => handler.UpdateAsync(1, request));
            Assert.Equal(SessionTransitionMoneyService.CoveredSessionMessage, ex.Message);
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(15m, updated.DiscountAmount);
        Assert.Equal(50m, updated.ProviderAmount);
    }

    [Fact]
    public async Task Put_TherapistReassignment_OnCoveredSession_Blocks400()
    {
        var options = NewOptions(nameof(Put_TherapistReassignment_OnCoveredSession_Blocks400));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session);
            request.TherapistId = 99; // reassignment

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => handler.UpdateAsync(1, request));
            Assert.Equal(SessionTransitionMoneyService.CoveredSessionMessage, ex.Message);
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal(1, updated.TherapistId);
    }

    [Fact]
    public async Task Put_NonMoneyEdit_OnCoveredSession_StillAllowed()
    {
        var options = NewOptions(nameof(Put_NonMoneyEdit_OnCoveredSession_StillAllowed));
        await SeedSessionAsync(options, covered: true);

        using (var context = new ApplicationDbContext(options))
        {
            var handler = CreateHandler(context);
            var session = await context.TherapySessions.AsNoTracking().SingleAsync(ts => ts.Id == 1);
            var request = EchoedUpdateRequest(session); // amount/discount/therapist echoed unchanged
            request.Notes = "rescheduled per mom's call";

            Assert.True(await handler.UpdateAsync(1, request));
        }

        var updated = await GetSessionAsync(options);
        Assert.Equal("rescheduled per mom's call", updated.Notes);
        Assert.Equal(100m, updated.Amount);
        Assert.Equal(50m, updated.ProviderAmount); // money byte-identical
    }
}
