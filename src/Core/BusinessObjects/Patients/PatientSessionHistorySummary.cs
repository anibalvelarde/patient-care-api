namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>
/// WP-21 (F1): one row of the Patients → Session History tab's patient list — patient identity
/// plus session-recency aggregates. <see cref="LastSessionDate"/> null with
/// <see cref="TotalSessions"/> 0 means the patient has never had a session (such rows sort last).
/// </summary>
public class PatientSessionHistorySummary
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
    public DateOnly? LastSessionDate { get; set; }
    public int TotalSessions { get; set; }
}
