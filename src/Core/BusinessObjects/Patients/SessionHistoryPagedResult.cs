using Neurocorp.Api.Core.BusinessObjects.Common;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-35 addendum: endpoint-specific envelope for GET /api/patients/session-history — the
/// shared WP-21 <see cref="PagedResult{T}"/> shape plus a <see cref="Totals"/> block over the
/// full filtered set. Deliberately a DERIVED type so every other paged endpoint keeps the
/// plain envelope untouched, and so ProviderAmountResultFilter can key its money shaping on a
/// closed type-match (same discipline as the PagedResult-of-SessionEvent arm).
/// </summary>
public class SessionHistoryPagedResult : PagedResult<PatientSessionHistorySummary>
{
    public SessionHistoryTotals Totals { get; set; } = new();
}
