using System.Globalization;
using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-42: money-at-transition semantics — the ONE choke point shared by all four paths into
/// Cancelled (3) / NoShow (5): <c>/cancel</c>, <c>/noshow</c>, <c>/confirm</c> with result
/// Declined, and the generic <c>PUT /api/sessions/{id}</c> whose appointmentStatusId changes
/// to 3/5.
///
/// Rules (contract: sessions-api.md "WP-42 money-at-transition semantics", 2026-07-31):
/// - Guards first, nothing changes when blocked: AmountPaid &gt; 0 (refund territory) and
///   non-reversed payroll allocations (reverse the covering payment first) both → 400.
/// - Cancel/Declined (→ 3): zero Amount/Discount/ProviderAmount/GrossProfit, stamping the
///   originals into Notes (<c>[CANCELLED-ZEROED yyyy-MM-dd: was A:.. D:.. P:.. G:..]</c>).
/// - NoShow (→ 5): Amount = round(Site.NoShowFeePct/100 × original gross Amount, 2),
///   Discount = 0, ProviderAmount = 0 (therapist rendered nothing — G2), GrossProfit = Amount
///   (clinic keeps 100%), stamped <c>[NOSHOW-FEE …]</c>.
/// - Idempotent: a transition that would change nothing (already zeroed; fee already applied —
///   detected by the NOSHOW-FEE marker so the fee is never compounded) is status-only: no
///   patch, no duplicate marker.
/// - These writes set the fields DIRECTLY — deliberately NOT routed through the WP-40
///   <see cref="SessionMoneyMath"/> booking derivation (no SENADIS floor, no fee-on-net here).
/// - Transitions OUT of 3/5 restore nothing (originals live in the marker).
/// </summary>
public class SessionTransitionMoneyService : ISessionTransitionMoneyService
{
    public const int CancelledStatusId = SessionStatus.Cancelled;
    public const int NoShowStatusId = SessionStatus.NoShow;

    /// <summary>
    /// Default fee pct when the session has no Site on file — mirrors the DB column default
    /// (V032, raised 30 → 100 by V033/WP-49). The name is kept because tests reference it.
    /// </summary>
    public const decimal DefaultNoShowFeePct = SiteDefaults.NoShowFeePct;

    /// <summary>Wire-exact 400 message for the AmountPaid guard (contract copy — the UI matches on it).</summary>
    public const string PaymentsRecordedMessage =
        "Payments are recorded against this session; money-moved cancellations are refund territory.";

    /// <summary>Wire-exact 400 message for the covered-session guard AND the G5 edit lock (contract copy).</summary>
    public const string CoveredSessionMessage =
        "This session is covered by a service payment — reverse the covering payment first.";

    /// <summary>
    /// Wire-exact 400 message for the WP-49 active-fee guard on cancellation (contract copy —
    /// the UI surfaces it verbatim). Names the required action, since the operator hitting it
    /// may not hold the claim to perform it.
    /// </summary>
    public const string ActiveFeeMessage =
        "This session carries an active fee — a manager must waive the fee before it can be cancelled.";

    private const string CancelledMarkerPrefix = "[CANCELLED-ZEROED";
    // The single definition lives on the entity (see TherapySession.HasNoShowFeeMarker).
    private const string NoShowMarkerPrefix = TherapySession.NoShowFeeMarkerPrefix;

    private readonly ISessionServicePaymentRepository _sessionServicePaymentRepository;
    private readonly IRepository<Site> _siteRepository;

    public SessionTransitionMoneyService(
        ISessionServicePaymentRepository sessionServicePaymentRepository,
        IRepository<Site> siteRepository)
    {
        _sessionServicePaymentRepository = sessionServicePaymentRepository;
        _siteRepository = siteRepository;
    }

    public async Task<bool> IsCoveredByServicePaymentAsync(int sessionId)
    {
        var allocations = await _sessionServicePaymentRepository.GetBySessionIdsAsync(new[] { sessionId });
        // Net > 0 = a non-reversed allocation still covers the session; WP-14.5 reversals are
        // negative mirror rows, so a reversed payment nets back to 0.
        return allocations.Sum(a => a.AmountApplied) > 0;
    }

    public async Task<SessionTransitionMoneyPatch?> PrepareTransitionAsync(TherapySession session, int targetStatusId)
    {
        if (targetStatusId != CancelledStatusId && targetStatusId != NoShowStatusId)
        {
            return null;
        }

        // Guards FIRST — when either throws, the caller must write nothing at all.
        if (session.AmountPaid > 0)
        {
            throw new ArgumentException(PaymentsRecordedMessage);
        }
        if (await IsCoveredByServicePaymentAsync(session.Id))
        {
            throw new ArgumentException(CoveredSessionMessage);
        }

        // WP-49 (owner ruling 2026-08-08): a session carrying an ACTIVE fee cannot be
        // cancelled. Waive the fee first — a manager-only act with a recorded reason — and the
        // cancellation then proceeds normally for FD/AM/MGR.
        //
        // This replaces the interim "cancel silently keeps the fee" behaviour. Both avoid the
        // real hazard (any Appointments.Book holder erasing a manager's fee by cancelling), but
        // blocking is the honest one: keeping the fee left a live receivable attached to a
        // session the clinic had just voided, which is a state nobody would think to look for.
        // Failing loudly forces the fee decision to be made explicitly, by someone entitled to
        // make it, before the session disappears from the schedule.
        if (targetStatusId == CancelledStatusId && HasActiveFee(session))
        {
            throw new ArgumentException(ActiveFeeMessage);
        }

        return targetStatusId == CancelledStatusId
            ? PrepareCancel(session)
            : await PrepareNoShowAsync(session);
    }

