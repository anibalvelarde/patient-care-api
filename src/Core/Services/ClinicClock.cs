namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-49: the ONE source of the clinic's calendar day. Extracted verbatim from
/// <see cref="SessionTransitionMoneyService"/>, which had grown a private copy of the Panama
/// timezone ladder.
///
/// Why this is shared rather than copied again: the deployed container runs UTC, so from 19:00
/// Panama time onward <c>DateTime.UtcNow.Date</c> is already tomorrow. Every WP that has
/// stamped a date has hit this — WP-26 (the backup cron firing a day early), WP-36 (MRN mint
/// dates), WP-40 (booking dates), WP-42 (transition markers). A second private copy of the
/// ladder is exactly the drift trap those WPs warn about, and the late-fee clock (BR3) needs
/// the same business day the markers use.
///
/// The 6-day late-fee clock is deliberately date arithmetic in the CLINIC's calendar, not the
/// UTC <c>DateTime</c> subtraction that <c>TherapySession.IsPastDeadline()</c> uses for the
/// 35-day delinquency window. Those two clocks are independent by owner ruling (a session is
/// surcharged on day 7 but only delinquent on day 36), so they are not being unified here —
/// only the timezone resolution is.
/// </summary>
public static class ClinicClock
{
    /// <summary>The clinic's (Panama) current calendar day.</summary>
    public static DateOnly Today()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PanamaTimeZone));

    /// <summary>The clinic's timezone, resolved once at startup.</summary>
    public static TimeZoneInfo PanamaTimeZone { get; } = ResolvePanamaTimeZone();

    // IANA id first (Linux container), Windows id as fallback, fixed UTC-5 as the last resort
    // (slim k3s images may lack tzdata — the WP-26/WP-36 lesson; Panama has no DST).
    private static TimeZoneInfo ResolvePanamaTimeZone()
    {
        foreach (var id in new[] { "America/Panama", "SA Pacific Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { /* try the next id */ }
            catch (InvalidTimeZoneException) { /* corrupt zone data — try the next id */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Panama (fixed)", TimeSpan.FromHours(-5), "Panama (UTC-5)", "Panama (UTC-5)");
    }
}
