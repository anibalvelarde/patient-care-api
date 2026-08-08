using Microsoft.Extensions.Logging;
using Moq;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Services;
using Xunit;

namespace Core.Tests;

/// <summary>
/// WP-49 (BR3/BR4): the late chargeback and the fee waiver.
/// </summary>
public class SessionFeeServiceTests
{
    private readonly Mock<ITherapySessionRepository> _mockSessions = new();
    private readonly SessionFeeService _sut;

    public SessionFeeServiceTests()
        => _sut = new SessionFeeService(_mockSessions.Object, Mock.Of<ILogger<SessionFeeService>>());

    private static TherapySession Session(
        int id = 1,
        decimal amount = 125m, decimal discount = 0m, decimal amountPaid = 0m,
        decimal provider = 0m, decimal gross = 125m,
        decimal? onSite = null, decimal? lateFee = null, DateOnly? lateFeeAppliedOn = null,
        DateOnly? sessionDate = null, int statusId = 4, string notes = "")
        => new()
        {
            Id = id,
            Amount = amount,
            DiscountAmount = discount,
            AmountPaid = amountPaid,
            ProviderAmount = provider,
            GrossProfit = gross,
            OnSiteChargeAmount = onSite,
            LateFeeAmount = lateFee,
            LateFeeAppliedOn = lateFeeAppliedOn,
            SessionDate = sessionDate ?? new DateOnly(2026, 7, 1),
            AppointmentStatusId = statusId,
            Notes = notes,
        };

    private void SetupById(params TherapySession[] sessions)
        => _mockSessions
            .Setup(r => r.GetByIdsWithPartiesAsync(It.IsAny<IReadOnlyCollection<int>>()))
            .ReturnsAsync(sessions);

    // ── the fee formula (ruling 2) ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(125.00, 37.50)]   // the live prod row measured 2026-08-02
    [InlineData(100.00, 30.00)]
    [InlineData(0.05, 0.02)]      // 0.015 rounds half AWAY from zero, matching MySQL ROUND
    [InlineData(85.00, 25.50)]
    public void CalculateLateFee_IsThirtyPctOfBalance_RoundedAwayFromZero(decimal balance, decimal expected)
        => Assert.Equal(expected, SessionFeeService.CalculateLateFee(balance));

    [Fact]
    public void UnpaidBalanceBeforeLateFee_ExcludesTheFeeItself_SoItCannotCompound()
    {
        // A session that already carries a fee must still expose the PRE-fee balance, or a
        // second application would charge 30% of a number that already contains 30%.
        var session = Session(amount: 125m, lateFee: 37.50m);

        Assert.Equal(125m, session.UnpaidBalanceBeforeLateFee());
        Assert.Equal(162.50m, session.AmountDue());
    }

    [Fact]
    public void AmountDue_CombinesOnSiteChargeAndLateFee()
    {
        // The parity case the SQL mirror must also satisfy: both add-on terms present at once.
        var session = Session(amount: 100m, discount: 10m, amountPaid: 20m, onSite: 15m, lateFee: 30m);

        Assert.Equal(85m, session.UnpaidBalanceBeforeLateFee()); // 100 − 10 − 20 + 15
        Assert.Equal(115m, session.AmountDue());                 // + 30
    }

    // ── eligibility (BR3) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_ProjectsBalanceAndProposedFee()
    {
        var asOf = new DateOnly(2026, 7, 10);
        _mockSessions
            .Setup(r => r.GetLateFeeEligibleAsync(asOf, SessionFeeService.LateFeeGraceDays))
            .ReturnsAsync(new[] { Session(sessionDate: new DateOnly(2026, 7, 1), amount: 125m) });

        var preview = await _sut.PreviewLateFeesAsync(asOf);

        Assert.Equal(1, preview.SessionCount);
        Assert.Equal(30.00m, preview.RatePct);
        Assert.Equal(6, preview.GraceDays);
        Assert.Equal(125m, preview.TotalUnpaidBalance);
        Assert.Equal(37.50m, preview.TotalProposedFee);
        Assert.Equal(9, preview.Items[0].DaysUnpaid);
    }

