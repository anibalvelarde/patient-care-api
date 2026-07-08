using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// One therapist's rollup for the "Run Payroll" preview: how many completed sessions still owe them
/// in the window and the total remaining provider amount, plus the underlying session detail.
/// </summary>
public class PayrollPreviewTherapist
{
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public int SessionCount { get; set; }
    public decimal TotalRemaining { get; set; }

    /// <summary>Fully-paid sessions the therapist has in the window (WP-20, context only).</summary>
    public int PaidSessionCount { get; set; }

    /// <summary>Σ AmountApplied across all of the therapist's in-range sessions (WP-20, context only).</summary>
    public decimal PaidTotal { get; set; }

    public List<UnpaidProviderSessionSummary> Sessions { get; set; } = new();
}
