using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// Read-only "Pending Pay" report (WP-14.5): the per-therapist decomposition behind the
/// "Pending therapist payments" dashboard tile. One row per active therapist with completed
/// sessions still owing them, plus grand totals. Payout-derived, so MGR/AM only (FD excluded).
/// Population = the completed sessions that still owe the therapist (the same set that drives
/// <see cref="PendingPayrollSummary"/>); all per-row figures describe that set.
/// </summary>
public class PendingPayReport
{
    public List<PendingPayReportRow> Rows { get; set; } = new();

    public int TherapistCount { get; set; }
    public int SessionCount { get; set; }
    /// <summary>Σ session <c>Amount</c> (gross billed, pre-discount) across all rows.</summary>
    public decimal TotalGrossBilled { get; set; }
    /// <summary>Σ remaining provider amount owed across all rows (ties to the tile's total).</summary>
    public decimal TotalOwed { get; set; }
    /// <summary><see cref="TotalOwed"/> ÷ <see cref="TotalGrossBilled"/> × 100, rounded to 1 dp.</summary>
    public decimal OwedPctOfGross { get; set; }
}

public class PendingPayReportRow
{
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    /// <summary>Earliest owing-session date (yyyy-MM-dd).</summary>
    public string FirstSessionDate { get; set; } = string.Empty;
    /// <summary>Latest owing-session date (yyyy-MM-dd).</summary>
    public string LastSessionDate { get; set; } = string.Empty;
    public int DistinctPatientCount { get; set; }
    /// <summary>Σ session <c>Amount</c> (gross billed, pre-discount) — "amount the clinic made".</summary>
    public decimal GrossBilled { get; set; }
    /// <summary>Σ (ProviderAmount − already-applied) — amount still owed to the therapist.</summary>
    public decimal AmountOwed { get; set; }
    /// <summary><see cref="AmountOwed"/> ÷ <see cref="GrossBilled"/> × 100, rounded to 1 dp (0 when gross is 0).</summary>
    public decimal OwedPctOfGross { get; set; }
}
