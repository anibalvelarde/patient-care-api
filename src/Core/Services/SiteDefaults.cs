namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-49 (BR1): the ONE place Site column defaults live on the API side, so a policy change is
/// a one-line edit here instead of a hunt through five files.
///
/// This type exists because of what BR1 cost. WP-42 shipped <c>Site.NoShowFeePct</c> with a
/// default of 30.00 written out as a literal in five separate places — the entity initializer,
/// the profile DTO initializer, the create-service <c>??</c> fallback, the transition service's
/// own named constant, and the controller's "is this a privileged value?" pivot. Moving the
/// default to 100.00 therefore was not a data-only change, and missing any one site would have
/// left a silent disagreement about what "the default" means.
///
/// Values here must stay in lockstep with the DB column defaults (V033 for
/// <see cref="NoShowFeePct"/>). The DB is authoritative for rows that already exist; these
/// constants only decide what a NEW site gets and what the API assumes when a session has no
/// Site on file.
/// </summary>
public static class SiteDefaults
{
    /// <summary>
    /// Default no-show fee as a PERCENT of the booked gross Amount — 100 means "the full
    /// session price", not "$100" (BR1, owner ruling 2026-08-01; DB default set by V033).
    ///
    /// Raised from 30.00, which came from the legacy workbooks' "Penalidad ~30%" column and
    /// was never the clinic's actual policy: a no-show consumes the slot and the therapist's
    /// time, so it bills in full. The per-site knob is deliberately retained.
    /// </summary>
    public const decimal NoShowFeePct = 100.00m;
}
