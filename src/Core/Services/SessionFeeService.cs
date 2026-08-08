using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Exceptions;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-49 (BR3/BR4): the late chargeback and the fee waiver.
///
/// <para><b>BR3 — late chargeback.</b> A session whose caretaker has not paid within
/// <see cref="LateFeeGraceDays"/> days is charged <see cref="LateFeeRatePct"/>% of its UNPAID
/// BALANCE (owner ruling 2, not of the session gross — a caretaker who paid most of it gets a
/// proportionally smaller penalty). It is applied by a MANAGER-TRIGGERED BATCH WITH PREVIEW,
/// mirroring Run Payroll. There is deliberately no scheduled job: the API has zero scheduled-job
/// infrastructure (its only IHostedService is a one-shot DbContext warmup), so automation would
/// mean a new k3s CronJob, a new auth story, and fees charged with nobody looking.</para>
///
/// <para><b>The 6-day clock runs BESIDE the 35-day delinquency window</b> (owner ruling 1).
/// <c>TherapySession.DAYS_LATE_LIMIT</c> is untouched: a session is surcharged on day 7 but only
/// becomes <i>delinquent</i> on day 36. Two different statements — "a late fee applies" and
/// "this is a collections problem" — and merging them would silently change who appears on the
/// delinquent lists.</para>
///
/// <para><b>BR4 — waiver.</b> Modelled on the WP-14.5 append-only reversal
/// (<c>ServicePaymentService.ReverseAsync</c>): a mandatory reason, no re-waiving, and the
/// original amount never destroyed — it is preserved in the Notes marker while the column holds
/// the live/effective fee.</para>
/// </summary>
public class SessionFeeService : ISessionFeeService
{
    /// <summary>BR3: the chargeback rate, as a percent of the unpaid balance.</summary>
    public const decimal LateFeeRatePct = 30.00m;

    /// <summary>
    /// BR3: days of grace before the chargeback becomes eligible. 6 grace days means a session
    /// is charged on day 7 — "hasn't paid within a week".
    /// </summary>
    public const int LateFeeGraceDays = 6;

    /// <summary>Longest a waiver reason may be once sanitized; matches the UI's maxlength.</summary>
    public const int MaxReasonLength = 200;

    public const string LateFeeMarkerPrefix = "[LATE-FEE";
    public const string FeeWaivedMarkerPrefix = "[FEE-WAIVED";
    private const string NoShowMarkerPrefix = "[NOSHOW-FEE";

    /// <summary>Wire-exact 400 messages — contract copies; the UI matches on them.</summary>
    public const string NoLateFeeMessage = "This session has no late fee to waive.";
    public const string LateFeeAlreadyWaivedMessage = "The late fee on this session has already been waived.";
    public const string NoNoShowFeeMessage = "This session has no no-show fee to waive.";
    public const string NoShowFeeAlreadyWaivedMessage = "The no-show fee on this session has already been waived.";
    public const string WouldCreateCreditMessage =
        "Waiving this fee would leave the caretaker with a credit — record a refund instead.";
    public const string InvalidFeeKindMessage =
        "Specify which fee to waive: Late, NoShow, or Both.";

    private readonly ITherapySessionRepository _sessionRepository;
    private readonly ILogger<SessionFeeService> _logger;

