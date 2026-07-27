using System.Text.Json.Serialization;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-35 addendum: envelope-level aggregates for GET /api/patients/session-history, computed
/// over the FULL filtered set (respects the <c>search</c> param — not just the current page).
/// <see cref="SessionCount"/> is always present; the money fields ride
/// <c>Appointments.ProviderAmount</c> (WP-17 discipline) — ProviderAmountResultFilter nulls
/// them for callers without the claim, and WhenWritingNull omits them from the JSON.
/// </summary>
public class SessionHistoryTotals
{
    public int SessionCount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? GrossAmount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DiscountAmount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? GrossProfit { get; set; }
}
