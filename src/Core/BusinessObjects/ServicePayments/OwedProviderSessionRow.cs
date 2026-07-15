namespace Neurocorp.Api.Core.BusinessObjects.ServicePayments;

/// <summary>
/// WP-29 (U3): slim per-session money row for the pending-payroll summary/report — only the
/// columns the aggregations need, with the allocation sum computed in SQL. Remaining owed =
/// ProviderAmount − Applied (always &gt; 0: the repository filters to owing rows).
/// </summary>
public class OwedProviderSessionRow
{
    public int SessionId { get; set; }
    public int TherapistId { get; set; }
    public int PatientId { get; set; }
    public DateOnly SessionDate { get; set; }
    public decimal Amount { get; set; }
    public decimal ProviderAmount { get; set; }
    public decimal Applied { get; set; }
}
