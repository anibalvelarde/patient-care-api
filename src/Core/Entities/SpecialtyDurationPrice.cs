namespace Neurocorp.Api.Core.Entities;

/// <summary>
/// WP-39 (PR-1): temporal per-duration price sheet row for a specialty. Append-only history —
/// rows are never mutated; the applicable price for a session is the row with the latest
/// <see cref="EffectiveFrom"/> ≤ the SESSION date (not the booking-creation date). Durations
/// {30,45,60,90,120} and Amount ≥ 0 are API-enforced (no DB CHECKs — V030).
/// </summary>
public class SpecialtyDurationPrice : AuditableEntityBase
{
    // FK column is SpecialtyTypeID; the referenced PK is SpecialtyType.SpecialtyID
    // (same pattern as TherapistSpecialty).
    public int SpecialtyTypeId { get; set; }
    public int DurationMinutes { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public SpecialtyType? SpecialtyType { get; set; }
}