    /// <summary>
    /// WP-49: does this session carry a fee that is still COLLECTIBLE?
    ///
    /// Deliberately keyed on the amounts, not on <c>TherapySession.CarriesFee()</c>. CarriesFee
    /// uses the latch and the marker, so it stays true forever once a fee has existed — right
    /// for the discount-edit guard, wrong here. A session whose fee was waived has
    /// <c>LateFeeAmount == 0</c> and, for a no-show, <c>DiscountAmount == Amount</c>; nothing is
    /// owed, so cancelling is fine. That is precisely what makes waive-then-cancel work.
    /// </summary>
    public static bool HasActiveFee(TherapySession session)
    {
        if ((session.LateFeeAmount ?? 0m) > 0m) return true;

        return session.HasNoShowFeeMarker() && session.Amount - session.DiscountAmount > 0m;
    }

    private static SessionTransitionMoneyPatch? PrepareCancel(TherapySession session)
    {
        // Any active fee has already been rejected by the guard in PrepareTransitionAsync, so
        // by here the session owes nothing beyond its service charge and GrossProfit goes to 0
        // exactly as it did before WP-49. A waived late fee is 0.00 and contributes nothing.

        // Idempotent: an already-zero session (12338 pattern, or a re-cancel) transitions
        // status-only — no re-stamp, no duplicate marker.
        if (session.Amount == 0m && session.DiscountAmount == 0m
            && session.ProviderAmount == 0m && session.GrossProfit == 0m)
        {
            return null;
        }

        return new SessionTransitionMoneyPatch(0m, 0m, 0m, 0m,
            BuildMarker(CancelledMarkerPrefix, session));
    }

    private async Task<SessionTransitionMoneyPatch?> PrepareNoShowAsync(TherapySession session)
    {
        // Fee already applied (marker on file) — e.g. a 5→2→5 correction round-trip. The fee
        // must never compound off itself; staff adjust it via the gated BK-3 discount edit (G3).
        if (session.HasNoShowFeeMarker())
        {
            return null;
        }

        var pct = await ResolveNoShowFeePctAsync(session);
        var fee = NoShowFee(pct, session.Amount);

        // WP-49: an existing late fee survives the transition and stays in GrossProfit. The
        // no-show fee lands in Amount; the late fee lives in its own column and is added on
        // top, so the two never overwrite each other. (A session CAN carry both — an unpaid
        // no-show fee can itself go late, which is why waive takes a fee-kind.)
        var lateFee = session.LateFeeAmount ?? 0m;

        // Idempotent: nothing would change (e.g. an all-zero cancelled row no-showed at pct 0).
        if (session.Amount == fee && session.DiscountAmount == 0m
            && session.ProviderAmount == 0m && session.GrossProfit == fee + lateFee)
        {
            return null;
        }

        return new SessionTransitionMoneyPatch(fee, 0m, 0m, fee + lateFee,
            BuildMarker(NoShowMarkerPrefix, session));
    }

    /// <summary>
    /// round(pct% × gross amount, 2), half away from zero — matches MySQL ROUND (the WP-42A
    /// rehearsal pinned 30% of 85.00 = 25.50 and 12.5% of 85.00 = 10.63).
    /// </summary>
    public static decimal NoShowFee(decimal pct, decimal grossAmount)
        => Math.Round(pct / 100m * grossAmount, 2, MidpointRounding.AwayFromZero);

    private async Task<decimal> ResolveNoShowFeePctAsync(TherapySession session)
    {
        if (!session.SiteId.HasValue)
        {
            return DefaultNoShowFeePct;
        }
        var site = await _siteRepository.GetByIdAsync(session.SiteId.Value);
        return site?.NoShowFeePct ?? DefaultNoShowFeePct;
    }

    // Marker date is the CLINIC's (Panama) calendar day — the deployed container runs UTC and
    // would otherwise stamp tomorrow's date from 19:00 local onward (the WP-36 G6 lesson).
    private static string BuildMarker(string prefix, TherapySession session)
        => string.Create(CultureInfo.InvariantCulture,
            $"{prefix} {PanamaToday():yyyy-MM-dd}: was A:{session.Amount:0.00} D:{session.DiscountAmount:0.00} P:{session.ProviderAmount:0.00} G:{session.GrossProfit:0.00}]");

    // WP-49: the Panama timezone ladder that used to live here moved to ClinicClock so the
    // late-fee clock shares it rather than growing a second copy. Pure move — the existing
    // marker-date tests pass unmodified, which is the proof.
    private static DateOnly PanamaToday() => ClinicClock.Today();
}
