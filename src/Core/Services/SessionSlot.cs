namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-59: the ONE definition of "two sessions clash". Shared by the single-session double-booking
/// guard (<see cref="SessionConflictGuard"/>) and the treatment-plan bulk scheduler.
/// </summary>
public static class SessionSlot
{
    /// <summary>
    /// Half-open time ranges on the same day: <c>[start, start + duration)</c>. Back-to-back
    /// (10:00–10:30, then 10:30) does NOT overlap.
    /// </summary>
    public static bool Overlaps(TimeOnly start1, int duration1, TimeOnly start2, int duration2)
    {
        var end1 = start1.AddMinutes(duration1);
        var end2 = start2.AddMinutes(duration2);
        // TimeOnly wraps at midnight; clinic sessions never cross it, but clamp so a late legacy
        // row can't wrap to 00:xx and appear to end before it starts.
        if (end1 <= start1) end1 = TimeOnly.MaxValue;
        if (end2 <= start2) end2 = TimeOnly.MaxValue;
        return start1 < end2 && start2 < end1;
    }

    /// <summary>
    /// Owner ruling 2026-11-08: every status except Cancelled occupies its slot (Proposed, Confirmed,
    /// CheckedIn, InTherapy, Completed and NoShow).
    /// </summary>
    public static bool OccupiesSlot(int appointmentStatusId) => appointmentStatusId != SessionStatus.Cancelled;
}
