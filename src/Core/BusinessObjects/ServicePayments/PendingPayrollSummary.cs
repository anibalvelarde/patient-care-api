namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// Clinic-wide rollup of what is still owed to therapists for completed sessions: the headline
/// figure for the "Pending therapist payments" dashboard tile (WP-14.5). Derives from
/// ProviderAmount, so it follows the same confidentiality rule as the rest of Service Payments
/// (MGR/AM only, FD excluded).
/// </summary>
public class PendingPayrollSummary
{
    /// <summary>Gross provider amount still owed across all active therapists in range.</summary>
    public decimal TotalPending { get; set; }

    /// <summary>How many therapists are owed at least some provider amount in range.</summary>
    public int TherapistCount { get; set; }

    /// <summary>How many completed sessions still owe a therapist in range.</summary>
    public int SessionCount { get; set; }
}
