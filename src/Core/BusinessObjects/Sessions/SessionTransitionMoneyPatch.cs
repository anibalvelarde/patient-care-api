namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

/// <summary>
/// WP-42: the money side-effect of a status transition into Cancelled (3) or NoShow (5),
/// prepared by <c>SessionTransitionMoneyService</c> (the ONE choke point) and applied verbatim
/// by whichever write path performs the transition. <see cref="NotesMarker"/> is the audit
/// stamp (<c>[CANCELLED-ZEROED …]</c> / <c>[NOSHOW-FEE …]</c>) to APPEND to the session's
/// Notes; null means the money write carries no marker (never the case today — a null PATCH,
/// not a null marker, is how "status-only transition" is expressed).
/// </summary>
public sealed record SessionTransitionMoneyPatch(
    decimal Amount,
    decimal DiscountAmount,
    decimal ProviderAmount,
    decimal GrossProfit,
    string? NotesMarker);