    [Fact]
    public async Task Apply_ChargesFee_AddsToGrossProfit_AndLatches()
    {
        var session = Session(sessionDate: new DateOnly(2026, 7, 1), amount: 125m, gross: 125m);
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 10) }, actingUserId: 3);

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(37.50m, result.TotalFeeApplied);
        Assert.Equal(37.50m, session.LateFeeAmount);
        Assert.Equal(new DateOnly(2026, 7, 10), session.LateFeeAppliedOn);
        Assert.Equal(162.50m, session.GrossProfit);   // D2: the fee reaches clinic P&L
        Assert.Equal(162.50m, session.AmountDue());
        Assert.Contains("[LATE-FEE 2026-07-10: 30% of 125.00 = 37.50; 9 days unpaid]", session.Notes);
    }

    [Fact]
    public async Task Apply_IsIdempotent_TheLatchBlocksASecondCharge()
    {
        // The whole point of a latch separate from the amount: re-running the batch must not
        // compound, and must say so rather than silently no-op.
        var session = Session(lateFee: 37.50m, lateFeeAppliedOn: new DateOnly(2026, 7, 10));
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 20) }, actingUserId: 3);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains("already been applied", result.Skipped[0].Reason);
        Assert.Equal(37.50m, session.LateFeeAmount);
    }

    [Fact]
    public async Task Apply_SkipsAWaivedSession_BecauseTheLatchSurvivedTheWaive()
    {
        // LateFeeAmount == 0 (waived) but the latch is set. If eligibility keyed off the
        // amount instead, this session would be re-charged the moment the batch ran again —
        // silently undoing a manager's decision.
        var session = Session(lateFee: 0.00m, lateFeeAppliedOn: new DateOnly(2026, 7, 10));
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 8, 1) }, actingUserId: 3);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(0.00m, session.LateFeeAmount);
    }

    [Fact]
    public async Task Apply_SkipsWithReason_WhenBalanceWasSettledBetweenPreviewAndApply()
    {
        var session = Session(amount: 125m, amountPaid: 125m);
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 20) }, actingUserId: 3);

        Assert.Equal(0, result.AppliedCount);
        Assert.Contains("no unpaid balance", result.Skipped[0].Reason);
        Assert.Null(session.LateFeeAmount);
    }

    [Fact]
    public async Task Apply_SkipsWithReason_InsideTheGracePeriod()
    {
        // Day 6 is still inside grace; day 7 is chargeable. Re-derived at write time, so a
        // stale preview cannot charge early.
        var session = Session(sessionDate: new DateOnly(2026, 7, 1));
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 7) }, actingUserId: 3);

        Assert.Equal(0, result.AppliedCount);
        Assert.Contains("grace period", result.Skipped[0].Reason);
    }

    [Fact]
    public async Task Apply_SkipsCancelledSessions()
    {
        var session = Session(statusId: 3, sessionDate: new DateOnly(2026, 7, 1));
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 20) }, actingUserId: 3);

        Assert.Equal(0, result.AppliedCount);
        Assert.Contains("cancelled", result.Skipped[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_ReopensASettledSession_IsPaidOffFlipsBack()
    {
        // Mirror #2: IsPaidOff is derived from AmountDue(), so a landed fee must re-open a
        // session that had been marked paid off.
        var session = Session(amount: 125m, amountPaid: 100m, sessionDate: new DateOnly(2026, 7, 1));
        session.IsPaidOff = true;
        SetupById(session);

        await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 20) }, actingUserId: 3);

        Assert.False(session.IsPaidOff);
        Assert.Equal(7.50m, session.LateFeeAmount);   // 30% of the 25.00 still owed
    }

    [Fact]
    public async Task Apply_NoShowSessionsAreEligible()
    {
        // An unpaid no-show fee can itself go late — which is exactly why waive takes a kind.
        var session = Session(statusId: 5, amount: 85m, gross: 85m,
            sessionDate: new DateOnly(2026, 7, 1), notes: "[NOSHOW-FEE 2026-07-01: was A:85.00]");
        SetupById(session);

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1], AsOf = new DateOnly(2026, 7, 20) }, actingUserId: 3);

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(25.50m, session.LateFeeAmount);
    }

    [Fact]
    public async Task Apply_ReportsMissingSessionsRatherThanThrowing()
    {
        SetupById(); // repository returns nothing

        var result = await _sut.ApplyLateFeesAsync(
            new ApplyLateFeesRequest { SessionIds = [1, 2] }, actingUserId: 3);

        Assert.Equal(2, result.SkippedCount);
        Assert.All(result.Skipped, s => Assert.Contains("not found", s.Reason));
    }

    [Fact]
    public async Task Apply_RequiresAtLeastOneSession()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ApplyLateFeesAsync(new ApplyLateFeesRequest { SessionIds = [] }, actingUserId: 3));

    // ── waive (BR4) ───────────────────────────────────────────────────────────────────────

    private void SetupSingle(TherapySession session)
        => _mockSessions.Setup(r => r.GetByIdAsync(session.Id)).ReturnsAsync(session);

    [Fact]
    public async Task Waive_Late_ZeroesTheAmount_KeepsTheLatch_AndStampsTheMarker()
    {
        var session = Session(amount: 125m, lateFee: 37.50m,
            lateFeeAppliedOn: new DateOnly(2026, 7, 10), gross: 162.50m);
        SetupSingle(session);

        var result = await _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "caretaker hospitalized" },
            actingUserId: 3);

        Assert.Equal(37.50m, result.LateFeeWaived);
        Assert.Equal(0.00m, session.LateFeeAmount);
        // The latch SURVIVES — otherwise the next batch run re-charges what was just forgiven.
        Assert.Equal(new DateOnly(2026, 7, 10), session.LateFeeAppliedOn);
        Assert.Equal(125m, session.GrossProfit);
        Assert.Equal(3, session.FeeWaivedByUserId);
        Assert.NotNull(session.FeeWaivedOn);
        Assert.Contains("[FEE-WAIVED", session.Notes);
        Assert.Contains("late 37.50 waived by u#3; reason: caretaker hospitalized]", session.Notes);
    }

    [Fact]
    public async Task Waive_Late_CannotBeDoneTwice()
    {
        var session = Session(lateFee: 0.00m, lateFeeAppliedOn: new DateOnly(2026, 7, 10));
        SetupSingle(session);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "again" }, actingUserId: 3));

        Assert.Equal(SessionFeeService.LateFeeAlreadyWaivedMessage, ex.Message);
    }

    [Fact]
    public async Task Waive_Late_RequiresAFeeToExist()
    {
        SetupSingle(Session());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "nothing here" }, actingUserId: 3));

        Assert.Equal(SessionFeeService.NoLateFeeMessage, ex.Message);
    }

    [Fact]
    public async Task Waive_NoShow_DiscountsTheFeeAway_LeavingAmountAsTheHistoricalRecord()
    {
        var session = Session(amount: 85m, discount: 0m, gross: 85m, statusId: 5,
            notes: "[NOSHOW-FEE 2026-07-01: was A:85.00 D:0.00 P:40.00 G:45.00]");
        SetupSingle(session);

        var result = await _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "NoShow", Reason = "clinic error" }, actingUserId: 3);

        Assert.Equal(85m, result.NoShowFeeWaived);
        Assert.Equal(85m, session.Amount);            // history preserved
        Assert.Equal(85m, session.DiscountAmount);    // fee discounted away
        Assert.Equal(0m, session.GrossProfit);
        Assert.Equal(0m, session.AmountDue());
        Assert.Contains("no-show 85.00 waived by u#3", session.Notes);
    }

    [Fact]
    public async Task Waive_NoShow_RequiresTheMarker()
    {
        SetupSingle(Session(notes: "just a normal session"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "NoShow", Reason = "no marker" }, actingUserId: 3));

        Assert.Equal(SessionFeeService.NoNoShowFeeMessage, ex.Message);
    }

    [Fact]
    public async Task Waive_Both_ClearsEachLeg_AndStampsOneMarkerPerLeg()
    {
        var session = Session(amount: 85m, discount: 0m, gross: 110.50m, statusId: 5,
            lateFee: 25.50m, lateFeeAppliedOn: new DateOnly(2026, 7, 10),
            notes: "[NOSHOW-FEE 2026-07-01: was A:85.00]");
        SetupSingle(session);

        var result = await _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Both", Reason = "billed in error" }, actingUserId: 7);

        Assert.Equal(25.50m, result.LateFeeWaived);
        Assert.Equal(85m, result.NoShowFeeWaived);
        Assert.Equal(0m, session.GrossProfit);
        Assert.Equal(0m, session.AmountDue());
        Assert.Contains("late 25.50 waived by u#7", session.Notes);
        Assert.Contains("no-show 85.00 waived by u#7", session.Notes);
    }

    [Fact]
    public async Task Waive_Both_FailsWhole_WhenOnlyOneLegIsValid()
    {
        // A half-applied waive would leave the session in a state nobody asked for.
        var session = Session(amount: 85m, lateFee: 25.50m,
            lateFeeAppliedOn: new DateOnly(2026, 7, 10), notes: "no no-show marker here");
        SetupSingle(session);

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Both", Reason = "both" }, actingUserId: 3));

        Assert.Equal(25.50m, session.LateFeeAmount);  // untouched
        Assert.Null(session.FeeWaivedOn);
    }

    [Fact]
    public async Task Waive_PartiallyPaidSessionIsAllowed_TheGuardIsNarrow()
    {
        // D3: WP-42 blocks any money move when AmountPaid > 0. That blanket rule would make
        // BR4 unusable, because partial payment is the NORMAL state on a late-fee session.
        var session = Session(amount: 125m, amountPaid: 100m, lateFee: 7.50m,
            lateFeeAppliedOn: new DateOnly(2026, 7, 10), gross: 132.50m);
        SetupSingle(session);

        var result = await _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "goodwill" }, actingUserId: 3);

        Assert.Equal(7.50m, result.LateFeeWaived);
        Assert.Equal(25m, result.AmountDueAfter);
    }

    [Fact]
    public async Task Waive_BlockedWhenItWouldLeaveACredit()
    {
        // The narrow guard's actual job: the caretaker overpaid, so forgiving the fee would
        // put the balance below zero and the clinic would owe money back.
        var session = Session(amount: 125m, amountPaid: 160m, lateFee: 37.50m,
            lateFeeAppliedOn: new DateOnly(2026, 7, 10), gross: 162.50m);
        SetupSingle(session);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "would credit" }, actingUserId: 3));

        Assert.Equal(SessionFeeService.WouldCreateCreditMessage, ex.Message);
        Assert.Equal(37.50m, session.LateFeeAmount);  // nothing written
    }

    [Fact]
    public async Task Waive_RequiresAFeeKind()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = null, Reason = "which one?" }, actingUserId: 3));

        Assert.Contains("Late, NoShow, or Both", ex.Message);
    }

    // ── feeKind is a WIRE STRING, not an enum ────────────────────────────────────────────
    // WP-49 originally typed this property as the SessionFeeKind enum. The API registers no
    // JsonStringEnumConverter, so System.Text.Json binds enums from integers only and every
    // waive call from the UI — which sends "Late" — died on a model-validation 400. These
    // pin the contract that replaced it.

    [Theory]
    [InlineData("Late", SessionFeeKind.Late)]
    [InlineData("NoShow", SessionFeeKind.NoShow)]
    [InlineData("Both", SessionFeeKind.Both)]
    [InlineData("late", SessionFeeKind.Late)]      // case-insensitive on the wire
    [InlineData("NOSHOW", SessionFeeKind.NoShow)]
    public void TryParseFeeKind_AcceptsTheWireStrings(string wire, SessionFeeKind expected)
    {
        Assert.True(new WaiveFeeRequest { FeeKind = wire }.TryParseFeeKind(out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Lat")]
    [InlineData("Everything")]
    [InlineData("1")]       // the integer form the enum binder used to demand — not a wire value
    [InlineData("99")]      // out of range: Enum.TryParse accepts stray numerics, IsDefined rejects
    public void TryParseFeeKind_RejectsAnythingElse(string? wire)
        => Assert.False(new WaiveFeeRequest { FeeKind = wire }.TryParseFeeKind(out _));

    [Fact]
    public async Task Waive_UnrecognisedFeeKind_SaysWhatIsLegal()
    {
        // The old failure named a JSON path and a byte offset. An operator needs the values.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Everything", Reason = "typo" }, actingUserId: 3));

        Assert.Equal(SessionFeeService.InvalidFeeKindMessage, ex.Message);
    }

    [Fact]
    public async Task Waive_EchoesTheFeeKindAsAString()
    {
        var session = Session(lateFee: 10m, lateFeeAppliedOn: new DateOnly(2026, 7, 10), gross: 135m);
        SetupSingle(session);

        var result = await _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "late", Reason = "goodwill" }, actingUserId: 3);

        // Normalised to the canonical casing, never a bare integer.
        Assert.Equal("Late", result.FeeKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Waive_RequiresAReason(string reason)
        => await Assert.ThrowsAsync<ArgumentException>(() => _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = reason }, actingUserId: 3));

    [Fact]
    public async Task Waive_UnknownSession_Throws404()
    {
        _mockSessions.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((TherapySession?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.WaiveFeeAsync(99,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "gone" }, actingUserId: 3));
    }

    // ── reason sanitizing ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SanitizeReason_StripsBrackets_WhichWouldBreakTheMarkerShape()
    {
        // A single ']' would truncate the bracket-bounded regex that reads these markers,
        // leaking the rest of the marker onto caretaker-facing printed output.
        Assert.Equal("evil not really", SessionFeeService.SanitizeReason("evil] not [really]"));
    }

    [Fact]
    public void SanitizeReason_CollapsesNewlinesAndWhitespace()
        => Assert.Equal("line one line two", SessionFeeService.SanitizeReason("line one\n\n   line two  "));

    [Fact]
    public void SanitizeReason_CapsLength()
        => Assert.Equal(SessionFeeService.MaxReasonLength,
            SessionFeeService.SanitizeReason(new string('x', 500)).Length);

    [Fact]
    public async Task Waive_MarkerSurvivesAHostileReason()
    {
        var session = Session(lateFee: 10m, lateFeeAppliedOn: new DateOnly(2026, 7, 10), gross: 135m);
        SetupSingle(session);

        await _sut.WaiveFeeAsync(1,
            new WaiveFeeRequest { FeeKind = "Late", Reason = "closed] [LEGACY-IMPORT: fake" },
            actingUserId: 3);

        // Exactly one opening bracket for the marker, and it still closes at the very end.
        var marker = session.Notes.Split('\n').Last();
        Assert.StartsWith("[FEE-WAIVED ", marker);
        Assert.EndsWith("]", marker);
        Assert.Equal(1, marker.Count(c => c == '['));
        Assert.Equal(1, marker.Count(c => c == ']'));
    }

    // ── the loophole predicate (ruling 4) ─────────────────────────────────────────────────

    [Fact]
    public void CarriesFee_TrueForALatchedLateFee_EvenAfterWaiving()
    {
        Assert.True(Session(lateFee: 37.50m, lateFeeAppliedOn: new DateOnly(2026, 7, 10)).CarriesFee());
        Assert.True(Session(lateFee: 0.00m, lateFeeAppliedOn: new DateOnly(2026, 7, 10)).CarriesFee());
    }

    [Fact]
    public void CarriesFee_TrueForANoShowMarker()
        => Assert.True(Session(notes: "[NOSHOW-FEE 2026-07-01: was A:85.00]").CarriesFee());

    [Fact]
    public void CarriesFee_FalseForAnOrdinarySession()
        => Assert.False(Session(notes: "patient was late but attended").CarriesFee());
}
