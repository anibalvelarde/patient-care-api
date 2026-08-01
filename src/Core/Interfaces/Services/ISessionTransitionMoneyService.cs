using Neurocorp.Api.Core.BusinessObjects.Sessions;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>
/// WP-42: the ONE shared choke point for money-at-transition semantics. Every path that moves
/// a session into Cancelled (3) or NoShow (5) — /cancel, /noshow, /confirm with result
/// Declined, and a generic PUT whose appointmentStatusId changes to 3/5 — must call
/// <see cref="PrepareTransitionAsync"/> and apply the returned patch atomically with the
/// status write. The covered-session lock (G5) additionally consults
/// <see cref="IsCoveredByServicePaymentAsync"/> on money/therapist edits.
/// </summary>
public interface ISessionTransitionMoneyService
{
    /// <summary>
    /// Runs the WP-42 guards and computes the money side-effect for a transition of
    /// <paramref name="session"/> into <paramref name="targetStatusId"/>.
    /// Returns null when the transition carries no money write (target is not 3/5, or the
    /// transition is an idempotent re-entry — already zeroed / fee already applied).
    /// Throws <see cref="ArgumentException"/> (→ 400) when a guard blocks: payments recorded
    /// (<c>AmountPaid &gt; 0</c>) or non-reversed payroll allocations exist. When it throws,
    /// NOTHING may be written by the caller.
    /// </summary>
    Task<SessionTransitionMoneyPatch?> PrepareTransitionAsync(TherapySession session, int targetStatusId);

    /// <summary>
    /// True while the session has non-reversed payroll allocations (net
    /// <c>SessionServicePayment.AmountApplied &gt; 0</c>) — the WP-42 covered-session lock
    /// predicate. Reversals (WP-14.5 negative mirrors) cancel their originals out.
    /// </summary>
    Task<bool> IsCoveredByServicePaymentAsync(int sessionId);
}
