using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

/// <summary>
/// WP-39: PUT /api/lookups/specialty-types/{id}/prices — APPEND-ONLY. Each row adds a new
/// effective-dated price; history is never mutated (a correction is a new row effective today
/// or the intended date; a future-dated row schedules a price change). A (durationMinutes,
/// effectiveFrom) pair already on file — or repeated within the request — is a 409.
/// </summary>
public class SpecialtyPricesPutRequest
{
    [Required]
    [MinLength(1)]
    public List<SpecialtyPriceAppendRow> Prices { get; set; } = [];
}

public class SpecialtyPriceAppendRow
{
    // WP-39 ruling: durations are API-enforced (no DB CHECK); a future 75-min offering is a
    // matrix-of-values change here, not a migration.
    [AllowedValues(30, 45, 60, 90, 120)]
    public int DurationMinutes { get; set; }

    [Range(0, 99999999.99)]
    public decimal Amount { get; set; }

    // Date-only by construction (DateOnly rejects time components at JSON binding);
    // nullable + [Required] so an omitted field is a 400, not a silent 0001-01-01.
    [Required]
    public DateOnly? EffectiveFrom { get; set; }
}
