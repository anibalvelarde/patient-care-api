using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// "Paid in range" rollup for a therapist + range (WP-20): sessions already covered by prior
/// service payments, so the payable list can reconcile against the raw data.
/// <c>TotalApplied</c> is the invariant term — Σ <c>AmountApplied</c> across ALL completed
/// sessions in range (including partials still listed as payable), so that
/// Σ ProviderAmount = Σ RemainingProviderAmount (payable) + <c>TotalApplied</c>.
/// </summary>
public class PaidInRangeSummary
{
    /// <summary>How many sessions in range are fully paid (remaining &lt;= 0).</summary>
    public int FullyPaidSessionCount { get; set; }

    /// <summary>Σ ProviderAmount of the fully-paid sessions.</summary>
    public decimal FullyPaidTotal { get; set; }

    /// <summary>Σ AmountApplied across all sessions in range (incl. partially-paid ones).</summary>
    public decimal TotalApplied { get; set; }

    /// <summary>Every session with any allocation (fully or partially paid), with payment refs.</summary>
    public List<PaidProviderSessionDetail> Sessions { get; set; } = new();
}
