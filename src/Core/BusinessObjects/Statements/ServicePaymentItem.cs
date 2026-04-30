namespace Neurocorp.Api.Core.BusinessObjects.Statements;

/// <summary>
/// Shape reserved for the Therapist Payroll WP. In the WP-13B pro-forma
/// scope, the <c>TherapistStatement.ServicePayments</c> list is always empty.
/// Once WP-14 ships, the service will populate this from a new
/// <c>ServicePayment</c> table that records disbursements to therapists.
/// </summary>
public class ServicePaymentItem
{
    public int ServicePaymentId { get; set; }
    public string PaymentDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentTypeName { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<ServicePaymentAllocation> Allocations { get; set; } = new();
}
