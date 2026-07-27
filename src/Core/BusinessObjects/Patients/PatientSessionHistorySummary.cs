using System.Text.Json.Serialization;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-21 (F1): one row of the Patients → Session History tab's patient list — patient identity
/// plus session-recency aggregates. <see cref="LastSessionDate"/> null with
/// <see cref="TotalSessions"/> 0 means the patient has never had a session (such rows sort last).
/// WP-35 (SH-1): <see cref="FirstSessionDate"/> — earliest session of ANY status (owner ruling
/// G1, consistent with <see cref="TotalSessions"/>); null for zero-session patients.
/// WP-35 addendum: lifetime money totals (SUMs of the STORED per-session TherapySession
/// columns, any status — same population as <see cref="TotalSessions"/>; zero-session
/// patients get 0). The money fields ride <c>Appointments.ProviderAmount</c> (owner ruling,
/// WP-17 discipline): ProviderAmountResultFilter nulls them for callers without the claim and
/// WhenWritingNull omits them from the JSON — counts always survive.
/// </summary>
public class PatientSessionHistorySummary
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
    public DateOnly? FirstSessionDate { get; set; }
    public DateOnly? LastSessionDate { get; set; }
    public int TotalSessions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? GrossAmount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? DiscountAmount { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? GrossProfit { get; set; }
}
