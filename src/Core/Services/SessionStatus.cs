namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-55 (B-1): the ONE home for AppointmentStatus lookup ids, their canonical names, and the
/// derived status sets. Before this, the same seven ids and their names were re-encoded across the
/// codebase — three private const blocks (this service pair, <c>TreatmentPlanService</c>,
/// <c>BulkSchedulingService</c>), four separate copies of the "confirmed" set
/// (<c>BookingService</c>, <c>BookingRepository</c>, <c>SessionEventRepository</c>, an inline
/// <c>HashSet</c> in <c>SessionEventHandler</c>), ~10 bare <c>== 3</c> / <c>== 4</c> literals in
/// <c>TherapySessionRepository</c> and friends, and a parallel NAME-string encoding
/// (<c>TherapistStatementService</c>, <c>ServicePaymentService</c>, <c>?? "Completed"</c>
/// fallbacks). One value typed in a dozen places drifts in one.
///
/// Ids and names are the ground truth captured from prod on 2026-08-23
/// (<c>patient-care-db/tools/wp-55/lookup-ground-truth.md</c>). WP-55 B-3 adds a startup task that
/// re-verifies this map against the live <c>AppointmentStatus</c> rows and flips the
/// <c>lookupSeed</c> health check Unhealthy on any mismatch — so a future seed change can't silently
/// diverge from these constants.
///
/// NOTE — this is the SESSION/appointment lifecycle status, NOT the treatment-plan status
/// (Draft/Active/Completed/Cancelled, which lives on <see cref="Entities.TreatmentPlan"/>).
/// </summary>
public static class SessionStatus
{
    // --- Ids (AppointmentStatus.AppointmentStatusID) -------------------------------------------
    public const int Proposed = 1;
    public const int Confirmed = 2;
    public const int Cancelled = 3;
    public const int Completed = 4;
    public const int NoShow = 5;
    public const int CheckedIn = 6;
    public const int InTherapy = 7;

    // --- Names (AppointmentStatus.StatusName) --------------------------------------------------
    public static class Names
    {
        public const string Proposed = "Proposed";
        public const string Confirmed = "Confirmed";
        public const string Cancelled = "Cancelled";
        public const string Completed = "Completed";
        public const string NoShow = "NoShow";
        public const string CheckedIn = "CheckedIn";
        public const string InTherapy = "InTherapy";
    }

    /// <summary>
    /// Canonical id → name map. Verified against live rows by the WP-55 B-3 startup check.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> IdToName = new Dictionary<int, string>
    {
        [Proposed] = Names.Proposed,
        [Confirmed] = Names.Confirmed,
        [Cancelled] = Names.Cancelled,
        [Completed] = Names.Completed,
        [NoShow] = Names.NoShow,
        [CheckedIn] = Names.CheckedIn,
        [InTherapy] = Names.InTherapy,
    };

    /// <summary>
    /// Statuses that count a slot as taking place / confirmed for the schedule
    /// (Confirmed, Completed, CheckedIn, InTherapy). Was copied verbatim four times as
    /// <c>[2, 4, 6, 7]</c>.
    /// </summary>
    public static readonly IReadOnlySet<int> ConfirmedStatuses =
        new HashSet<int> { Confirmed, Completed, CheckedIn, InTherapy };

    /// <summary>
    /// Statuses treated as a live/pending booking on the board
    /// (Proposed, Confirmed, CheckedIn, InTherapy) — excludes the terminal Cancelled/Completed/NoShow.
    /// </summary>
    public static readonly IReadOnlySet<int> ActiveStatuses =
        new HashSet<int> { Proposed, Confirmed, CheckedIn, InTherapy };
}
