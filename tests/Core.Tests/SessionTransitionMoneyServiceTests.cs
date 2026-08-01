using System.Text.RegularExpressions;
using Moq;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;

namespace Core.Tests;

/// <summary>
/// WP-42: unit tests for the money-at-transition choke point — guards, marker formats,
/// fee math (incl. half-cent rounding), idempotency, and site-pct resolution.
/// </summary>
public class SessionTransitionMoneyServiceTests
{
    private readonly Mock<ISessionServicePaymentRepository> _mockAllocations = new();
    private readonly Mock<IRepository<Site>> _mockSites = new();
    private readonly SessionTransitionMoneyService _sut;

    public SessionTransitionMoneyServiceTests()
    {
        // default: no allocations cover the session
        _mockAllocations
            .Setup(r => r.GetBySessionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(new List<SessionServicePayment>());
        _sut = new SessionTransitionMoneyService(_mockAllocations.Object, _mockSites.Object);
    }

    private static TherapySession Session(
        decimal amount = 100m, decimal discount = 15m, decimal provider = 50m, decimal gross = 35m,
        decimal amountPaid = 0m, int statusId = 2, string notes = "", int? siteId = null) => new()
    {
        Id = 1,
        PatientId = 1,
        TherapistId = 1,
        Amount = amount,
        DiscountAmount = discount,
        ProviderAmount = provider,
        GrossProfit = gross,
        AmountPaid = amountPaid,
        AppointmentStatusId = statusId,
        SiteId = siteId,
        Notes = notes,
    };

    private void SetupAllocations(params decimal[] amountsApplied)
    {
        _mockAllocations
            .Setup(r => r.GetBySessionIdsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync(amountsApplied
                .Select((a, i) => new SessionServicePayment { Id = i + 1, TherapySessionId = 1, AmountApplied = a })
                .ToList());
    }

    // ---------------------------------------------------------------- guards

    [Theory]
    [InlineData(1)] // Proposed
    [InlineData(2)] // Confirmed
    [InlineData(4)] // Completed
    [InlineData(6)] // CheckedIn
    [InlineData(7)] // InTherapy
    public async Task Prepare_NonMoneyStatus_ReturnsNull_WithoutRunningGuards(int statusId)
    {
        // Even a paid session passes — the guards only apply to transitions into 3/5.
        var patch = await _sut.PrepareTransitionAsync(Session(amountPaid: 50m), statusId);

        Assert.Null(patch);
        _mockAllocations.Verify(r => r.GetBySessionIdsAsync(It.IsAny<IEnumerable<int>>()), Times.Never);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Prepare_AmountPaidPositive_Throws_WithExactWireMessage(int statusId)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.PrepareTransitionAsync(Session(amountPaid: 0.01m), statusId));

        Assert.Equal(SessionTransitionMoneyService.PaymentsRecordedMessage, ex.Message);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Prepare_NonReversedAllocations_Throws_WithExactWireMessage(int statusId)
    {
        SetupAllocations(50m);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.PrepareTransitionAsync(Session(), statusId));

        Assert.Equal(SessionTransitionMoneyService.CoveredSessionMessage, ex.Message);
    }

    [Fact]
    public async Task Prepare_ReversedAllocations_NetZero_Passes()
    {
        SetupAllocations(50m, -50m); // WP-14.5 reversal mirrors the original

        var patch = await _sut.PrepareTransitionAsync(Session(), 3);

        Assert.NotNull(patch);
        Assert.Equal(0m, patch!.Amount);
    }

    // ---------------------------------------------------------------- cancel (→ 3)

    [Fact]
    public async Task Cancel_ZeroesAllFour_AndStampsContractMarker()
    {
        var patch = await _sut.PrepareTransitionAsync(Session(100m, 15m, 50m, 35m), 3);

        Assert.NotNull(patch);
        Assert.Equal(0m, patch!.Amount);
        Assert.Equal(0m, patch.DiscountAmount);
        Assert.Equal(0m, patch.ProviderAmount);
        Assert.Equal(0m, patch.GrossProfit);
        // [CANCELLED-ZEROED yyyy-MM-dd: was A:.. D:.. P:.. G:..] — the exact contract format
        Assert.Matches(
            new Regex(@"^\[CANCELLED-ZEROED \d{4}-\d{2}-\d{2}: was A:100\.00 D:15\.00 P:50\.00 G:35\.00\]$"),
            patch.NotesMarker!);
    }

    [Fact]
    public async Task Cancel_AlreadyAllZero_ReturnsNull_NoRestamp()
    {
        var patch = await _sut.PrepareTransitionAsync(Session(0m, 0m, 0m, 0m), 3);

        Assert.Null(patch); // status-only transition; no duplicate marker
    }

    [Fact]
    public async Task Cancel_OnLegacyMoneyCarryingCancelledRow_StillZeroes()
    {
        // A pre-WP-42 cancelled row that kept its money (the 12339 pattern): re-cancelling it
        // zeroes and stamps — the guarded one-off becomes self-service.
        var patch = await _sut.PrepareTransitionAsync(Session(statusId: 3), 3);

        Assert.NotNull(patch);
        Assert.Equal(0m, patch!.Amount);
        Assert.Contains("[CANCELLED-ZEROED", patch.NotesMarker);
    }

    // ---------------------------------------------------------------- no-show (→ 5)

    [Fact]
    public async Task NoShow_UsesSessionSitePct_AndStampsContractMarker()
    {
        _mockSites.Setup(r => r.GetByIdAsync(7))
            .ReturnsAsync(new Site { Id = 7, SiteName = "Main", NoShowFeePct = 12.50m });

        var patch = await _sut.PrepareTransitionAsync(Session(85m, 17m, 40m, 28m, siteId: 7), 5);

        Assert.NotNull(patch);
        Assert.Equal(10.63m, patch!.Amount);       // round(12.5% × 85.00, 2) — half away from zero
        Assert.Equal(0m, patch.DiscountAmount);
        Assert.Equal(0m, patch.ProviderAmount);    // G2: therapist rendered nothing
        Assert.Equal(10.63m, patch.GrossProfit);   // clinic keeps 100% of the fee
        Assert.Matches(
            new Regex(@"^\[NOSHOW-FEE \d{4}-\d{2}-\d{2}: was A:85\.00 D:17\.00 P:40\.00 G:28\.00\]$"),
            patch.NotesMarker!);
    }

    [Fact]
    public async Task NoShow_NoSiteOnSession_UsesDefault30()
    {
        var patch = await _sut.PrepareTransitionAsync(Session(85m, 0m, 40m, 45m, siteId: null), 5);

        Assert.Equal(25.50m, patch!.Amount); // 30% of 85.00
        _mockSites.Verify(r => r.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task NoShow_SiteRowMissing_UsesDefault30()
    {
        _mockSites.Setup(r => r.GetByIdAsync(9)).ReturnsAsync((Site?)null);

        var patch = await _sut.PrepareTransitionAsync(Session(85m, 0m, 40m, 45m, siteId: 9), 5);

        Assert.Equal(25.50m, patch!.Amount);
    }

    [Fact]
    public async Task NoShow_PctZero_FeeZero_OriginalsStillStamped()
    {
        _mockSites.Setup(r => r.GetByIdAsync(7))
            .ReturnsAsync(new Site { Id = 7, SiteName = "Main", NoShowFeePct = 0m });

        var patch = await _sut.PrepareTransitionAsync(Session(85m, 0m, 40m, 45m, siteId: 7), 5);

        Assert.NotNull(patch);
        Assert.Equal(0m, patch!.Amount);
        Assert.Equal(0m, patch.GrossProfit);
        Assert.Contains("was A:85.00", patch.NotesMarker);
    }

    [Fact]
    public async Task NoShow_MarkerAlreadyOnFile_ReturnsNull_FeeNeverCompounds()
    {
        var session = Session(25.50m, 0m, 0m, 25.50m,
            notes: "[NOSHOW-FEE 2026-07-30: was A:85.00 D:17.00 P:40.00 G:28.00]");

        var patch = await _sut.PrepareTransitionAsync(session, 5);

        Assert.Null(patch); // NOT 30% of 25.50
    }

    // ---------------------------------------------------------------- fee math

    [Theory]
    [InlineData("30", "85.00", "25.50")]     // the workbook's Penalidad 30%
    [InlineData("12.5", "85.00", "10.63")]   // 10.625 rounds half AWAY (matches MySQL ROUND)
    [InlineData("0", "85.00", "0.00")]       // pct 0 = no fee
    [InlineData("100", "85.00", "85.00")]    // full charge
    [InlineData("30", "0.00", "0.00")]       // zero-amount session
    [InlineData("15", "33.33", "5.00")]      // 4.9995 → 5.00
    public void NoShowFee_RoundsHalfAwayFromZero(string pct, string amount, string expected)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var fee = SessionTransitionMoneyService.NoShowFee(decimal.Parse(pct, inv), decimal.Parse(amount, inv));

        Assert.Equal(decimal.Parse(expected, inv), fee);
    }

    // ---------------------------------------------------------------- covered predicate

    [Fact]
    public async Task IsCovered_NetPositive_True()
    {
        SetupAllocations(25m, 25m);
        Assert.True(await _sut.IsCoveredByServicePaymentAsync(1));
    }

    [Fact]
    public async Task IsCovered_ReversedToNetZero_False()
    {
        SetupAllocations(50m, -50m);
        Assert.False(await _sut.IsCoveredByServicePaymentAsync(1));
    }

    [Fact]
    public async Task IsCovered_NoAllocations_False()
    {
        Assert.False(await _sut.IsCoveredByServicePaymentAsync(1));
    }
}
