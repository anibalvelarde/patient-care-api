using System.Collections.Generic;

namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// Envelope for GET /api/service-payments/unpaid-sessions (WP-20): the payable list that
/// drives the "Pay Therapist" wizard (element shape unchanged) plus a "paid in range"
/// summary of sessions already covered by prior service payments, so the UI can always
/// reconcile the range: Σ ProviderAmount = Σ RemainingProviderAmount + PaidInRange.TotalApplied.
/// </summary>
public class UnpaidProviderSessionsResult
{
    /// <summary>Completed sessions that still owe the therapist (remaining &gt; 0), ordered by date/time.</summary>
    public List<UnpaidProviderSessionSummary> Sessions { get; set; } = new();

    /// <summary>Sessions in range already (fully or partially) paid, with the covering payment refs.</summary>
    public PaidInRangeSummary PaidInRange { get; set; } = new();
}