    public SessionFeeService(
        ITherapySessionRepository sessionRepository,
        ILogger<SessionFeeService> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    // ── BR3: preview ─────────────────────────────────────────────────────────────────────

    public async Task<LateFeePreviewResult> PreviewLateFeesAsync(DateOnly? asOf)
    {
        var evaluationDate = asOf ?? ClinicClock.Today();
        var eligible = await _sessionRepository.GetLateFeeEligibleAsync(evaluationDate, LateFeeGraceDays);

        var items = eligible
            .Select(s => BuildPreviewItem(s, evaluationDate))
            .ToList();

        return new LateFeePreviewResult
        {
            AsOf = evaluationDate,
            RatePct = LateFeeRatePct,
            GraceDays = LateFeeGraceDays,
            Items = items,
            SessionCount = items.Count,
            TotalUnpaidBalance = items.Sum(i => i.UnpaidBalance),
            TotalProposedFee = items.Sum(i => i.ProposedFee),
        };
    }

    private static LateFeePreviewItem BuildPreviewItem(TherapySession session, DateOnly asOf)
    {
        var balance = session.UnpaidBalanceBeforeLateFee();
        return new LateFeePreviewItem
        {
            SessionId = session.Id,
            SessionDate = session.SessionDate,
            DaysUnpaid = asOf.DayNumber - session.SessionDate.DayNumber,
            PatientId = session.PatientId,
            PatientName = session.Patient?.User?.GetFullName() ?? string.Empty,
            CaretakerName = ResolveCaretakerName(session),
            UnpaidBalance = balance,
            ProposedFee = CalculateLateFee(balance),
        };
    }

    private static string? ResolveCaretakerName(TherapySession session)
    {
        var links = session.Patient?.Caretakers;
        if (links is null || links.Count == 0) return null;

        // Primary if flagged, else the first link — same fallback the session-details panel uses.
        var link = links.FirstOrDefault(l => l.PrimaryCaretaker) ?? links.First();
        return link.Caretaker?.User?.GetFullName();
    }

    /// <summary>
    /// 30% of the unpaid balance, rounded half away from zero — the same rounding as
    /// <c>SessionTransitionMoneyService.NoShowFee</c>, which the WP-42A rehearsal pinned
    /// against MySQL's ROUND.
    /// </summary>
    public static decimal CalculateLateFee(decimal unpaidBalance)
        => Math.Round(LateFeeRatePct / 100m * unpaidBalance, 2, MidpointRounding.AwayFromZero);

    // ── BR3: apply ───────────────────────────────────────────────────────────────────────

    public async Task<ApplyLateFeesResult> ApplyLateFeesAsync(ApplyLateFeesRequest request, int actingUserId)
    {
        if (request.SessionIds is null || request.SessionIds.Count == 0)
        {
            throw new ArgumentException("Select at least one session to charge.");
        }

        var evaluationDate = request.AsOf ?? ClinicClock.Today();
        var requestedIds = request.SessionIds.Distinct().ToList();

        _logger.LogInformation("Applying late fees to {Count} selected session(s) as of {AsOf}",
            requestedIds.Count, evaluationDate);

        var sessions = await _sessionRepository.GetByIdsWithPartiesAsync(requestedIds);
        var byId = sessions.ToDictionary(s => s.Id);

        var applied = new List<LateFeeAppliedItem>();
        var skipped = new List<LateFeeSkippedItem>();

        foreach (var sessionId in requestedIds)
        {
            if (!byId.TryGetValue(sessionId, out var session))
            {
                skipped.Add(new LateFeeSkippedItem { SessionId = sessionId, Reason = "Session not found." });
                continue;
            }

            var outcome = await TryApplyLateFeeAsync(session, evaluationDate);
            if (outcome.Applied is not null) applied.Add(outcome.Applied);
            else skipped.Add(new LateFeeSkippedItem { SessionId = sessionId, Reason = outcome.SkipReason! });
        }

        return new ApplyLateFeesResult
        {
            AsOf = evaluationDate,
            Applied = applied,
            Skipped = skipped,
            AppliedCount = applied.Count,
            SkippedCount = skipped.Count,
            TotalFeeApplied = applied.Sum(a => a.FeeApplied),
        };
    }

    /// <summary>
    /// The ONE write path for a late fee — the batch loops it, so a future single-session
    /// endpoint cannot drift from batch behaviour.
    ///
    /// Eligibility is RE-DERIVED here rather than trusted from the preview, because the preview
    /// is a separate request and a caretaker may well have paid in between. When that happens
    /// the session is skipped WITH A REASON rather than silently: a manager who selected 12
    /// sessions and saw 9 charged needs to know that the other 3 were settled, not lost.
    /// </summary>
    private async Task<(LateFeeAppliedItem? Applied, string? SkipReason)> TryApplyLateFeeAsync(
        TherapySession session, DateOnly asOf)
    {
        if (session.LateFeeAppliedOn.HasValue)
        {
            return (null, "A late fee has already been applied to this session.");
        }
        if (session.AppointmentStatusId == SessionTransitionMoneyService.CancelledStatusId)
        {
            return (null, "Session is cancelled and is not billable.");
        }

        var balance = session.UnpaidBalanceBeforeLateFee();
        if (balance <= 0)
        {
            return (null, "Session has no unpaid balance — it was settled before the fee was applied.");
        }

        var daysUnpaid = asOf.DayNumber - session.SessionDate.DayNumber;
        if (daysUnpaid <= LateFeeGraceDays)
        {
            return (null, $"Session is only {daysUnpaid} day(s) old — still inside the {LateFeeGraceDays}-day grace period.");
        }

        var fee = CalculateLateFee(balance);
        if (fee <= 0)
        {
            return (null, "Computed fee rounds to zero.");
        }

        session.LateFeeAmount = fee;
        session.LateFeeAppliedOn = asOf;
        // The fee is 100% clinic revenue — the therapist is not paid for a payment delay.
        session.GrossProfit += fee;
        session.Notes = AppendMarker(session.Notes, string.Create(CultureInfo.InvariantCulture,
            $"{LateFeeMarkerPrefix} {asOf:yyyy-MM-dd}: {LateFeeRatePct:0}% of {balance:0.00} = {fee:0.00}; {daysUnpaid} days unpaid]"));

        // A landed fee re-opens a session that had been settled.
        session.IsPaidOff = session.AmountDue() <= 0;

        await _sessionRepository.UpdateAsync(session);

        _logger.LogInformation("Late fee {Fee} applied to session {SessionId} (balance {Balance}, {Days} days unpaid)",
            fee, session.Id, balance, daysUnpaid);

        return (new LateFeeAppliedItem
        {
            SessionId = session.Id,
            FeeApplied = fee,
            UnpaidBalanceBefore = balance,
            AmountDueAfter = session.AmountDue(),
        }, null);
    }

    // ── BR4: waive ───────────────────────────────────────────────────────────────────────

    public async Task<WaiveFeeResult> WaiveFeeAsync(int sessionId, WaiveFeeRequest request, int actingUserId)
    {
        // Parsed here rather than bound by the serializer — see WaiveFeeRequest.FeeKind. An
        // unrecognised value gets a message naming the legal ones, not a JSON path.
        if (!request.TryParseFeeKind(out var feeKind))
        {
            throw new ArgumentException(InvalidFeeKindMessage);
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException("A reason is required to waive a fee.");
        }
        var reason = SanitizeReason(request.Reason);
        if (reason.Length == 0)
        {
            throw new ArgumentException("A reason is required to waive a fee.");
        }

        var session = await _sessionRepository.GetByIdAsync(sessionId)
            ?? throw new NotFoundException("TherapySession", sessionId);

        var waiveLate = feeKind is SessionFeeKind.Late or SessionFeeKind.Both;
        var waiveNoShow = feeKind is SessionFeeKind.NoShow or SessionFeeKind.Both;

        // Validate BOTH legs before writing anything — a Both request that is only half-valid
        // must fail whole, not leave the session half-waived.
        decimal lateFeeToWaive = 0m;
        if (waiveLate)
        {
            if (!session.LateFeeAppliedOn.HasValue) throw new ArgumentException(NoLateFeeMessage);
            lateFeeToWaive = session.LateFeeAmount ?? 0m;
            if (lateFeeToWaive <= 0m) throw new ArgumentException(LateFeeAlreadyWaivedMessage);
        }

        decimal noShowFeeToWaive = 0m;
        if (waiveNoShow)
        {
            if (session.Notes?.Contains(NoShowMarkerPrefix, StringComparison.Ordinal) != true)
            {
                throw new ArgumentException(NoNoShowFeeMessage);
            }
            // The no-show fee lives in Amount; waiving it means discounting it away in full,
            // which is exactly what WP-42's G3 did — now as a gated, reasoned, audited action.
            noShowFeeToWaive = session.Amount - session.DiscountAmount;
            if (noShowFeeToWaive <= 0m) throw new ArgumentException(NoShowFeeAlreadyWaivedMessage);
        }

        // D3 — NARROW credit guard. WP-42 blocks any money transition when AmountPaid > 0; that
        // blanket rule would make BR4 unusable, because partial payment is the NORMAL state on a
        // late-fee session. Block only the case that actually breaks the books: a waive that
        // would leave the caretaker owed money.
        var amountDueAfter = session.AmountDue() - lateFeeToWaive - noShowFeeToWaive;
        if (amountDueAfter < 0m)
        {
            throw new ArgumentException(WouldCreateCreditMessage);
        }

        var waivedOn = ClinicClock.Today();

        // ONE marker covering however many legs were waived, rather than one per leg. A
        // two-leg waive is a single decision with a single reason, so repeating that reason
        // verbatim on a second line stored it twice in a Notes field that is already carrying
        // clinical text, import provenance and transition markers. Owner call, run-002.
        //
        // Shape is unchanged for the single-leg case, which is the overwhelming majority:
        //   [FEE-WAIVED 2026-08-08: late 37.50 waived by u#3; reason: …]
        //   [FEE-WAIVED 2026-08-08: late 25.50 + no-show 85.00 waived by u#3; reason: …]
        // Still one bracket-bounded marker, so the print sanitizer is unaffected.
        var waivedLegs = new List<string>();

        if (waiveLate)
        {
            // The amount goes to zero but LateFeeAppliedOn STAYS SET — that latch is what stops
            // the next batch run re-charging a fee a manager just forgave.
            session.LateFeeAmount = 0.00m;
            session.GrossProfit -= lateFeeToWaive;
            waivedLegs.Add(string.Create(CultureInfo.InvariantCulture, $"late {lateFeeToWaive:0.00}"));
        }

        if (waiveNoShow)
        {
            // Amount is left alone as the historical record; the discount absorbs the fee.
            session.DiscountAmount = session.Amount;
            session.GrossProfit -= noShowFeeToWaive;
            waivedLegs.Add(string.Create(CultureInfo.InvariantCulture, $"no-show {noShowFeeToWaive:0.00}"));
        }

        session.Notes = AppendMarker(session.Notes, string.Create(CultureInfo.InvariantCulture,
            $"{FeeWaivedMarkerPrefix} {waivedOn:yyyy-MM-dd}: {string.Join(" + ", waivedLegs)} waived by u#{actingUserId}; reason: {reason}]"));

        session.FeeWaivedOn = waivedOn;
        session.FeeWaivedByUserId = actingUserId;
        session.IsPaidOff = session.AmountDue() <= 0;

        // No covered-session guard here, deliberately: a no-show carries ProviderAmount = 0 so
        // payroll cannot have covered it, and the late leg touches nothing therapist-facing.
        // Do not "fix" this by copying WP-42's guard across.
        await _sessionRepository.UpdateAsync(session);

        _logger.LogInformation("Fee waived on session {SessionId} by user {UserId}: kind={Kind} late={Late} noShow={NoShow}",
            sessionId, actingUserId, feeKind, lateFeeToWaive, noShowFeeToWaive);

        return new WaiveFeeResult
        {
            SessionId = session.Id,
            FeeKind = feeKind.ToString(),
            LateFeeWaived = lateFeeToWaive,
            NoShowFeeWaived = noShowFeeToWaive,
            AmountDueAfter = session.AmountDue(),
            GrossProfitAfter = session.GrossProfit,
            WaivedOn = waivedOn,
            WaivedByUserId = actingUserId,
        };
    }

    // ── Marker plumbing ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strip anything that would break the marker's bracket-bounded shape, collapse whitespace,
    /// and cap the length.
    ///
    /// This is not cosmetic. Markers are parsed downstream by a bracket-bounded regex
    /// (<c>[^\]]*</c>), so a single <c>]</c> inside a free-text reason would truncate the match
    /// and leak the remainder of the marker onto caretaker-facing printed output. Newlines
    /// would split one marker across two lines and break the same matching.
    /// </summary>
    public static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return string.Empty;

        var cleaned = reason.Replace("[", string.Empty).Replace("]", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

        return cleaned.Length > MaxReasonLength
            ? cleaned[..MaxReasonLength].TrimEnd()
            : cleaned;
    }

    private static string AppendMarker(string? notes, string marker)
        => string.IsNullOrWhiteSpace(notes) ? marker : $"{notes}\n{marker}";
}
